using Nook.Api.Data;
using Nook.Api.Features.PublicAccess;

namespace Nook.Api.Tests.Integration;

/// <summary>What <c>api/access</c> tells a client about the caller. Each test saves the settings it needs first.</summary>
public class PublicAccessVisitorTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Access_reports_a_visitors_level_in_the_default_area()
    {
        using var visitor = factory.CreateClient();

        await factory.SetPublicAccessAsync(true, ContentAccess.Download, ContentAccess.Read);
        var opened = await visitor.GetAsync<AccessResponse>("/api/access");
        await factory.SetPublicAccessAsync(true, ContentAccess.Browse, ContentAccess.Download);
        var capped = await visitor.GetAsync<AccessResponse>("/api/access");
        await factory.SetPublicAccessAsync(false, ContentAccess.Download, ContentAccess.Download);
        var off = await visitor.GetAsync<AccessResponse>("/api/access");

        Assert.Equal(new AccessResponse(true, ContentAccess.Read), opened);
        Assert.Equal(new AccessResponse(true, ContentAccess.Browse), capped);
        Assert.Equal(new AccessResponse(false, ContentAccess.None), off);
    }

    [Fact]
    public async Task Access_reports_download_in_the_default_area_for_a_viewer()
    {
        await factory.SetPublicAccessAsync(true, ContentAccess.Browse, ContentAccess.Browse);
        using var viewer = await factory.CreateSignedInClientAsync(BuiltInRole.Viewer);

        var access = await viewer.GetAsync<AccessResponse>("/api/access");

        Assert.Equal(new AccessResponse(true, ContentAccess.Download), access);
    }
}
