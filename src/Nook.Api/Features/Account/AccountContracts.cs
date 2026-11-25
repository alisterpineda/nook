using System.ComponentModel.DataAnnotations;

namespace Nook.Api.Features.Account;

/// <summary>
/// The signed-in user's own account: their email and shown name, the name of their role, whether a password is stored,
/// and whether a single sign-on login is linked.
/// </summary>
public record AccountResponse(string Email, string Name, string Role, bool HasPassword, bool HasSingleSignOn);

/// <summary>A new password for the signed-in user. The current one is needed only when a password is stored.</summary>
public record SetOwnPasswordRequest(string? CurrentPassword, [Required] string NewPassword);
