namespace Nook.Api.Features.Security;

/// <summary>
/// Keeps Nook out of search results and away from crawlers that would copy it. Every response says not to index it,
/// and <c>robots.txt</c> lets in only search engines that honour that. A crawler that <c>robots.txt</c> keeps away
/// never sees <c>noindex</c> and can still list the bare address, while AI and scraper crawlers name
/// <c>robots.txt</c> as their only way to opt out, so every other agent is turned away there.
/// </summary>
public static class CrawlerDirectives
{
    public const string RobotsTag = "noindex, nofollow";

    public const string RobotsTxt =
        "User-agent: Googlebot\n"
        + "User-agent: Bingbot\n"
        + "User-agent: Yandex\n"
        + "Allow: /\n"
        + "\n"
        + "User-agent: *\n"
        + "Disallow: /\n";

    /// <summary>
    /// Adds <c>X-Robots-Tag</c> as the response starts, to every response: the web app's pages and built files, the
    /// API's answers, and errors. Like the content security policy, it must come before <c>UseExceptionHandler</c>,
    /// so it still runs on the error the handler writes in place of a failed response.
    /// </summary>
    public static IApplicationBuilder UseCrawlerDirectives(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers["X-Robots-Tag"] = RobotsTag;
                return Task.CompletedTask;
            });
            return next(context);
        });

    /// <summary>
    /// Serves <c>robots.txt</c> from the API, so it is the same with or without a built web app and whether public
    /// access is on or off. It needs no session and stays out of the API description.
    /// </summary>
    public static IEndpointConventionBuilder MapRobotsTxt(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/robots.txt", () => Results.Text(RobotsTxt, "text/plain"))
            .AllowAnonymous()
            .ExcludeFromDescription();
}
