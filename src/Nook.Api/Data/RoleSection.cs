namespace Nook.Api.Data;

/// <summary>
/// A role's level in one section. It can only lower the role's own level, and a role that is not for every
/// section has nothing in a section with no row.
/// </summary>
public class RoleSection : Entity
{
    public Guid RoleId { get; set; }

    public Guid SectionId { get; set; }

    /// <summary>The most the role may do with a work in this section, up to the role's own level.</summary>
    public ContentAccess Access { get; set; }
}
