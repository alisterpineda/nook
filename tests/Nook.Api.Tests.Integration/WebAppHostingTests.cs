using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

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
    [InlineData("/health")]
    [InlineData("/alive")]
    public async Task Health_path_does_not_return_the_web_app_outside_development(string path)
    {
        using var production = _app.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = production.CreateClient();

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    public void Dispose()
    {
        _app.Dispose();
        Directory.Delete(_webRoot, recursive: true);
    }
}
