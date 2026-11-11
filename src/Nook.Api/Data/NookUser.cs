using Microsoft.AspNetCore.Identity;

namespace Nook.Api.Data;

/// <summary>A person who can sign in to Nook.</summary>
public class NookUser : IdentityUser<Guid>, ITimestamped
{
    public NookUser() => Id = Guid.CreateVersion7();

    /// <summary>What the user may do. New users get the least privilege.</summary>
    public UserRole Role { get; set; } = UserRole.Guest;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
