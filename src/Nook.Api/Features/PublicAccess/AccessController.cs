using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nook.Api.Features.Sections;

namespace Nook.Api.Features.PublicAccess;

/// <summary>What the caller may do, for a client to read before it knows whether anyone is signed in.</summary>
[ApiController]
[Route("api/access")]
[AllowAnonymous]
public class AccessController(InstanceSettings settings, SectionAccess sectionAccess) : ControllerBase
{
    /// <summary>Whether public access is on, and the caller's level in the Default area. Needs no session.</summary>
    [HttpGet]
    [ProducesResponseType<AccessResponse>(StatusCodes.Status200OK)]
    public async Task<AccessResponse> Get(CancellationToken cancellationToken)
    {
        var enabled = await settings.PublicAccessEnabledAsync(cancellationToken);
        var grants = await sectionAccess.ForAsync(User, cancellationToken);
        return new AccessResponse(enabled, grants.In(null));
    }
}
