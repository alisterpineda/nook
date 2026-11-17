using System.Net;
using System.Text.Json.Nodes;
using Nook.Api.Data;
using static Nook.Api.Tests.Integration.PublicationManifests;

namespace Nook.Api.Tests.Integration;

/// <summary>
/// Which publications a visitor with no session may read with public access on, and how long the token they are given
/// keeps working. Each test saves the settings it needs first, and a save replaces every level, so sections other tests
/// added are closed.
/// </summary>
public class PublicAccessReadingTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Visitor_reads_a_publication_where_the_level_is_read()
    {
        var section = await factory.AddSectionAsync();
        var part = await factory.IngestOnePartAsync(OneChapterBook("Kindred"), section);
        await factory.SetPublicAccessAsync(true, ContentAccess.Download, ContentAccess.None, (section.Id, ContentAccess.Read));
        using var visitor = factory.CreateClient();

        var response = await visitor.GetAsync(ManifestUrl(part.ItemId), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var manifest = await ReadManifestAsync(response);
        Assert.Equal("Kindred", (string?)manifest["metadata"]?["title"]);
        using var reader = factory.CreateClient(new() { HandleCookies = false });
        var chapter = await reader.GetAsync(FirstChapterOf(manifest), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, chapter.StatusCode);
        Assert.Contains("<h1>Kindred</h1>", await chapter.Content.ReadAsStringAsync(CancellationToken));
        Assert.True(chapter.Headers.CacheControl?.Private);
    }

    [Fact]
    public async Task Manifest_is_not_found_for_a_visitor_where_the_level_is_browse()
    {
        var section = await factory.AddSectionAsync();
        var part = await factory.IngestOnePartAsync(OneChapterBook("Dawn"), section);
        await factory.SetPublicAccessAsync(true, ContentAccess.Download, ContentAccess.None, (section.Id, ContentAccess.Browse));
        using var visitor = factory.CreateClient();

        var response = await visitor.GetAsync(ManifestUrl(part.ItemId), CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Manifest_is_not_found_for_a_visitor_where_the_anonymous_level_is_browse()
    {
        var section = await factory.AddSectionAsync();
        var part = await factory.IngestOnePartAsync(OneChapterBook("Imago"), section);
        await factory.SetPublicAccessAsync(true, ContentAccess.Browse, ContentAccess.None, (section.Id, ContentAccess.Download));
        using var visitor = factory.CreateClient();

        var response = await visitor.GetAsync(ManifestUrl(part.ItemId), CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_visitors_token_stops_working_when_public_access_is_turned_off()
    {
        var section = await factory.AddSectionAsync();
        var part = await factory.IngestOnePartAsync(OneChapterBook("Parable of the Sower"), section);
        await factory.SetPublicAccessAsync(true, ContentAccess.Download, ContentAccess.None, (section.Id, ContentAccess.Read));
        var manifest = await GetVisitorManifestAsync(part.ItemId);
        using var reader = factory.CreateClient(new() { HandleCookies = false });
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync(FirstChapterOf(manifest), CancellationToken)).StatusCode);

        await factory.SetPublicAccessAsync(false, ContentAccess.Download, ContentAccess.None, (section.Id, ContentAccess.Read));

        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync(SelfHref(manifest), CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync(FirstChapterOf(manifest), CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task A_visitors_token_stops_working_when_the_section_drops_to_browse()
    {
        var section = await factory.AddSectionAsync();
        var part = await factory.IngestOnePartAsync(OneChapterBook("Parable of the Talents"), section);
        await factory.SetPublicAccessAsync(true, ContentAccess.Download, ContentAccess.None, (section.Id, ContentAccess.Read));
        var manifest = await GetVisitorManifestAsync(part.ItemId);
        using var reader = factory.CreateClient(new() { HandleCookies = false });
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync(FirstChapterOf(manifest), CancellationToken)).StatusCode);

        await factory.SetPublicAccessAsync(true, ContentAccess.Download, ContentAccess.None, (section.Id, ContentAccess.Browse));

        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync(SelfHref(manifest), CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync(FirstChapterOf(manifest), CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task A_visitors_token_does_not_reach_another_item()
    {
        var (section, otherSection) = (await factory.AddSectionAsync(), await factory.AddSectionAsync());
        var open = await factory.IngestOnePartAsync(OneChapterBook("Wild Seed"), section);
        var other = await factory.IngestOnePartAsync(OneChapterBook("Mind of My Mind"), otherSection);
        await factory.SetPublicAccessAsync(
            true,
            ContentAccess.Download,
            ContentAccess.None,
            (section.Id, ContentAccess.Read),
            (otherSection.Id, ContentAccess.Read));
        var token = TokenOf(SelfHref(await GetVisitorManifestAsync(open.ItemId)));
        using var reader = factory.CreateClient(new() { HandleCookies = false });

        var response = await reader.GetAsync(TokenUrl(other.ItemId, token, "manifest.json"), CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<JsonObject> GetVisitorManifestAsync(Guid itemId)
    {
        using var visitor = factory.CreateClient();
        return await GetManifestAsync(visitor, itemId);
    }
}
