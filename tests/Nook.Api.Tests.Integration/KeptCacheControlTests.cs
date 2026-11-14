using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Features.Auth;

namespace Nook.Api.Tests.Integration;

public class KeptCacheControlTests
{
    private const string Kept = "private, max-age=31536000, immutable";

    [Fact]
    public async Task Kept_cache_control_stays_when_something_replaces_it_as_the_response_starts()
    {
        await using var app = await StartAsync(context =>
        {
            context.Response.KeepCacheControl(Kept);
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.CacheControl = "no-cache,no-store";
                context.Response.Headers.Pragma = "no-cache";
                return Task.CompletedTask;
            });
            return context.Response.WriteAsync("kept");
        });

        var response = await app.GetTestClient().GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cacheControl = response.Headers.CacheControl;
        Assert.NotNull(cacheControl);
        Assert.Equal(TimeSpan.FromSeconds(31536000), cacheControl.MaxAge);
        Assert.False(cacheControl.NoStore, $"Cache-Control is {cacheControl}.");
        Assert.False(response.Headers.Contains("Pragma"), "The response carries Pragma.");
    }

    [Fact]
    public async Task Kept_cache_control_does_not_reach_the_error_that_takes_the_responses_place()
    {
        await using var app = await StartAsync(context =>
        {
            context.Response.KeepCacheControl(Kept);
            throw new IOException("The file could not be read.");
        });

        var response = await app.GetTestClient().GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var cacheControl = response.Headers.CacheControl;
        Assert.NotNull(cacheControl);
        Assert.True(cacheControl.NoStore, $"Cache-Control is {cacheControl}.");
        Assert.Null(cacheControl.MaxAge);
    }

    /// <summary>Starts a small app whose one endpoint sits behind the exception handler and the kept header.</summary>
    private static async Task<WebApplication> StartAsync(RequestDelegate endpoint)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();

        var app = builder.Build();
        app.UseExceptionHandler();
        app.UseKeptCacheControl();
        app.Run(endpoint);

        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }
}
