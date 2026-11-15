using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nook.Api.Data;
using Nook.Api.Features.Security;
using Contents = Nook.Api.Tests.Integration.EpubBuilder.Contents;

namespace Nook.Api.Tests.Integration;

public class PublicationTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Manifest_describes_the_book()
    {
        var part = await factory.IngestOnePartAsync(new EpubBuilder()
            .WithTitle("Piranesi")
            .WithIdentifier("urn:isbn:9781635575637")
            .WithLanguage("en-GB")
            .WithCreator("Susanna Clarke", role: "aut")
            .WithChapter("text/part 2.xhtml", "<h1 id=\"start\">Two</h1><h2 id=\"hall\">Hall</h2>")
            .WithChapter("text/part1.xhtml", "<h1>One</h1>")
            .WithManifestItem("styles/book.css", "p { margin: 0; }", "text/css")
            .WithContents(
                new Contents("Two", "text/part%202.xhtml#start",
                    new Contents("Hall", "text/part%202.xhtml#hall")),
                new Contents("One", "text/part1.xhtml"))
            .Build());
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync(ManifestUrl(part.ItemId), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/webpub+json", response.Content.Headers.ContentType?.MediaType);
        var manifest = await ReadManifestAsync(response);

        Assert.Equal("Piranesi", (string?)manifest["metadata"]?["title"]);
        Assert.Equal("urn:isbn:9781635575637", (string?)manifest["metadata"]?["identifier"]);
        Assert.Equal("en-GB", (string?)manifest["metadata"]?["language"]);
        Assert.Equal(["Susanna Clarke"], Strings(manifest["metadata"]?["author"]));

        Assert.Equal(
            [("OEBPS/text/part%202.xhtml", "application/xhtml+xml"), ("OEBPS/text/part1.xhtml", "application/xhtml+xml")],
            Links(manifest["readingOrder"]));
        Assert.Equal(
            [("OEBPS/nav.xhtml", "application/xhtml+xml"), ("OEBPS/styles/book.css", "text/css")],
            Links(manifest["resources"]).OrderBy(link => link.Href, StringComparer.Ordinal));

        var toc = Assert.IsType<JsonArray>(manifest["toc"]);
        Assert.Equal(2, toc.Count);
        Assert.Equal("Two", (string?)toc[0]?["title"]);
        Assert.Equal("OEBPS/text/part%202.xhtml#start", (string?)toc[0]?["href"]);
        var nested = Assert.Single(Assert.IsType<JsonArray>(toc[0]?["children"]));
        Assert.Equal("Hall", (string?)nested?["title"]);
        Assert.Equal("OEBPS/text/part%202.xhtml#hall", (string?)nested?["href"]);
        Assert.Equal("One", (string?)toc[1]?["title"]);
        Assert.Equal("OEBPS/text/part1.xhtml", (string?)toc[1]?["href"]);
        Assert.Null(toc[1]?["children"]);
    }

    [Fact]
    public async Task Manifest_title_is_the_works_when_the_book_gives_none()
    {
        var part = await factory.IngestOnePartAsync(new EpubBuilder().Build(), fileName: "Nameless Book.epub");
        using var client = await factory.CreateSignedInClientAsync();

        var manifest = await GetManifestAsync(client, part.ItemId);

        var work = await factory.ReadWorkAsync(await factory.ReadWorkIdOfItemAsync(part.ItemId));
        Assert.Equal(work.Title, (string?)manifest["metadata"]?["title"]);
    }

    [Fact]
    public async Task Manifest_links_a_positions_list_by_media_type()
    {
        var part = await factory.IngestOnePartAsync(new EpubBuilder().WithTitle("Kindred").Build());
        using var client = await factory.CreateSignedInClientAsync();

        var manifest = await GetManifestAsync(client, part.ItemId);

        var links = Assert.IsType<JsonArray>(manifest["links"]);
        var positions = Assert.Single(links,
            link => (string?)link?["type"] == "application/vnd.readium.position-list+json");
        Assert.Equal("~readium/positions.json", (string?)positions?["href"]);
    }

    [Fact]
    public async Task Manifest_self_link_is_absolute_and_under_a_token_path()
    {
        var part = await factory.IngestOnePartAsync(new EpubBuilder().WithTitle("Kindred").Build());
        using var client = await factory.CreateSignedInClientAsync();

        var manifest = await GetManifestAsync(client, part.ItemId);

        var self = SelfHref(manifest);
        Assert.Matches(
            new Regex($"^http://localhost/api/items/{part.ItemId}/publication/t/[A-Za-z0-9_-]+/manifest\\.json$"),
            self);
    }

    [Fact]
    public async Task Manifest_self_link_is_the_same_on_a_second_request()
    {
        var part = await factory.IngestOnePartAsync(new EpubBuilder().WithTitle("Kindred").Build());
        using var client = await factory.CreateSignedInClientAsync();

        var first = SelfHref(await GetManifestAsync(client, part.ItemId));
        var second = SelfHref(await GetManifestAsync(client, part.ItemId));

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Manifest_self_link_differs_between_users_and_items()
    {
        var one = await factory.IngestOnePartAsync(new EpubBuilder().WithTitle("Kindred").Build());
        var other = await factory.IngestOnePartAsync(new EpubBuilder().WithTitle("Dawn").Build());
        using var client = await factory.CreateSignedInClientAsync();
        using var otherClient = await factory.CreateSignedInClientAsync();

        var token = TokenOf(SelfHref(await GetManifestAsync(client, one.ItemId)));

        Assert.NotEqual(token, TokenOf(SelfHref(await GetManifestAsync(otherClient, one.ItemId))));
        Assert.NotEqual(token, TokenOf(SelfHref(await GetManifestAsync(client, other.ItemId))));
    }

    [Fact]
    public async Task Manifest_without_a_session_is_unauthorized()
    {
        var part = await factory.IngestOnePartAsync(new EpubBuilder().WithTitle("Kindred").Build());
        using var client = factory.CreateClient();

        var response = await client.GetAsync(ManifestUrl(part.ItemId), CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Manifest_of_an_unknown_item_is_not_found()
    {
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync(ManifestUrl(Guid.NewGuid()), CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Manifest_of_an_item_without_exactly_one_part_is_not_found(int parts)
    {
        var ingested = await factory.IngestOnePartAsync(new EpubBuilder().WithTitle("Kindred").Build());
        Guid itemId;
        if (parts == 0)
        {
            var item = new Item { WorkId = await factory.ReadWorkIdOfItemAsync(ingested.ItemId), Format = ItemFormat.Epub };
            await factory.AddRowsAsync(item);
            itemId = item.Id;
        }
        else
        {
            await factory.AddRowsAsync(new Part
            {
                ItemId = ingested.ItemId,
                Order = 1,
                Path = ingested.Path,
                MediaType = ingested.MediaType,
                Size = ingested.Size,
                Sha256 = ingested.Sha256,
            });
            itemId = ingested.ItemId;
        }

        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync(ManifestUrl(itemId), CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Manifest_of_a_missing_file_is_not_found()
    {
        var library = await factory.AddLibraryAsync();
        var part = await factory.IngestOnePartAsync(new EpubBuilder().WithTitle("Kindred").Build(), library: library);
        File.Delete(factory.LibraryPath(library, part.Path.Split('/')));
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync(ManifestUrl(part.ItemId), CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(factory.ReadLogs(LogLevel.Error), m => m.Contains(part.ItemId.ToString()));
    }

    [Fact]
    public async Task Manifest_of_a_file_that_is_no_longer_a_book_is_not_found()
    {
        var library = await factory.AddLibraryAsync();
        var part = await factory.IngestOnePartAsync(new EpubBuilder().WithTitle("Kindred").Build(), library: library);
        await File.WriteAllTextAsync(
            factory.LibraryPath(library, part.Path.Split('/')), "not a zip", CancellationToken);
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync(ManifestUrl(part.ItemId), CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Token_manifest_is_the_same_manifest_without_a_cookie()
    {
        var part = await factory.IngestOnePartAsync(new EpubBuilder().WithTitle("Kindred").Build());
        using var signedIn = await factory.CreateSignedInClientAsync();
        var manifest = await GetManifestAsync(signedIn, part.ItemId);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(SelfHref(manifest), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/webpub+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(manifest.ToJsonString(), (await ReadManifestAsync(response)).ToJsonString());
    }

    [Fact]
    public async Task Positions_follow_the_manifest_link_without_a_cookie()
    {
        var epub = new EpubBuilder()
            .WithTitle("Kindred")
            .WithChapter("text/long.xhtml", $"<p>{Convert.ToBase64String(RandomNumberGenerator.GetBytes(6000))}</p>")
            .WithChapter("text/short.xhtml", "<p>Short.</p>")
            .Build();
        var part = await factory.IngestOnePartAsync(epub);
        var manifest = await GetSignedInManifestAsync(part.ItemId);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(Resolve(manifest, PositionsHrefOf(manifest)), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.readium.position-list+json", response.Content.Headers.ContentType?.MediaType);
        var list = await ReadManifestAsync(response);
        var positions = Assert.IsType<JsonArray>(list["positions"]);

        var compressed = CompressedLengths(epub);
        var expected = Links(manifest["readingOrder"])
            .SelectMany(link =>
            {
                var count = (int)Math.Max(Math.Ceiling(compressed[Uri.UnescapeDataString(link.Href!)] / 1024.0), 1);
                return Enumerable.Range(0, count).Select(index => (link.Href, Progression: (double)index / count));
            })
            .ToList();
        Assert.True(expected.Count > 2, "The long chapter should take more than one position.");
        Assert.Equal(expected.Count, (int?)list["total"]);
        Assert.Equal(expected.Count, positions.Count);

        for (var i = 0; i < positions.Count; i++)
        {
            var position = positions[i];
            Assert.Equal(expected[i].Href, (string?)position?["href"]);
            Assert.Equal("application/xhtml+xml", (string?)position?["type"]);
            Assert.Equal(i + 1, (int?)position?["locations"]?["position"]);
            Assert.Equal(expected[i].Progression, (double?)position?["locations"]?["progression"]);
            Assert.Equal((double)i / expected.Count, (double?)position?["locations"]?["totalProgression"]);
        }
    }

    [Fact]
    public async Task Position_hrefs_are_reading_order_hrefs()
    {
        var part = await factory.IngestOnePartAsync(new EpubBuilder()
            .WithChapter("text/part 1.xhtml", "<p>One</p>")
            .WithChapter("text/café.xhtml", "<p>Two</p>")
            .Build());
        var manifest = await GetSignedInManifestAsync(part.ItemId);
        using var client = factory.CreateClient();

        var list = await ReadManifestAsync(
            await client.GetAsync(Resolve(manifest, PositionsHrefOf(manifest)), CancellationToken));

        var readingOrder = Links(manifest["readingOrder"]).Select(link => link.Href).ToList();
        Assert.Equal(
            readingOrder,
            Assert.IsType<JsonArray>(list["positions"]).Select(position => (string?)position?["href"]).Distinct());
    }

    [Fact]
    public async Task A_chapter_a_stylesheet_and_an_image_are_served_with_their_media_types_without_a_cookie()
    {
        var epub = new EpubBuilder()
            .WithTitle("Kindred")
            .WithChapter("text/part 1.xhtml", "<p>One</p>")
            .WithManifestItem("styles/book.css", "p { margin: 0; }", "text/css")
            .WithManifestItem("images/plate.png", TestImages.Png(40, 30), "image/png")
            .Build();
        var part = await factory.IngestOnePartAsync(epub);
        var manifest = await GetSignedInManifestAsync(part.ItemId);
        using var client = factory.CreateClient();

        var files = Links(manifest["readingOrder"]).Concat(Links(manifest["resources"])).ToList();
        Assert.Equal(
            ["OEBPS/images/plate.png", "OEBPS/nav.xhtml", "OEBPS/styles/book.css", "OEBPS/text/part%201.xhtml"],
            files.Select(file => file.Href).Order(StringComparer.Ordinal));

        foreach (var (href, type) in files)
        {
            var response = await client.GetAsync(Resolve(manifest, href!), CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(type, response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(
                EntryBytes(epub, Uri.UnescapeDataString(href!)),
                await response.Content.ReadAsByteArrayAsync(CancellationToken));
        }
    }

    [Fact]
    public async Task A_resource_can_be_cached_privately_for_a_day()
    {
        var part = await factory.IngestOnePartAsync(new EpubBuilder().WithTitle("Kindred").Build());
        var manifest = await GetSignedInManifestAsync(part.ItemId);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            Resolve(manifest, Links(manifest["readingOrder"])[0].Href!), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cacheControl = response.Headers.CacheControl;
        Assert.NotNull(cacheControl);
        Assert.True(cacheControl.Private, $"Cache-Control is {cacheControl}.");
        Assert.Equal(TimeSpan.FromDays(1), cacheControl.MaxAge);
        Assert.Null(response.Headers.ETag);
    }

    [Theory]
    [InlineData("META-INF/container.xml")]
    [InlineData("mimetype")]
    [InlineData("OEBPS/content.opf")]
    [InlineData("..%2Fmimetype")]
    [InlineData("%2Fetc%2Fpasswd")]
    public async Task A_path_outside_the_package_manifest_is_not_found(string path)
    {
        var part = await factory.IngestOnePartAsync(new EpubBuilder().WithTitle("Kindred").Build());
        var manifest = await GetSignedInManifestAsync(part.ItemId);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(Resolve(manifest, path), CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("no-referrer", ReferrerPolicyOf(response));
    }

    [Theory]
    [InlineData("OEBPS/chapter.xhtml")]
    [InlineData("~readium/positions.json")]
    public async Task A_resource_path_without_a_token_is_not_found(string path)
    {
        var part = await factory.IngestOnePartAsync(new EpubBuilder().WithTitle("Kindred").Build());
        using var client = await factory.CreateSignedInClientAsync();
        var manifest = await GetManifestAsync(client, part.ItemId);
        Assert.Contains(Links(manifest["readingOrder"]).Concat(Links(manifest["links"])), link => link.Href == path);

        var response = await client.GetAsync($"/api/items/{part.ItemId}/publication/{path}", CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_token_for_another_item_is_not_found()
    {
        var one = await factory.IngestOnePartAsync(new EpubBuilder().WithTitle("Kindred").Build());
        var other = await factory.IngestOnePartAsync(new EpubBuilder().WithTitle("Dawn").Build());
        var manifest = await GetSignedInManifestAsync(one.ItemId);
        var token = TokenOf(SelfHref(manifest));
        using var client = factory.CreateClient();

        foreach (var path in TokenPaths(manifest))
        {
            var response = await client.GetAsync(TokenUrl(other.ItemId, token, path), CancellationToken);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task A_changed_token_is_not_found()
    {
        var part = await factory.IngestOnePartAsync(new EpubBuilder().WithTitle("Kindred").Build());
        var manifest = await GetSignedInManifestAsync(part.ItemId);
        var token = TokenOf(SelfHref(manifest));
        var changed = string.Concat(token[..10], token[10] == 'A' ? 'B' : 'A', token[11..]);
        using var client = factory.CreateClient();

        foreach (var path in TokenPaths(manifest))
        {
            Assert.Equal(HttpStatusCode.OK,
                (await client.GetAsync(TokenUrl(part.ItemId, token, path), CancellationToken)).StatusCode);

            var response = await client.GetAsync(TokenUrl(part.ItemId, changed, path), CancellationToken);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task A_token_of_a_deleted_user_is_not_found()
    {
        var part = await factory.IngestOnePartAsync(new EpubBuilder().WithTitle("Kindred").Build());
        var user = await factory.CreateUserAsync();
        using var signedIn = factory.CreateClient();
        (await signedIn.LoginAsync(user.Email!, ApiTestExtensions.Password)).EnsureSuccessStatusCode();
        var manifest = await GetManifestAsync(signedIn, part.ItemId);
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(SelfHref(manifest), CancellationToken)).StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<NookUser>>();
            var result = await users.DeleteAsync((await users.FindByIdAsync(user.Id.ToString()))!);
            Assert.True(result.Succeeded);
        }

        foreach (var path in TokenPaths(manifest))
        {
            var response = await client.GetAsync(Resolve(manifest, path), CancellationToken);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task A_script_file_is_not_found_by_media_type_or_extension()
    {
        var part = await factory.IngestOnePartAsync(ScriptedBook());
        var manifest = await GetSignedInManifestAsync(part.ItemId);
        using var client = factory.CreateClient();

        foreach (var script in ScriptHrefs)
        {
            Assert.Contains(Links(manifest["resources"]), link => link.Href == script);

            var response = await client.GetAsync(Resolve(manifest, script), CancellationToken);

            Assert.True(HttpStatusCode.NotFound == response.StatusCode, $"{script} answered {response.StatusCode}.");
        }

        var stylesheet = await client.GetAsync(Resolve(manifest, "OEBPS/styles/book.css"), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, stylesheet.StatusCode);
    }

    [Fact]
    public async Task Resources_carry_nosniff_no_referrer_and_a_sandbox_policy()
    {
        var part = await factory.IngestOnePartAsync(ScriptedBook());
        var manifest = await GetSignedInManifestAsync(part.ItemId);
        using var client = factory.CreateClient();

        foreach (var (href, _) in Links(manifest["readingOrder"]).Concat(Links(manifest["resources"]))
                     .Where(link => !ScriptHrefs.Contains(link.Href)))
        {
            var response = await client.GetAsync(Resolve(manifest, href!), CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("nosniff", ContentTypeOptionsOf(response));
            Assert.Equal("no-referrer", ReferrerPolicyOf(response));
            Assert.Equal(SandboxPolicy, ContentSecurityPolicyOf(response));
        }
    }

    [Fact]
    public async Task The_token_manifest_and_positions_list_carry_nosniff_and_no_referrer_without_the_sandbox_policy()
    {
        var part = await factory.IngestOnePartAsync(new EpubBuilder().WithTitle("Kindred").Build());
        var manifest = await GetSignedInManifestAsync(part.ItemId);
        using var client = factory.CreateClient();

        foreach (var path in new[] { "manifest.json", PositionsHrefOf(manifest) })
        {
            var response = await client.GetAsync(Resolve(manifest, path), CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("nosniff", ContentTypeOptionsOf(response));
            Assert.Equal("no-referrer", ReferrerPolicyOf(response));
            Assert.NotEqual(SandboxPolicy, ContentSecurityPolicyOf(response));
        }
    }

    [Fact]
    public async Task A_refused_script_an_unlisted_path_and_a_changed_token_carry_nosniff_and_no_referrer_without_the_sandbox_policy()
    {
        var part = await factory.IngestOnePartAsync(ScriptedBook());
        var manifest = await GetSignedInManifestAsync(part.ItemId);
        var token = TokenOf(SelfHref(manifest));
        var changed = string.Concat(token[..10], token[10] == 'A' ? 'B' : 'A', token[11..]);
        using var client = factory.CreateClient();

        foreach (var url in new[]
                 {
                     Resolve(manifest, "OEBPS/scripts/run.js").ToString(),
                     Resolve(manifest, "META-INF/container.xml").ToString(),
                     TokenUrl(part.ItemId, changed, "OEBPS/styles/book.css"),
                     TokenUrl(part.ItemId, changed, "manifest.json"),
                 })
        {
            var response = await client.GetAsync(url, CancellationToken);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("nosniff", ContentTypeOptionsOf(response));
            Assert.Equal("no-referrer", ReferrerPolicyOf(response));
            Assert.NotEqual(SandboxPolicy, ContentSecurityPolicyOf(response));
        }
    }

    [Fact]
    public async Task A_book_resource_keeps_its_own_policy()
    {
        var part = await factory.IngestOnePartAsync(ScriptedBook());
        var manifest = await GetSignedInManifestAsync(part.ItemId);
        using var client = factory.CreateClient();

        var resource = await client.GetAsync(Resolve(manifest, "OEBPS/styles/book.css"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, resource.StatusCode);
        Assert.Equal(SandboxPolicy, ContentSecurityPolicyOf(resource));

        foreach (var path in new[] { "manifest.json", PositionsHrefOf(manifest), "OEBPS/scripts/run.js" })
        {
            var response = await client.GetAsync(Resolve(manifest, path), CancellationToken);

            Assert.Equal(WebAppContentSecurityPolicy.Value, ContentSecurityPolicyOf(response));
        }
    }

    private const string SandboxPolicy = "sandbox; default-src 'none'; style-src 'unsafe-inline'; img-src data:";

    /// <summary>
    /// The files of <see cref="ScriptedBook"/> that are scripts by their extension or their declared media type, all
    /// declared in the package manifest.
    /// </summary>
    private static readonly string[] ScriptHrefs =
    [
        "OEBPS/scripts/run.js",
        "OEBPS/scripts/module.mjs",
        "OEBPS/scripts/plain.txt",
        "OEBPS/scripts/legacy.txt",
        "OEBPS/scripts/versioned.txt",
        "OEBPS/scripts/cased.txt",
    ];

    private static byte[] ScriptedBook() =>
        new EpubBuilder()
            .WithTitle("Kindred")
            .WithManifestItem("scripts/run.js", "alert(1);", "text/plain")
            .WithManifestItem("scripts/module.mjs", "alert(1);", "text/plain")
            .WithManifestItem("scripts/plain.txt", "alert(1);", "text/javascript")
            .WithManifestItem("scripts/legacy.txt", "alert(1);", "application/x-javascript")
            .WithManifestItem("scripts/versioned.txt", "alert(1);", "text/javascript1.5")
            .WithManifestItem("scripts/cased.txt", "alert(1);", "Text/JavaScript; charset=utf-8")
            .WithManifestItem("styles/book.css", "p { margin: 0; }", "text/css")
            .Build();

    private static string? ContentTypeOptionsOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Content-Type-Options", out var values) ? string.Join(", ", values) : null;

    private static string? ContentSecurityPolicyOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Content-Security-Policy", out var values) ? string.Join(", ", values) : null;

    private static string ManifestUrl(Guid itemId) => $"/api/items/{itemId}/publication/manifest.json";

    private static string TokenUrl(Guid itemId, string token, string path) =>
        $"/api/items/{itemId}/publication/t/{token}/{path}";

    /// <summary>The manifest, the positions list and the first chapter, relative to the self link.</summary>
    private static string[] TokenPaths(JsonObject manifest) =>
        ["manifest.json", PositionsHrefOf(manifest), Links(manifest["readingOrder"])[0].Href!];

    /// <summary>An href of the manifest as an absolute URL, resolved against its self link.</summary>
    private static Uri Resolve(JsonObject manifest, string href) => new(new Uri(SelfHref(manifest)), href);

    private static string PositionsHrefOf(JsonObject manifest)
    {
        var links = Assert.IsType<JsonArray>(manifest["links"]);
        var positions = Assert.Single(links,
            link => (string?)link?["type"] == "application/vnd.readium.position-list+json");
        var href = (string?)positions?["href"];
        Assert.NotNull(href);
        return href;
    }

    private static string? ReferrerPolicyOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Referrer-Policy", out var values) ? string.Join(", ", values) : null;

    private async Task<JsonObject> GetSignedInManifestAsync(Guid itemId)
    {
        using var client = await factory.CreateSignedInClientAsync();
        return await GetManifestAsync(client, itemId);
    }

    private static Dictionary<string, long> CompressedLengths(byte[] epub)
    {
        using var archive = new ZipArchive(new MemoryStream(epub), ZipArchiveMode.Read);
        return archive.Entries.ToDictionary(entry => entry.FullName, entry => entry.CompressedLength);
    }

    private static byte[] EntryBytes(byte[] epub, string entryName)
    {
        using var archive = new ZipArchive(new MemoryStream(epub), ZipArchiveMode.Read);
        var entry = archive.GetEntry(entryName);
        Assert.NotNull(entry);
        using var stream = entry.Open();
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    private static async Task<JsonObject> GetManifestAsync(HttpClient client, Guid itemId)
    {
        var response = await client.GetAsync(ManifestUrl(itemId), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadManifestAsync(response);
    }

    private static async Task<JsonObject> ReadManifestAsync(HttpResponseMessage response) =>
        Assert.IsType<JsonObject>(JsonNode.Parse(await response.Content.ReadAsStringAsync(CancellationToken)));

    private static string SelfHref(JsonObject manifest)
    {
        var links = Assert.IsType<JsonArray>(manifest["links"]);
        var self = Assert.Single(links, link => (string?)link?["rel"] == "self");
        var href = (string?)self?["href"];
        Assert.NotNull(href);
        return href;
    }

    private static string TokenOf(string selfHref) => selfHref.Split('/')[^2];

    private static List<string?> Strings(JsonNode? array) =>
        Assert.IsType<JsonArray>(array).Select(value => (string?)value).ToList();

    private static List<(string? Href, string? Type)> Links(JsonNode? array) =>
        Assert.IsType<JsonArray>(array).Select(link => ((string?)link?["href"], (string?)link?["type"])).ToList();
}
