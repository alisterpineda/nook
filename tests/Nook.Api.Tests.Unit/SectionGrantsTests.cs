using Nook.Api.Data;
using Nook.Api.Features.Sections;

namespace Nook.Api.Tests.Unit;

public class SectionGrantsTests
{
    private static readonly Guid Poetry = Guid.CreateVersion7();
    private static readonly Guid History = Guid.CreateVersion7();
    private static readonly Guid Unlisted = Guid.CreateVersion7();

    private static Role LimitedRole(ContentAccess access, ContentAccess defaultArea = ContentAccess.None) =>
        new() { Name = "Limited", ContentAccess = access, DefaultAreaAccess = defaultArea, AllSections = false };

    private static RoleSection Row(Role role, Guid sectionId, ContentAccess access) =>
        new() { RoleId = role.Id, SectionId = sectionId, Access = access };

    [Fact]
    public void A_section_gets_the_lower_of_the_roles_level_and_its_own()
    {
        var role = LimitedRole(ContentAccess.Read);

        var grants = SectionGrants.For(
            role,
            [Row(role, Poetry, ContentAccess.Browse), Row(role, History, ContentAccess.Download)]);

        Assert.Equal(ContentAccess.Browse, grants.In(Poetry));
        Assert.Equal(ContentAccess.Read, grants.In(History));
    }

    [Fact]
    public void A_section_with_no_level_gets_none()
    {
        var role = LimitedRole(ContentAccess.Download, ContentAccess.Download);

        var grants = SectionGrants.For(role, [Row(role, Poetry, ContentAccess.Read)]);

        Assert.Equal(ContentAccess.None, grants.In(Unlisted));
        Assert.Equal(ContentAccess.None, SectionGrants.None.In(Poetry));
        Assert.Equal(ContentAccess.None, SectionGrants.None.In(null));
    }

    [Fact]
    public void The_default_area_gets_the_lower_of_the_roles_level_and_its_own()
    {
        var lowerOwn = SectionGrants.For(LimitedRole(ContentAccess.Download, ContentAccess.Browse), []);
        var lowerRole = SectionGrants.For(LimitedRole(ContentAccess.Browse, ContentAccess.Download), []);

        Assert.Equal(ContentAccess.Browse, lowerOwn.In(null));
        Assert.Equal(ContentAccess.Browse, lowerRole.In(null));
    }

    [Fact]
    public void A_role_for_all_sections_has_its_level_everywhere()
    {
        var role = new Role
        {
            Name = "Viewer",
            ContentAccess = ContentAccess.Read,
            DefaultAreaAccess = ContentAccess.None,
            AllSections = true,
        };

        var grants = SectionGrants.For(role, [Row(role, Poetry, ContentAccess.Browse)]);

        Assert.Equal(ContentAccess.Read, grants.In(Poetry));
        Assert.Equal(ContentAccess.Read, grants.In(Unlisted));
        Assert.Equal(ContentAccess.Read, grants.In(null));
        Assert.True(grants.IsOpenEverywhere(ContentAccess.Read));
        Assert.False(grants.IsOpenEverywhere(ContentAccess.Download));
    }

    [Fact]
    public void Combining_gives_the_higher_level_in_each_area()
    {
        var limited = LimitedRole(ContentAccess.Download, ContentAccess.Browse);
        var anonymous = LimitedRole(ContentAccess.Read, ContentAccess.Download);
        var limitedGrants = SectionGrants.For(
            limited,
            [Row(limited, Poetry, ContentAccess.Download), Row(limited, History, ContentAccess.Browse)]);
        var anonymousGrants = SectionGrants.For(
            anonymous,
            [Row(anonymous, History, ContentAccess.Read), Row(anonymous, Unlisted, ContentAccess.Browse)]);

        var combined = limitedGrants.Combine(anonymousGrants);

        Assert.Equal(ContentAccess.Download, combined.In(Poetry));
        Assert.Equal(ContentAccess.Read, combined.In(History));
        Assert.Equal(ContentAccess.Browse, combined.In(Unlisted));
        Assert.Equal(ContentAccess.Read, combined.In(null));
        Assert.Equal(ContentAccess.Read, anonymousGrants.Combine(limitedGrants).In(null));
    }

    [Fact]
    public void Combining_with_a_role_for_all_sections_opens_every_area()
    {
        var limited = LimitedRole(ContentAccess.Download);
        var viewer = new Role { Name = "Viewer", ContentAccess = ContentAccess.Browse, AllSections = true };

        var combined = SectionGrants
            .For(limited, [Row(limited, Poetry, ContentAccess.Download)])
            .Combine(SectionGrants.For(viewer, []));

        Assert.Equal(ContentAccess.Download, combined.In(Poetry));
        Assert.Equal(ContentAccess.Browse, combined.In(Unlisted));
        Assert.Equal(ContentAccess.Browse, combined.In(null));
        Assert.True(combined.IsOpenEverywhere(ContentAccess.Browse));
        Assert.False(combined.IsOpenEverywhere(ContentAccess.Read));
    }

    [Fact]
    public void The_areas_open_at_a_level_are_listed()
    {
        var role = LimitedRole(ContentAccess.Download, ContentAccess.Read);

        var grants = SectionGrants.For(
            role,
            [Row(role, Poetry, ContentAccess.Download), Row(role, History, ContentAccess.Browse)]);

        Assert.False(grants.IsOpenEverywhere(ContentAccess.Browse));
        Assert.True(grants.IsDefaultAreaOpen(ContentAccess.Read));
        Assert.False(grants.IsDefaultAreaOpen(ContentAccess.Download));
        Assert.Equal(new HashSet<Guid> { Poetry, History }, grants.SectionsOpen(ContentAccess.Browse).ToHashSet());
        Assert.Equal(new[] { Poetry }, grants.SectionsOpen(ContentAccess.Read));
        Assert.Empty(SectionGrants.None.SectionsOpen(ContentAccess.Browse));
        Assert.False(SectionGrants.None.IsDefaultAreaOpen(ContentAccess.Browse));
    }
}
