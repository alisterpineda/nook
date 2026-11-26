using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;
using Nook.Api.Features.Audit;
using Nook.Api.Features.Auth;

namespace Nook.Api.Features.Account;

/// <summary>The signed-in user's own account.</summary>
[ApiController]
[Route("api/account")]
[Authorize]
public class AccountController(
    NookDbContext db,
    UserManager<NookUser> users,
    SignInManager<NookUser> signIn,
    AuditLog audit) : ControllerBase
{
    /// <summary>Who the caller is, and whether they have a password and a single sign-on login.</summary>
    [HttpGet]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AccountResponse>> Get(CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var role = await db.Roles.AsNoTracking()
            .Where(r => r.Id == user.RoleId)
            .Select(r => r.Name)
            .SingleAsync(cancellationToken);
        var hasSingleSignOn = await db.UserLogins.AnyAsync(l => l.UserId == user.Id, cancellationToken);

        return new AccountResponse(user.ShownEmail, user.ShownName, role, user.PasswordHash is not null, hasSingleSignOn);
    }

    /// <summary>
    /// Changes the caller's password, which needs the current one, or adds one when they have none. Their own session
    /// stays signed in and their others end. Not found when password sign-in is off.
    /// </summary>
    [HttpPut("password")]
    [RequiresPasswordSignIn]
    [EnableRateLimiting(RateLimits.Auth)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SetPassword(SetOwnPasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var added = !await users.HasPasswordAsync(user);
        if (!added && string.IsNullOrEmpty(request.CurrentPassword))
        {
            ModelState.AddModelError(nameof(request.CurrentPassword), "Enter your current password.");
            return ValidationProblem(ModelState);
        }

        // The entry is saved by the same save as the password, which the UserManager call below makes.
        await audit.AddAsync(
            User,
            AuditActions.UserPasswordChanged,
            new AuditSubject(AuditSubjectTypes.User, user.Id, user.ShownName),
            new { added },
            cancellationToken: cancellationToken);

        var result = added
            ? await users.AddPasswordAsync(user, request.NewPassword)
            : await users.ChangePasswordAsync(user, request.CurrentPassword!, request.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(
                    error.Code == nameof(IdentityErrorDescriber.PasswordMismatch)
                        ? nameof(request.CurrentPassword)
                        : nameof(request.NewPassword),
                    error.Description);
            }

            return ValidationProblem(ModelState);
        }

        // The password change rotated the security stamp, which ends the other sessions; this one gets a new cookie.
        // The new stamp also forgets every browser that was known for this account. This one is marked as known,
        // whether or not it was before, with Identity's "two-factor remembered" cookie, which Nook uses as that mark.
        await signIn.RefreshSignInAsync(user);
        await signIn.RememberTwoFactorClientAsync(user);
        return NoContent();
    }
}
