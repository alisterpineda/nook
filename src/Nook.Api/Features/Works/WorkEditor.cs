using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Nook.Api.Data;
using Nook.Api.Features.Audit;
using Nook.Api.Features.Auth;
using Nook.Api.Features.Sections;
using Nook.Api.Ingest;
using Nook.Api.Storage;

namespace Nook.Api.Features.Works;

/// <summary>Changes the works of the library, and says who may.</summary>
public class WorkEditor(
    NookDbContext db,
    NookStorage storage,
    SectionAccess access,
    AuditLog audit,
    IServiceScopeFactory scopes,
    IOptions<IdentityOptions> identityOptions,
    ILogger<WorkEditor> logger)
{
    private readonly WorkMatcher _matcher = new(db);
    private readonly WorkSearchIndex _searchIndex = new(db);

    /// <summary>
    /// Whether the user may change a work owned by <paramref name="ownerId"/>: an admin may change any work, and
    /// anyone else only a work they own while their role may upload. A work with no owner is for admins alone.
    /// </summary>
    public bool MayChange(ClaimsPrincipal user, Guid? ownerId)
    {
        if (user.IsInRole(nameof(BuiltInRole.Admin)))
        {
            return true;
        }

        return ownerId is { } owner
            && user.HasClaim(Policies.CanUploadClaim, "true")
            && user.FindFirstValue(identityOptions.Value.ClaimsIdentity.UserIdClaimType) == owner.ToString();
    }
}
