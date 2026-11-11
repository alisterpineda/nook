using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Nook.Api.Data;

namespace Nook.Api.Features.Auth;

[ApiController]
[Route("api/auth")]
public class AuthController(UserManager<NookUser> users, SignInManager<NookUser> signIn) : ControllerBase
{
    /// <summary>Signs in with an email and password, and sets the session cookie.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.Auth)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<CurrentUserResponse>> Login(LoginRequest request)
    {
        // The user name is the email, which is what keeps emails unique.
        var user = await users.FindByNameAsync(request.Email);

        if (user is null)
        {
            // Hashing anyway makes an unknown email take as long as a wrong password, so timing does not tell them apart.
            users.PasswordHasher.HashPassword(new NookUser(), request.Password);
        }

        // A wrong password, an unknown email and a locked account all get the same answer.
        if (user is null
            || !(await signIn.PasswordSignInAsync(user, request.Password, isPersistent: true, lockoutOnFailure: true)).Succeeded)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "The email or password is incorrect.");
        }

        return CurrentUserResponse.From(user);
    }

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
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CurrentUserResponse>> Me()
    {
        var user = await users.GetUserAsync(User);
        return user is null ? Unauthorized() : CurrentUserResponse.From(user);
    }
}
