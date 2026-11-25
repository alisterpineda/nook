using Microsoft.AspNetCore.Mvc;

namespace Nook.Api.Features.Build;

/// <summary>
/// The version of the build that is running. It carries no authorization attribute: the fallback policy requires a
/// signed-in user, so a visitor cannot use it to look up known flaws.
/// </summary>
[ApiController]
[Route("api/version")]
public class VersionController : ControllerBase
{
    /// <summary>The version this build reports.</summary>
    [HttpGet]
    [ProducesResponseType<VersionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public VersionResponse Get() => new(BuildVersion.Current);
}
