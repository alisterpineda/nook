using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Nook.Api.Data;
using static Nook.Api.Tests.Integration.PublicationManifests;

namespace Nook.Api.Tests.Integration;

/// <summary>
/// How often a visitor with no session may download and read with public access on, on a host whose limit is three a
/// minute. Each test starts its own host on the class's database, so each has a limit of its own. Each test saves the
/// settings it needs first, and a save replaces every level, so sections other tests added are closed.
/// </summary>
public class PublicAccessRateLimitTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    private const int PermitLimit = 3;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_visitors_downloads_are_rate_limited()
    {
        using var limited = Limited();
        var part = await OpenPartAsync("Kindred");
        using var visitor = limited.CreateClient();

        for (var download = 0; download < PermitLimit; download++)
        {
            var allowed = await visitor.GetAsync($"/api/parts/{part.Id}/file", CancellationToken);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        var refused = await visitor.GetAsync($"/api/parts/{part.Id}/file", CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
    }

    [Fact]
    public async Task A_visitors_reading_is_rate_limited()
    {
        using var limited = Limited();
        var part = await OpenPartAsync("Dawn");
        using var visitor = limited.CreateClient();
        var manifest = await GetManifestAsync(visitor, part.ItemId);
        using var reader = limited.CreateClient(new() { HandleCookies = false });
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync(SelfHref(manifest), CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync(FirstChapterOf(manifest), CancellationToken)).StatusCode);

        var refused = await reader.GetAsync(FirstChapterOf(manifest), CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
    }

    [Fact]
    public async Task A_signed_in_users_downloads_are_not_rate_limited()
    {
        using var limited = Limited();
        var part = await OpenPartAsync("Imago");
        using var user = await limited.CreateSignedInClientAsync(BuiltInRole.Viewer);

        for (var download = 0; download < PermitLimit * 2; download++)
        {
            var response = await user.GetAsync($"/api/parts/{part.Id}/file", CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task A_signed_in_users_token_path_is_not_rate_limited_without_the_cookie()
    {
        using var limited = Limited();
        var part = await OpenPartAsync("Wild Seed");
        using var user = await limited.CreateSignedInClientAsync(BuiltInRole.Viewer);
        var manifest = await GetManifestAsync(user, part.ItemId);
        using var reader = limited.CreateClient(new() { HandleCookies = false });

        for (var request = 0; request < PermitLimit * 2; request++)
        {
            Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync(SelfHref(manifest), CancellationToken)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync(FirstChapterOf(manifest), CancellationToken)).StatusCode);
        }
    }

    [Fact]
    public async Task A_visitor_and_a_signed_in_user_at_one_address_are_told_apart()
    {
        using var limited = Limited();
        var part = await OpenPartAsync("Fledgling");
        using var user = await limited.CreateSignedInClientAsync(BuiltInRole.Viewer);
        using var visitor = limited.CreateClient();
        var file = $"/api/parts/{part.Id}/file";
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync(file, CancellationToken)).StatusCode);
        for (var download = 0; download < PermitLimit; download++)
        {
            Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync(file, CancellationToken)).StatusCode);
        }

        var refused = await visitor.GetAsync(file, CancellationToken);
        var stillAllowed = await user.GetAsync(file, CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stillAllowed.StatusCode);
    }

    private WebApplicationFactory<Program> Limited() =>
        factory.WithWebHostBuilder(builder =>
            builder.UseSetting("RateLimiting:Anonymous:PermitLimit", PermitLimit.ToString()));

    /// <summary>A part of a new book in a new section that a visitor may download, with public access on.</summary>
    private async Task<Part> OpenPartAsync(string title)
    {
        var section = await factory.AddSectionAsync();
        var part = await factory.IngestOnePartAsync(
            OneChapterBook(title), section);
        await factory.SetPublicAccessAsync(
            true, ContentAccess.Download, ContentAccess.None, (section.Id, ContentAccess.Download));
        return part;
    }
}
