using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Nook.Api.Data;

namespace Nook.Api.Tests.Integration;

public class PartFileTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Part_file_is_the_uploaded_file_under_its_name_on_disk()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var epub = new EpubBuilder().WithTitle("Les Misérables").WithCreator("Victor Hugo").Build();
        var part = await factory.IngestOnePartAsync(epub);
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync($"/api/parts/{part.Id}/file", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(epub, await response.Content.ReadAsByteArrayAsync(cancellationToken));
        Assert.Equal("application/epub+zip", response.Content.Headers.ContentType?.MediaType);
        var disposition = response.Content.Headers.ContentDisposition;
        Assert.NotNull(disposition);
        Assert.Equal("attachment", disposition.DispositionType);
        Assert.Equal("Les Misérables.epub", part.Path.Split('/')[^1]);
        Assert.Equal(part.Path.Split('/')[^1], disposition.FileNameStar);
    }

    [Theory]
    [InlineData(BuiltInRole.Contributor)]
    [InlineData(BuiltInRole.Viewer)]
    public async Task Contributor_and_viewer_can_download_a_part(BuiltInRole role)
    {
        var part = await factory.IngestOnePartAsync(new EpubBuilder().WithTitle("Kindred").WithCreator("Octavia E. Butler").Build());
        using var client = await factory.CreateSignedInClientAsync(role);

        var response = await client.GetAsync($"/api/parts/{part.Id}/file", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Part_file_without_a_session_is_unauthorized()
    {
        var part = await factory.IngestOnePartAsync(new EpubBuilder().WithTitle("Kindred").WithCreator("Octavia E. Butler").Build());
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/parts/{part.Id}/file", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task File_of_an_unknown_part_is_not_found()
    {
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync($"/api/parts/{Guid.NewGuid()}/file", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Range_request_gets_only_the_bytes_asked_for()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var epub = new EpubBuilder().WithTitle("Kindred").WithCreator("Octavia E. Butler").Build();
        var part = await factory.IngestOnePartAsync(epub);
        using var client = await factory.CreateSignedInClientAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/parts/{part.Id}/file");
        request.Headers.Range = new RangeHeaderValue(0, 9);

        var response = await client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(epub[..10], await response.Content.ReadAsByteArrayAsync(cancellationToken));
        Assert.Equal($"bytes 0-9/{epub.Length}", response.Content.Headers.ContentRange?.ToString());
    }

    [Fact]
    public async Task Part_whose_file_is_gone_is_not_found_and_logged()
    {
        var section = await factory.AddSectionAsync();
        var part = await factory.IngestOnePartAsync(
            new EpubBuilder().WithTitle("Kindred").WithCreator("Octavia E. Butler").Build(), section);
        File.Delete(factory.LibraryPath(part.Path.Split('/')));
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync($"/api/parts/{part.Id}/file", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(factory.ReadLogs(LogLevel.Error), m => m.Contains(part.Id.ToString()));
    }
}
