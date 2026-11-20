using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nook.Api.Data;
using Nook.Api.Features.Audit;
using Nook.Api.Features.Auth;
using Nook.Api.Features.Sections;
using Nook.Api.Ingest;
using Nook.Api.Jobs;
using Nook.Api.Storage;

namespace Nook.Api.Features.Uploads;

/// <summary>Files sent in to be ingested into a section or the Default area.</summary>
[ApiController]
[Route("api/uploads")]
[Authorize(Policy = Policies.Upload)]
public class UploadsController(
    NookDbContext db, JobQueue queue, NookStorage storage, IOptions<UploadOptions> options, SectionAccess access,
    AuditLog audit) : ControllerBase
{
    /// <summary>The largest file an upload may carry, the limit <see cref="Upload"/> enforces.</summary>
    [HttpGet("limits")]
    [ProducesResponseType<UploadLimitsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<UploadLimitsResponse> Limits() => new UploadLimitsResponse(options.Value.MaxBytes);

    /// <summary>
    /// Stages a file and starts the job that ingests it into the section, or into the Default area when none is given.
    /// Any file is accepted: the job decides whether Nook can read it, and fails if not. A file larger than the upload
    /// limit is refused.
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
        // The file is required, so the framework has already refused a request without it. No section id means the
        // Default area.
        var sectionId = request.SectionId;
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

        string? sectionName = null;
        if (sectionId is { } id)
        {
            sectionName = await db.Sections.Where(s => s.Id == id).Select(s => s.Name).SingleOrDefaultAsync(cancellationToken);
        }

        if (sectionId is not null && sectionName is null)
        {
            ModelState.AddModelError(nameof(request.SectionId), "There is no section with this id.");
            return ValidationProblem(ModelState);
        }

        // Uploading is for a role that may see the section, as the sections list shows it.
        var grants = await access.ForAsync(User, cancellationToken);
        if (grants.In(sectionId) < ContentAccess.Browse)
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: "Your role cannot see this section, so it cannot add books to it.");
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

            var fileName = Path.GetFileName(file.FileName);
            jobId = Guid.CreateVersion7();

            // The entry is added to this scope's context, so the save that stores the job stores it too.
            var submission = await audit.AddAsync(
                User,
                AuditActions.UploadSubmitted,
                new AuditSubject(AuditSubjectTypes.Job, jobId, fileName),
                new { fileName, sectionId, sectionName },
                isSubmission: true,
                cancellationToken: cancellationToken);

            var payload = new IngestPayload(sectionId, stagedName, fileName, submission.Id);
            await queue.EnqueueAsync(IngestJob.TypeName, payload, id: jobId, cancellationToken: cancellationToken);
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
