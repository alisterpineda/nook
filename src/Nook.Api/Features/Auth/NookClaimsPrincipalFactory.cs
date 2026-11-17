using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Nook.Api.Data;

namespace Nook.Api.Features.Auth;

/// <summary>Adds the name of the user's <see cref="BuiltInRole"/> to their claims, so policies can require a role.</summary>
public sealed class NookClaimsPrincipalFactory(UserManager<NookUser> userManager, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<NookUser>(userManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(NookUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.BuiltInRole is { } role)
        {
            identity.AddClaim(new Claim(Options.ClaimsIdentity.RoleClaimType, role.ToString()));
        }

        return identity;
    }
}
