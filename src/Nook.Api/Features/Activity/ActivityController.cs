using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Nook.Api.Data;
using Nook.Api.Features.Jobs;

namespace Nook.Api.Features.Activity;

/// <summary>What the signed-in user has sent in.</summary>
[ApiController]
[Route("api/activity")]
[Authorize]
public class ActivityController(UploadActivity uploads, UserManager<NookUser> users, JobActions actions) : ControllerBase
{
    private const int UploadLimit = 20;

    /// <summary>The caller's 20 most recent uploads, newest first.</summary>
    [HttpGet("uploads")]
    [ProducesResponseType<IReadOnlyList<UploadActivityResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<UploadActivityResponse>>> ListUploads(CancellationToken cancellationToken) =>
        Ok(await uploads.ForUserAsync(Guid.Parse(users.GetUserId(User)!), UploadLimit, cancellationToken));

    /// <summary>
    /// Sends the caller's failed upload back to pending, to run again from its first step. An upload that has not
    /// failed is refused, and one the caller did not submit is not found.
    /// </summary>
    [HttpPost("uploads/{jobId:guid}/retry")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RetryUpload(Guid jobId, CancellationToken cancellationToken)
    {
        if (!await IsOwnUploadAsync(jobId, cancellationToken))
        {
            return NotFound();
        }

        return (await actions.RetryAsync(User, jobId, cancellationToken)).ToResult(this);
    }

    /// <summary>
    /// Removes the caller's upload and its staged file, with the same refusals as deleting the job: a running upload
    /// is refused, and one the caller did not submit is not found.
    /// </summary>
    [HttpDelete("uploads/{jobId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveUpload(Guid jobId, CancellationToken cancellationToken)
    {
        if (!await IsOwnUploadAsync(jobId, cancellationToken))
        {
            return NotFound();
        }

        return (await actions.DeleteAsync(User, jobId, cancellationToken)).ToResult(this);
    }

    /// <summary>Whether the caller submitted the upload whose job this is.</summary>
    private async Task<bool> IsOwnUploadAsync(Guid jobId, CancellationToken cancellationToken) =>
        (await uploads.ForJobAsync(jobId, cancellationToken))?.UserId == Guid.Parse(users.GetUserId(User)!);
}
