namespace Nook.Api.Data;

/// <summary>What a user, or a visitor with no account, may do in Nook. Every user references one.</summary>
public class Role : Entity
{
    public static readonly Guid AdminId = new("01a1c7e7-8000-7000-8000-0000000000a1");

    public static readonly Guid ContributorId = new("01a1c7e7-8000-7000-8000-0000000000a2");

    public static readonly Guid ViewerId = new("01a1c7e7-8000-7000-8000-0000000000a3");

    public static readonly Guid AnonymousId = new("01a1c7e7-8000-7000-8000-0000000000a4");

    /// <summary>Each built-in role's id: the one place that pairs them.</summary>
    private static readonly Dictionary<BuiltInRole, Guid> BuiltInIds = new()
    {
        [BuiltInRole.Admin] = AdminId,
        [BuiltInRole.Contributor] = ContributorId,
        [BuiltInRole.Viewer] = ViewerId,
        [BuiltInRole.Anonymous] = AnonymousId,
    };

    /// <summary>The name people see.</summary>
    public required string Name { get; set; }

    /// <summary>Which built-in role this is, or null for a role someone made.</summary>
    public BuiltInRole? BuiltIn { get; set; }

    /// <summary>The most the role may do with a work in any section.</summary>
    public ContentAccess ContentAccess { get; set; }

    /// <summary>The most the role may do with a work in no section.</summary>
    public ContentAccess DefaultAreaAccess { get; set; }

    /// <summary>Whether the role has its <see cref="ContentAccess"/> in every section.</summary>
    public bool AllSections { get; set; }

    /// <summary>Whether the role may upload books.</summary>
    public bool CanUpload { get; set; }

    /// <summary>The fixed id of a built-in role.</summary>
    public static Guid IdOf(BuiltInRole role) =>
        BuiltInIds.TryGetValue(role, out var id) ? id : throw new ArgumentOutOfRangeException(nameof(role), role, null);

    /// <summary>The built-in role a role id is, or null when it is none of them.</summary>
    public static BuiltInRole? BuiltInOf(Guid id)
    {
        foreach (var (role, roleId) in BuiltInIds)
        {
            if (roleId == id)
            {
                return role;
            }
        }

        return null;
    }
}
