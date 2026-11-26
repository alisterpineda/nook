using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Nook.Api.Data;

namespace Nook.Api.Features.Auth;

[ApiController]
[Route("api/auth")]
public class AuthController(
    NookDbContext db,
    UserManager<NookUser> users,
    SignInManager<NookUser> signIn,
    IOptions<OidcOptions> oidc,
    IOptions<PasswordSignInOptions> passwordSignIn,
    ILogger<AuthController> logger) : ControllerBase
{
    /// <summary>How someone may sign in: through single sign-on, and whether a password works.</summary>
    [HttpGet("sign-in-options")]
    [AllowAnonymous]
    [ProducesResponseType<SignInOptionsResponse>(StatusCodes.Status200OK)]
    public SignInOptionsResponse SignInOptions() =>
        new(oidc.Value.Enabled ? oidc.Value.DisplayName : null, passwordSignIn.Value.Enabled);

    /// <summary>
    /// Starts single sign-on: redirects to the provider's authorization endpoint, or back to the sign-in page when
    /// the provider cannot be reached. Not found when single sign-on is off.
    /// </summary>
    [HttpGet("oidc/login")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> OidcLogin()
    {
        if (!oidc.Value.Enabled)
        {
            return NotFound();
        }

        // The challenge runs here rather than as a returned Challenge result, which would run after this action
        // has returned, where a provider that cannot be reached could not be caught.
        try
        {
            await HttpContext.ChallengeAsync(OidcSignIn.Scheme, new AuthenticationProperties { RedirectUri = "/" });
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Single sign-on could not start at the provider: {Reason}", exception.Message);
            return Redirect("/sign-in?error=sso-failed");
        }

        // The handler has written the redirect to the provider.
        return new EmptyResult();
    }

    /// <summary>Signs in with an email and password, and sets the session cookie. Not found when password sign-in is off.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [RequiresPasswordSignIn]
    [EnableRateLimiting(RateLimits.Auth)]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<CurrentUserResponse>> Login(LoginRequest request)
    {
        // The user name is the email, which is what keeps emails unique.
        var user = await users.FindByNameAsync(request.Email);

        if (user is null)
        {
            // Hashing anyway makes an unknown email take as long as a wrong password, so timing does not tell them apart.
            users.PasswordHasher.HashPassword(new NookUser(), request.Password);
            return Refused();
        }

        // Identity calls this cookie the "two-factor remembered" client; Nook has no two-factor sign-in and uses it as
        // the mark of a browser that has signed in with a password. A known browser skips the account lock, so a
        // stranger guessing at an email cannot keep its owner out. Its wrong passwords are not counted toward the lock,
        // and its right one does not reset the failed count or end the lock: that would give a guessing stranger five
        // more tries each time the user signed in.
        var known = await signIn.IsTwoFactorClientRememberedAsync(user);
        var right = known
            ? await users.CheckPasswordAsync(user, request.Password)
            : (await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true)).Succeeded;

        // A wrong password, an unknown email and a locked account all get the same answer.
        if (!right)
        {
            return Refused();
        }

        await signIn.SignInAsync(user, isPersistent: true);

        // Written on every successful password sign-in, which renews it and makes this browser known for this user only.
        await signIn.RememberTwoFactorClientAsync(user);
        return await CurrentAsync(user);
    }

    private ObjectResult Refused() =>
        Problem(statusCode: StatusCodes.Status401Unauthorized, title: "The email or password is incorrect.");

    /// <summary>Signs out and clears the session cookie.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return NoContent();
    }

    /// <summary>The signed-in user.</summary>
    [HttpGet("me")]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CurrentUserResponse>> Me()
    {
        var user = await users.GetUserAsync(User);
        return user is null ? Unauthorized() : await CurrentAsync(user);
    }

    private Task<CurrentUserResponse> CurrentAsync(NookUser user) =>
        CurrentUserResponse.FromAsync(db, user, HttpContext.RequestAborted);
}
