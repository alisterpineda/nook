namespace Nook.Api.Features.Security;

/// <summary>
/// Tells the browser to trust the type a response names and not guess one from its bytes, so an uploaded file
/// served as a download can never be run as a script or page. It is the same for every response.
/// </summary>
public static class ContentTypeOptions
{
    public const string NoSniff = "nosniff";

    /// <summary>
    /// Adds <c>X-Content-Type-Options</c> as the response starts, to every response: the web app's pages and built
    /// files, the API's answers, a part's file, and errors. Like the crawler directives, it must come before
    /// <c>UseExceptionHandler</c>, so it still runs on the error the handler writes in place of a failed response.
    /// </summary>
    public static IApplicationBuilder UseNoSniff(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.XContentTypeOptions = NoSniff;
                return Task.CompletedTask;
            });
            return next(context);
        });
}
