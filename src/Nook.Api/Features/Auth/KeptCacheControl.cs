namespace Nook.Api.Features.Auth;

/// <summary>
/// Lets an endpoint choose a <c>Cache-Control</c> that outlasts the session cookie's renewal. A response that renews
/// the cookie has its <c>Cache-Control</c> replaced with <c>no-cache,no-store</c> by the cookie handler, with
/// <c>Pragma</c> and <c>Expires</c> to match, as the response starts. A renewal can fall on any request, so without
/// this a response meant to be kept by the browser would now and then not be.
/// </summary>
public static class KeptCacheControl
{
    private static readonly object ItemKey = new();

    /// <summary>
    /// Puts the endpoint's choice back as the response starts. It must come before <c>UseAuthentication</c>: the
    /// callbacks run last registered first, so this one runs after the cookie handler's.
    /// </summary>
    public static IApplicationBuilder UseKeptCacheControl(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                if (IsTheEndpointsOwn(context.Response)
                    && context.Items.TryGetValue(ItemKey, out var kept) && kept is string cacheControl)
                {
                    var headers = context.Response.Headers;
                    headers.CacheControl = cacheControl;
                    headers.Remove("Pragma");
                    headers.Remove("Expires");
                }

                return Task.CompletedTask;
            });
            return next(context);
        });

    /// <summary>
    /// Whether the response is still the one the endpoint made. An error that took its place, such as a 500 from the
    /// exception handler, keeps the caching headers it was given, so a browser never keeps an error for the endpoint's
    /// answer.
    /// </summary>
    private static bool IsTheEndpointsOwn(HttpResponse response) =>
        response.StatusCode is (>= 200 and < 300) or StatusCodes.Status304NotModified;

    /// <summary>Sets the response's <c>Cache-Control</c> so that it stays even when the response renews the cookie.</summary>
    public static void KeepCacheControl(this HttpResponse response, string cacheControl)
    {
        response.Headers.CacheControl = cacheControl;
        response.HttpContext.Items[ItemKey] = cacheControl;
    }
}
