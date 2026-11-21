using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Nook.Api.Features.Auth;

/// <summary>
/// Answers 404 when password sign-in is off. It is a resource filter, so it runs before model binding and validation:
/// a body that is not valid is not found either, rather than a bad request.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequiresPasswordSignInAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        if (!context.HttpContext.RequestServices.GetRequiredService<IOptions<PasswordSignInOptions>>().Value.Enabled)
        {
            context.Result = new NotFoundResult();
        }
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
