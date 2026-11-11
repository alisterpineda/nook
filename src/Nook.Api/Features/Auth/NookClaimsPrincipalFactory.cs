using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Nook.Api.Data;

namespace Nook.Api.Features.Auth;

/// <summary>Adds the user's <see cref="UserRole"/> to their claims, so policies can require a role.</summary>
public sealed class NookClaimsPrincipalFactory(UserManager<NookUser> userManager, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<NookUser>(userManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(NookUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(Options.ClaimsIdentity.RoleClaimType, user.Role.ToString()));
        return identity;
    }
}
