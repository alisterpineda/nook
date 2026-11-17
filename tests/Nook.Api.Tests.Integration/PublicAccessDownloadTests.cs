using System.Net;
using Nook.Api.Data;

namespace Nook.Api.Tests.Integration;

/// <summary>
/// Which part files a visitor with no session may download with public access on. Each test saves the settings it
/// needs first, and a save replaces every level, so sections other tests added are closed.
/// </summary>
public class PublicAccessDownloadTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Visitor_downloads_a_part_where_the_level_is_download()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var section = await factory.AddSectionAsync();
        var epub = new EpubBuilder().WithTitle("Kindred").WithCreator("Octavia E. Butler").Build();
        var part = await factory.IngestOnePartAsync(epub, section);
        await factory.SetPublicAccessAsync(
            true, ContentAccess.Download, ContentAccess.None, (section.Id, ContentAccess.Download));
        using var visitor = factory.CreateClient();

        var response = await visitor.GetAsync($"/api/parts/{part.Id}/file", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(epub, await response.Content.ReadAsByteArrayAsync(cancellationToken));
    }

    [Fact]
    public async Task Part_file_is_not_found_for_a_visitor_where_the_level_is_read()
    {
        var section = await factory.AddSectionAsync();
        var part = await factory.IngestOnePartAsync(
            new EpubBuilder().WithTitle("Dawn").WithCreator("Octavia E. Butler").Build(), section);
        await factory.SetPublicAccessAsync(
            true, ContentAccess.Download, ContentAccess.None, (section.Id, ContentAccess.Read));
        using var visitor = factory.CreateClient();

        var response = await visitor.GetAsync($"/api/parts/{part.Id}/file", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Part_file_is_not_found_for_a_visitor_where_the_anonymous_level_is_read()
    {
        var section = await factory.AddSectionAsync();
        var part = await factory.IngestOnePartAsync(
            new EpubBuilder().WithTitle("Imago").WithCreator("Octavia E. Butler").Build(), section);
        await factory.SetPublicAccessAsync(
            true, ContentAccess.Read, ContentAccess.None, (section.Id, ContentAccess.Download));
        using var visitor = factory.CreateClient();

        var response = await visitor.GetAsync($"/api/parts/{part.Id}/file", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
