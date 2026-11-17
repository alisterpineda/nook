using Nook.Api.Data;
using Nook.Api.Features.PublicAccess;

namespace Nook.Api.Tests.Integration;

/// <summary>Public access as a new Nook has it. No test here saves the settings, so the database stays as migrated.</summary>
public class PublicAccessDefaultsTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Public_access_starts_off_with_every_level_none()
    {
        var section = new LibrarySection { Name = "Comics" };
        await factory.AddRowsAsync(section);
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var settings = await admin.GetAsync<PublicAccessResponse>("/api/public-access");

        Assert.False(settings.Enabled);
        Assert.Equal(ContentAccess.None, settings.Access);
        Assert.Equal(ContentAccess.None, settings.DefaultAreaAccess);
        var listed = Assert.Single(settings.Sections, s => s.Id == section.Id);
        Assert.Equal(("Comics", ContentAccess.None), (listed.Name, listed.Access));
        Assert.All(settings.Sections, s => Assert.Equal(ContentAccess.None, s.Access));
    }

    [Fact]
    public async Task Access_without_a_session_says_public_access_is_off_by_default()
    {
        using var visitor = factory.CreateClient();

        var access = await visitor.GetAsync<AccessResponse>("/api/access");

        Assert.Equal(new AccessResponse(false, ContentAccess.None), access);
    }
}
