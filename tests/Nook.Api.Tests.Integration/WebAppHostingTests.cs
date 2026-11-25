using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Nook.Api.Features.Security;

namespace Nook.Api.Tests.Integration;

public sealed class WebAppHostingTests : IClassFixture<NookApiFactory>, IDisposable
{
    private const string IndexHtml = "<!doctype html><title>Nook</title>";
    private const string MainJs = "console.log('nook');";

    private readonly string _webRoot;
    private readonly WebApplicationFactory<Program> _app;

    /// <summary>Runs the API over a stand-in for the built web app, so the tests need no npm build.</summary>
    public WebAppHostingTests(NookApiFactory factory)
    {
        _webRoot = Directory.CreateTempSubdirectory("nook-web-").FullName;
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), IndexHtml);
        File.WriteAllText(Path.Combine(_webRoot, "main.js"), MainJs);

        _app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Configure<StaticFileOptions>(options => options.FileProvider = new PhysicalFileProvider(_webRoot))));
    }

    [Fact]
    public async Task Unknown_path_returns_the_web_app()
    {
        using var client = _app.CreateClient();

        var response = await client.GetAsync("/works/123", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(IndexHtml, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Root_returns_the_web_app()
    {
        using var client = _app.CreateClient();

        var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(IndexHtml, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_built_file_is_served()
    {
        using var client = _app.CreateClient();

        var response = await client.GetAsync("/main.js", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MainJs, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("/index.html")]
    [InlineData("/")]
    [InlineData("/works/123")]
    public async Task The_web_app_page_is_never_served_from_a_stale_cache(string path)
    {
        using var client = _app.CreateClient();

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoCache);
    }

    [Fact]
    public async Task A_built_file_does_not_ask_to_be_revalidated()
    {
        using var client = _app.CreateClient();

        var response = await client.GetAsync("/main.js", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(true, response.Headers.CacheControl?.NoCache);
    }

    [Fact]
    public async Task Unknown_api_path_does_not_return_the_web_app()
    {
        using var client = _app.CreateClient();

        var response = await client.GetAsync("/api/nope", TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Api_endpoints_still_need_sign_in()
    {
        using var client = _app.CreateClient();

        var response = await client.GetAsync("/api/works", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/works/123")]
    [InlineData("/main.js")]
    public async Task The_web_app_and_its_files_carry_the_content_security_policy(string path)
    {
        using var client = _app.CreateClient();

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([WebAppPolicy], response.Headers.GetValues("Content-Security-Policy"));
    }

    [Fact]
    public async Task An_api_response_carries_the_content_security_policy()
    {
        using var anonymous = _app.CreateClient();
        using var signedIn = await _app.CreateSignedInClientAsync();

        var responses = new[]
        {
            await signedIn.GetAsync("/api/works", TestContext.Current.CancellationToken),
            await anonymous.GetAsync("/api/works", TestContext.Current.CancellationToken),
            await signedIn.GetAsync("/api/nope", TestContext.Current.CancellationToken),
        };

        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.Unauthorized, HttpStatusCode.NotFound],
            responses.Select(response => response.StatusCode));
        Assert.All(responses, response =>
            Assert.Equal([WebAppPolicy], response.Headers.GetValues("Content-Security-Policy")));
    }

    [Fact]
    public async Task Every_response_tells_crawlers_not_to_index()
    {
        using var client = _app.CreateClient();
        using var signedIn = await _app.CreateSignedInClientAsync();

        var responses = new[]
        {
            await client.GetAsync("/", TestContext.Current.CancellationToken),
            await client.GetAsync("/library", TestContext.Current.CancellationToken),
            await client.GetAsync("/main.js", TestContext.Current.CancellationToken),
            await client.GetAsync("/api/access", TestContext.Current.CancellationToken),
            await client.GetAsync("/api/works", TestContext.Current.CancellationToken),
            await signedIn.GetAsync("/api/nope", TestContext.Current.CancellationToken),
        };

        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK,
                HttpStatusCode.Unauthorized, HttpStatusCode.NotFound],
            responses.Select(response => response.StatusCode));
        Assert.All(responses, response =>
            Assert.Equal(["noindex, nofollow"], response.Headers.GetValues("X-Robots-Tag")));
    }

    [Fact]
    public async Task Every_response_tells_the_browser_not_to_guess_its_type()
    {
        using var client = _app.CreateClient();
        using var signedIn = await _app.CreateSignedInClientAsync();

        var responses = new[]
        {
            await client.GetAsync("/", TestContext.Current.CancellationToken),
            await client.GetAsync("/library", TestContext.Current.CancellationToken),
            await client.GetAsync("/main.js", TestContext.Current.CancellationToken),
            await client.GetAsync("/api/access", TestContext.Current.CancellationToken),
            await client.GetAsync("/api/works", TestContext.Current.CancellationToken),
            await signedIn.GetAsync("/api/nope", TestContext.Current.CancellationToken),
        };

        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK,
                HttpStatusCode.Unauthorized, HttpStatusCode.NotFound],
            responses.Select(response => response.StatusCode));
        Assert.All(responses, response =>
            Assert.Equal(["nosniff"], response.Headers.GetValues("X-Content-Type-Options")));
    }

    [Fact]
    public async Task Robots_txt_lets_in_three_search_engines_and_turns_every_other_crawler_away()
    {
        using var client = _app.CreateClient();

        var response = await client.GetAsync("/robots.txt", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "User-agent: Googlebot\nUser-agent: Bingbot\nUser-agent: Yandex\nAllow: /\n\nUser-agent: *\nDisallow: /\n",
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The dev server serves the web app in development, so it has to send the policy the API sends in production,
    /// widened only by the socket its live reload uses. The two are separate copies, and this keeps them the same.
    /// </summary>
    [Fact]
    public void The_dev_server_sends_the_same_policy_plus_its_live_reload_socket()
    {
        using var angularJson = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src", "Nook.Web", "angular.json")));

        var devServerPolicy = angularJson.RootElement
            .GetProperty("projects").GetProperty("nook-web").GetProperty("architect").GetProperty("serve")
            .GetProperty("options").GetProperty("headers").GetProperty("Content-Security-Policy").GetString();

        const string ApiConnectSource = "connect-src 'self' blob:";
        Assert.Contains(ApiConnectSource, WebAppContentSecurityPolicy.Value);
        Assert.Equal(
            WebAppContentSecurityPolicy.Value.Replace(ApiConnectSource, "connect-src 'self' ws://localhost:4200 blob:"),
            devServerPolicy);
    }

    /// <summary>The folder that holds the solution file, found by walking up from where the tests run.</summary>
    private static string RepositoryRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Nook.slnx")))
            {
                return folder.FullName;
            }
        }

        throw new InvalidOperationException($"No Nook.slnx above {AppContext.BaseDirectory}.");
    }

    /// <summary>The policy as the web app needs it, written out in full so a change to it is a change to a test.</summary>
    private const string WebAppPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline' blob:; img-src 'self' blob: data:; font-src 'self' blob: data:; media-src 'self' blob:; connect-src 'self' blob:; frame-src blob:; child-src blob:; worker-src 'self' blob:; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'self'";

    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    public async Task Health_path_answers_healthy_outside_development(string path)
    {
        using var production = _app.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = production.CreateClient();

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(IndexHtml, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        _app.Dispose();
        Directory.Delete(_webRoot, recursive: true);
    }
}
