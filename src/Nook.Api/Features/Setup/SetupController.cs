using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;
using Nook.Api.Features.Auth;

namespace Nook.Api.Features.Setup;

public record SetupStatusResponse(bool Required);

public record SetupRequest([Required, EmailAddress] string Email, [Required] string Password, string? DisplayName = null);

/// <summary>First-run setup: creating the first admin. Open to anyone, and closed for good once any user exists.</summary>
[ApiController]
[Route("api/setup")]
[AllowAnonymous]
public class SetupController(NookDbContext db, UserManager<NookUser> users, SignInManager<NookUser> signIn) : ControllerBase
{
    // Any fixed number works; it only has to differ from other advisory locks Nook takes.
    private const long SetupLockKey = 0x4E6F6F6B_00000001;

    /// <summary>Whether Nook still needs its first admin.</summary>
    [HttpGet]
    public async Task<SetupStatusResponse> GetStatus(CancellationToken cancellationToken) =>
        new(await FirstRunSetup.IsRequiredAsync(db, cancellationToken));

    /// <summary>Creates the first admin and signs them in.</summary>
    [HttpPost]
    [EnableRateLimiting(RateLimits.Auth)]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<CurrentUserResponse>> CreateFirstAdmin(SetupRequest request, CancellationToken cancellationToken)
    {
        var user = new NookUser { UserName = request.Email, Email = request.Email, RoleId = Role.AdminId, DisplayName = NookUser.CleanDisplayName(request.DisplayName) };

        // The database is set to retry, so the transaction has to run inside its retry strategy.
        var result = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            // Two setups at once would both see no users. The lock makes the second wait for the first to commit.
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({SetupLockKey})", cancellationToken);
            if (await db.Users.AnyAsync(u => u.Id != user.Id, cancellationToken))
            {
                return null;
            }

            // A retry after a commit whose answer was lost finds this request's own admin already saved.
            if (await db.Users.AnyAsync(u => u.Id == user.Id, cancellationToken))
            {
                return IdentityResult.Success;
            }

            var created = await users.CreateAsync(user, request.Password);
            if (created.Succeeded)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return created;
        });

        if (result is null)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Nook is already set up.");
        }

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                var field = error.Code.StartsWith("Password", StringComparison.Ordinal) ? nameof(request.Password) : nameof(request.Email);
                ModelState.AddModelError(field, error.Description);
            }

            return ValidationProblem(ModelState);
        }

        await signIn.SignInAsync(user, isPersistent: true);
        return await CurrentUserResponse.FromAsync(db, user, cancellationToken);
    }
}
