using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nook.Api.Data;
using Nook.Api.Ingest;
using Nook.Api.Jobs;
using Nook.Api.Storage;

namespace Nook.Api.Features.Uploads;

/// <summary>Files sent in to be ingested into a library.</summary>
[ApiController]
[Route("api/uploads")]
[Authorize(Roles = nameof(UserRole.Admin))]
public class UploadsController(
    NookDbContext db, JobQueue queue, NookStorage storage, IOptions<UploadOptions> options) : ControllerBase
{
    /// <summary>
    /// Stages a file and starts the job that ingests it into the library. Any file is accepted: the job decides whether
    /// Nook can read it, and fails if not. A file larger than the upload limit is refused.
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [TypeFilter<UploadSizeLimitFilter>]
    [ProducesResponseType<UploadResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<UploadResponse>> Upload([FromForm] UploadRequest request, CancellationToken cancellationToken)
    {
        // Both are required, so the framework has already refused a request without them.
        var libraryId = request.LibraryId!.Value;
        var file = request.File!;

        // The filter refused a body declared too large; this catches a file that is too large in a body that fits.
        var maxBytes = options.Value.MaxBytes;
        if (file.Length > maxBytes)
        {
            return Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: UploadSizeLimitFilter.TooLargeTitle,
                detail: UploadSizeLimitFilter.TooLargeDetail(maxBytes));
        }

        if (!await db.Libraries.AnyAsync(l => l.Id == libraryId, cancellationToken))
        {
            ModelState.AddModelError(nameof(request.LibraryId), "There is no library with this id.");
            return ValidationProblem(ModelState);
        }

        // The staged name is new and carries no extension; the uploaded name travels in the payload only.
        var stagedName = Guid.NewGuid().ToString();
        var stagedPath = storage.StagedFile(stagedName);
        Guid jobId;
        try
        {
            await using (var staged = NookStorage.CreateFile(stagedPath))
            {
                await file.CopyToAsync(staged, cancellationToken);
            }

            var payload = new IngestPayload(libraryId, stagedName, Path.GetFileName(file.FileName));
            jobId = await queue.EnqueueAsync(IngestJob.TypeName, payload, cancellationToken: cancellationToken);
        }
        catch
        {
            // No job points at the file, so nothing would ever delete it.
            System.IO.File.Delete(stagedPath);
            throw;
        }

        return Accepted(new UploadResponse(jobId));
    }
}
