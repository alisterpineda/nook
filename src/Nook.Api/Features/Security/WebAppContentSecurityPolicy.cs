namespace Nook.Api.Features.Security;

/// <summary>
/// The Content-Security-Policy the web app runs under, sent on every response the API host makes. The web app's
/// pages, its built files, the fallback that answers a path the browser routes, and every API answer, an error
/// included, all carry it. A response that sets its own policy, such as a book's file or a cover, keeps that one.
/// </summary>
public static class WebAppContentSecurityPolicy
{
    /// <summary>
    /// Scripts only from the app's own files, so a script in a book never runs in the page. Styles, images, fonts,
    /// media, frames and workers may also come from <c>blob:</c> URLs, which is how the reader shows a book.
    /// </summary>
    public const string Value =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline' blob:; "
        + "img-src 'self' blob: data:; font-src 'self' blob: data:; media-src 'self' blob:; "
        + "connect-src 'self' blob:; frame-src blob:; child-src blob:; worker-src 'self' blob:; object-src 'none'; "
        + "base-uri 'self'; form-action 'self'; frame-ancestors 'self'";

    /// <summary>
    /// Adds the policy as the response starts, unless the response already has one. It must come before
    /// <c>UseExceptionHandler</c>: the callbacks run last registered first, so this one runs after every other
    /// middleware's and the endpoint's have set their headers, and it still runs on the error the handler writes in
    /// place of a failed response.
    /// </summary>
    public static IApplicationBuilder UseWebAppContentSecurityPolicy(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                if (!headers.ContainsKey("Content-Security-Policy"))
                {
                    headers.ContentSecurityPolicy = Value;
                }

                return Task.CompletedTask;
            });
            return next(context);
        });
}
