using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;

namespace Nook.Api.Features.Auth;

public record LoginRequest([Required, EmailAddress] string Email, [Required] string Password);

/// <summary>The signed-in user, as the client needs to know them.</summary>
public record CurrentUserResponse(Guid Id, string Email, BuiltInRole Role, string DisplayName, bool CanUpload)
{
    /// <summary>The user with what their role allows, read from the role's row.</summary>
    public static async Task<CurrentUserResponse> FromAsync(NookDbContext db, NookUser user, CancellationToken cancellationToken)
    {
        var canUpload = await db.Roles.AsNoTracking()
            .Where(r => r.Id == user.RoleId)
            .Select(r => r.CanUpload)
            .SingleAsync(cancellationToken);
        return new(user.Id, user.ShownEmail, user.BuiltInRoleOrViewer, user.ShownName, canUpload);
    }
}

/// <summary>
/// How someone may sign in: the name of the single sign-on provider, or null when there is none, and whether a password
/// works.
/// </summary>
public record SignInOptionsResponse(string? SsoName, bool PasswordSignIn);
