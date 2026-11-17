using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nook.Api.Data;
using Nook.Api.Features.PublicAccess;

namespace Nook.Api.Features.Sections;

/// <summary>
/// The one place that decides which sections, and so which works, a caller can see and how far they may go with them.
/// Every query that returns catalog data goes through the grants it gives, with <see cref="SectionVisibility"/>.
/// </summary>
public class SectionAccess(NookDbContext db, InstanceSettings settings, IOptions<IdentityOptions> identityOptions)
{
    /// <summary>The grants already worked out in this request, by user id, with the empty string for a visitor.</summary>
    private readonly Dictionary<string, SectionGrants> worked = [];

    /// <summary>
    /// The caller's grants. A signed-in user has their role's, joined with the Anonymous role's while public access
    /// is on, so they never get less than a visitor. A visitor has the Anonymous role's while public access is on,
    /// and nothing otherwise.
    /// </summary>
    public async Task<SectionGrants> ForAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var userId = user.FindFirstValue(identityOptions.Value.ClaimsIdentity.UserIdClaimType) ?? "";
        if (worked.TryGetValue(userId, out var known))
        {
            return known;
        }

        var grants = await settings.PublicAccessEnabledAsync(cancellationToken)
            ? await GrantsOfAsync(Role.AnonymousId, cancellationToken)
            : SectionGrants.None;

        if (Guid.TryParse(userId, out var id)
            && await db.Users.Where(u => u.Id == id).Select(u => (Guid?)u.RoleId).SingleOrDefaultAsync(cancellationToken)
                is { } roleId)
        {
            grants = grants.Combine(await GrantsOfAsync(roleId, cancellationToken));
        }

        worked[userId] = grants;
        return grants;
    }

    private async Task<SectionGrants> GrantsOfAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var role = await db.Roles.AsNoTracking().SingleOrDefaultAsync(r => r.Id == roleId, cancellationToken);
        if (role is null)
        {
            return SectionGrants.None;
        }

        var sections = role.AllSections
            ? []
            : await db.RoleSections.AsNoTracking().Where(r => r.RoleId == roleId).ToListAsync(cancellationToken);
        return SectionGrants.For(role, sections);
    }
}
