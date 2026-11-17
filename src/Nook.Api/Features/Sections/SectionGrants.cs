using Nook.Api.Data;

namespace Nook.Api.Features.Sections;

/// <summary>
/// The level a caller has in each area of the library: every section, and the Default area, which holds the works in
/// no section.
/// </summary>
public sealed class SectionGrants
{
    /// <summary>Nothing in any area.</summary>
    public static readonly SectionGrants None = new(ContentAccess.None, ContentAccess.None, new Dictionary<Guid, ContentAccess>());

    /// <summary>The level in every section, whether or not it has a level of its own below.</summary>
    private readonly ContentAccess everySection;

    private readonly ContentAccess defaultArea;

    /// <summary>The level in each section that has one of its own, above none.</summary>
    private readonly IReadOnlyDictionary<Guid, ContentAccess> sections;

    private SectionGrants(
        ContentAccess everySection,
        ContentAccess defaultArea,
        IReadOnlyDictionary<Guid, ContentAccess> sections)
    {
        this.everySection = everySection;
        this.defaultArea = defaultArea;
        this.sections = sections;
    }

    /// <summary>
    /// The levels a role gives. A role for every section has its level in every area. Any other role has, in a
    /// section, the lower of its level and that section's, none in a section with no level, and in the Default area
    /// the lower of its level and its Default area level. A section's level can only lower the role's.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <param name="sections">The role's levels per section. Rows for other roles are ignored.</param>
    public static SectionGrants For(Role role, IEnumerable<RoleSection> sections)
    {
        if (role.AllSections)
        {
            return new SectionGrants(role.ContentAccess, role.ContentAccess, new Dictionary<Guid, ContentAccess>());
        }

        var levels = new Dictionary<Guid, ContentAccess>();
        foreach (var row in sections)
        {
            if (row.RoleId != role.Id)
            {
                continue;
            }

            var level = Lower(role.ContentAccess, row.Access);
            if (level > ContentAccess.None)
            {
                levels[row.SectionId] = Higher(levels.GetValueOrDefault(row.SectionId), level);
            }
        }

        return new SectionGrants(ContentAccess.None, Lower(role.ContentAccess, role.DefaultAreaAccess), levels);
    }

    /// <summary>The level in a section, or in the Default area when <paramref name="sectionId"/> is null.</summary>
    public ContentAccess In(Guid? sectionId) =>
        sectionId is { } id
            ? Higher(everySection, sections.GetValueOrDefault(id))
            : defaultArea;

    /// <summary>The higher of the two levels in each area.</summary>
    public SectionGrants Combine(SectionGrants other)
    {
        var levels = new Dictionary<Guid, ContentAccess>(sections);
        foreach (var (id, level) in other.sections)
        {
            levels[id] = Higher(levels.GetValueOrDefault(id), level);
        }

        return new SectionGrants(
            Higher(everySection, other.everySection),
            Higher(defaultArea, other.defaultArea),
            levels);
    }

    /// <summary>Whether every section and the Default area are open at <paramref name="level"/> or above.</summary>
    public bool IsOpenEverywhere(ContentAccess level) => everySection >= level && defaultArea >= level;

    /// <summary>Whether the Default area is open at <paramref name="level"/> or above.</summary>
    public bool IsDefaultAreaOpen(ContentAccess level) => defaultArea >= level;

    /// <summary>
    /// The ids of the sections open at <paramref name="level"/> or above through a level of their own. When every
    /// section is open at that level, as <see cref="IsOpenEverywhere"/> tells, sections with no level of their own
    /// are open too and are not listed.
    /// </summary>
    public IReadOnlyCollection<Guid> SectionsOpen(ContentAccess level) =>
        sections.Where(section => section.Value >= level).Select(section => section.Key).ToList();

    private static ContentAccess Lower(ContentAccess a, ContentAccess b) => a < b ? a : b;

    private static ContentAccess Higher(ContentAccess a, ContentAccess b) => a > b ? a : b;
}

/// <summary>Narrows catalog queries to what a caller's <see cref="SectionGrants"/> let them see.</summary>
public static class SectionVisibility
{
    /// <summary>The sections the grants open to browsing.</summary>
    public static IQueryable<LibrarySection> VisibleTo(this IQueryable<LibrarySection> sections, SectionGrants grants)
    {
        if (grants.IsOpenEverywhere(ContentAccess.Browse))
        {
            return sections;
        }

        var open = grants.SectionsOpen(ContentAccess.Browse).ToArray();
        return sections.Where(s => open.Contains(s.Id));
    }

    /// <summary>The works in an area the grants open at <paramref name="level"/> or above.</summary>
    public static IQueryable<Work> VisibleTo(
        this IQueryable<Work> works, SectionGrants grants, ContentAccess level = ContentAccess.Browse)
    {
        if (grants.IsOpenEverywhere(level))
        {
            return works;
        }

        var open = grants.SectionsOpen(level).ToArray();
        return grants.IsDefaultAreaOpen(level)
            ? works.Where(w => w.SectionId == null || open.Contains(w.SectionId.Value))
            : works.Where(w => w.SectionId != null && open.Contains(w.SectionId.Value));
    }
}
