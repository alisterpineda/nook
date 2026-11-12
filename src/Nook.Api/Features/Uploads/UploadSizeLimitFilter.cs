using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Options;

namespace Nook.Api.Features.Uploads;

/// <summary>
/// Holds an upload's body to <see cref="UploadOptions.MaxBytes"/>, plus room for the rest of the form, before anything
/// reads it. A body that declares a larger length is refused with 413 and never read. Otherwise the server's body limit
/// and the form's multipart limit are set to that same figure, so the server's own smaller defaults do not refuse a file
/// the setting allows. The action still checks the file it receives against <see cref="UploadOptions.MaxBytes"/>.
/// </summary>
public sealed class UploadSizeLimitFilter(IOptions<UploadOptions> options, ProblemDetailsFactory problems) : IResourceFilter
{
    /// <summary>Room for what travels beside the file: the form's boundaries, each part's headers and the other fields.</summary>
    public const long MultipartAllowance = 1024 * 1024;

    /// <summary>The title of the problem an upload over the limit answers with.</summary>
    public const string TooLargeTitle = "The file is larger than an upload may be.";

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var maxBytes = options.Value.MaxBytes;
        var bodyLimit = maxBytes > long.MaxValue - MultipartAllowance ? long.MaxValue : maxBytes + MultipartAllowance;
        var httpContext = context.HttpContext;

        if (httpContext.Request.ContentLength > bodyLimit)
        {
            var problem = problems.CreateProblemDetails(
                httpContext,
                StatusCodes.Status413PayloadTooLarge,
                title: TooLargeTitle,
                detail: TooLargeDetail(maxBytes));
            context.Result = new ObjectResult(problem) { StatusCode = problem.Status };
            return;
        }

        // Kestrel offers this feature and allows 30,000,000 bytes by default; the in-memory test server offers none.
        if (httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodySize)
        {
            bodySize.MaxRequestBodySize = bodyLimit;
        }

        // The form is read during model binding, after this filter, through whichever form feature the request holds.
        if (httpContext.Features.Get<IFormFeature>()?.Form is null)
        {
            httpContext.Features.Set<IFormFeature>(
                new FormFeature(httpContext.Request, new FormOptions { MultipartBodyLengthLimit = bodyLimit }));
        }
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }

    /// <summary>The detail of the problem an upload over the limit answers with.</summary>
    public static string TooLargeDetail(long maxBytes) => $"An upload may carry a file of at most {maxBytes} bytes.";
}
