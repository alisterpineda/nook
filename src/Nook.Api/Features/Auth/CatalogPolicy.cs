using Microsoft.AspNetCore.Authorization;
using Nook.Api.Features.PublicAccess;

namespace Nook.Api.Features.Auth;

/// <summary>The names of Nook's authorization policies.</summary>
public static class Policies
{
    /// <summary>
    /// The catalog: open to a signed-in user, and to a visitor with no session while public access is on. What either
    /// may see is narrowed by their section grants.
    /// </summary>
    public const string Catalog = "catalog";

    /// <summary>Uploading: open to a user whose role may upload.</summary>
    public const string Upload = "upload";

    /// <summary>The claim the cookie carries for a user whose role may upload.</summary>
    public const string CanUploadClaim = "can_upload";
}

/// <summary>Signed in, or public access on.</summary>
public sealed class CatalogRequirement : IAuthorizationRequirement;

/// <summary>
/// Lets a request into the catalog when it is signed in or public access is on. Otherwise it fails, and a request
/// with no session is challenged, so it gets 401.
/// </summary>
public sealed class CatalogHandler(InstanceSettings settings) : AuthorizationHandler<CatalogRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, CatalogRequirement requirement)
    {
        var cancellationToken = context.Resource is HttpContext http ? http.RequestAborted : default;
        if (context.User.Identity?.IsAuthenticated == true
            || await settings.PublicAccessEnabledAsync(cancellationToken))
        {
            context.Succeed(requirement);
        }
    }
}
