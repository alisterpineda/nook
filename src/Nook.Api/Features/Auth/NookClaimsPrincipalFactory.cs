using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nook.Api.Data;

namespace Nook.Api.Features.Auth;

/// <summary>
/// Adds the name of the user's <see cref="BuiltInRole"/> to their claims, so policies can require a role, and a
/// <see cref="Policies.CanUploadClaim"/> claim when their role may upload.
/// </summary>
public sealed class NookClaimsPrincipalFactory(
    UserManager<NookUser> userManager, IOptions<IdentityOptions> options, NookDbContext db)
    : UserClaimsPrincipalFactory<NookUser>(userManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(NookUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.BuiltInRole is { } role)
        {
            identity.AddClaim(new Claim(Options.ClaimsIdentity.RoleClaimType, role.ToString()));
        }

        if (await db.Roles.AsNoTracking().AnyAsync(r => r.Id == user.RoleId && r.CanUpload))
        {
            identity.AddClaim(new Claim(Policies.CanUploadClaim, "true"));
        }

        return identity;
    }
}
