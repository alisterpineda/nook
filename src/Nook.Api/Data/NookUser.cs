using Microsoft.AspNetCore.Identity;

namespace Nook.Api.Data;

/// <summary>A person who can sign in to Nook.</summary>
public class NookUser : IdentityUser<Guid>, ITimestamped
{
    public NookUser() => Id = Guid.CreateVersion7();

    /// <summary>The id of the user's <see cref="Role"/>. New users get the least privilege a signed-in user has.</summary>
    public Guid RoleId { get; set; } = Role.ViewerId;

    /// <summary>The built-in role the user holds, or null when their role is not built in.</summary>
    public BuiltInRole? BuiltInRole => Role.BuiltInOf(RoleId);

    /// <summary>The built-in role the user holds, counting a role that is not built in as viewer.</summary>
    public BuiltInRole BuiltInRoleOrViewer => BuiltInRole ?? Data.BuiltInRole.Viewer;

    /// <summary>The name people see for the user, or null when none was given.</summary>
    public string? DisplayName { get; set; }

    /// <summary>The name to show: the display name, else the part of the email before the <c>@</c>.</summary>
    public string ShownName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(DisplayName))
            {
                return DisplayName;
            }

            var email = ShownEmail;
            var at = email.IndexOf('@');
            return at > 0 ? email[..at] : email;
        }
    }

    /// <summary>The email to show: the email, else the user name, which is the email they signed up with.</summary>
    public string ShownEmail => Email ?? UserName ?? "";

    /// <summary>A display name as it is stored: trimmed, and null when nothing is left.</summary>
    public static string? CleanDisplayName(string? name) => string.IsNullOrWhiteSpace(name) ? null : name.Trim();

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
