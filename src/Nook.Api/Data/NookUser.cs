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

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
