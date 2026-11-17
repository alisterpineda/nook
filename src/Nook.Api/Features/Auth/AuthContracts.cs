using System.ComponentModel.DataAnnotations;
using Nook.Api.Data;

namespace Nook.Api.Features.Auth;

public record LoginRequest([Required, EmailAddress] string Email, [Required] string Password);

/// <summary>The signed-in user, as the client needs to know them.</summary>
public record CurrentUserResponse(Guid Id, string Email, BuiltInRole Role)
{
    public static CurrentUserResponse From(NookUser user) =>
        new(user.Id, user.Email ?? user.UserName ?? "", user.BuiltInRoleOrViewer);
}

/// <summary>How someone may sign in besides a password: the name of the single sign-on provider, or null when there is none.</summary>
public record SignInOptionsResponse(string? SsoName);
