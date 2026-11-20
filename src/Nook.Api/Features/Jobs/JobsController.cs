using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;
using Nook.Api.Features.Audit;
using Nook.Api.Jobs;

namespace Nook.Api.Features.Jobs;

/// <summary>Background jobs, for admins to watch, retry and delete.</summary>
[ApiController]
[Route("api/jobs")]
[Authorize(Roles = nameof(BuiltInRole.Admin))]
public class JobsController(NookDbContext db, JobActions actions) : ControllerBase
{
    /// <summary>
    /// The summaries of the jobs, as the list and the activity hub give them. The submitter is the actor of the
    /// job's upload.submitted entry, or null when it has none: the same entry
    /// <see cref="AuditEntryQueries.SubmissionsOf"/> puts first, written out here because the query runs per job
    /// inside the database.
    /// </summary>
    internal static IQueryable<JobSummaryResponse> Summaries(NookDbContext db, IQueryable<Job> jobs) =>
        jobs.Select(j => new JobSummaryResponse(
            j.Id,
            j.Type,
            j.Status,
            j.Error,
            j.ParentJobId,
            j.NotBefore,
            j.AttemptsMade,
            j.AttemptsAllowed,
            j.ReclaimsMade,
            j.CreatedAt,
            j.UpdatedAt,
            db.AuditEntries
                .Where(e => e.Action == AuditActions.UploadSubmitted && e.SubjectId == j.Id)
                .OrderByDescending(e => e.CreatedAt)
                .ThenByDescending(e => e.Id)
                .Select(e => e.ActorName)
                .FirstOrDefault()));

    /// <summary>One page of the jobs in the statuses asked for, or of all jobs, newest first.</summary>
    [HttpGet]
    [ProducesResponseType<Page<JobSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<Page<JobSummaryResponse>>> List(
        [FromQuery] JobListQuery query, CancellationToken cancellationToken)
    {
        var jobs = db.Jobs.AsQueryable();
        if (query.Status.Count > 0)
        {
            var statuses = query.Status.Distinct().ToList();
            jobs = jobs.Where(j => statuses.Contains(j.Status));
        }

        var total = await jobs.CountAsync(cancellationToken);

        // The id breaks ties, so a job never shows on two pages or on none.
        var items = await Summaries(db, jobs
                .OrderByDescending(j => j.CreatedAt)
                .ThenByDescending(j => j.Id)
                .Skip(query.Offset)
                .Take(query.Limit))
            .ToListAsync(cancellationToken);

        return new Page<JobSummaryResponse>(items, total, query.Offset, query.Limit);
    }

    /// <summary>One job, with its result, the steps of every run and the jobs enqueued under it.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<JobDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JobDetailResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id, cancellationToken);
        if (job is null)
        {
            return NotFound();
        }

        var steps = await db.JobSteps
            .Where(s => s.JobId == id)
            .OrderBy(s => s.Attempt)
            .ThenBy(s => s.Order)
            .ThenBy(s => s.StartedAt)
            .ThenBy(s => s.Id)
            .Select(s => new JobStepResponse(s.Name, s.Order, s.Attempt, s.Status, s.Error, s.StartedAt, s.FinishedAt))
            .ToListAsync(cancellationToken);

        var children = await Summaries(db, db.Jobs
                .Where(j => j.ParentJobId == id)
                .OrderBy(j => j.CreatedAt)
                .ThenBy(j => j.Id))
            .ToListAsync(cancellationToken);

        // Read from the log alone, so a job deleted since it was loaded above still answers.
        var submittedBy = await db.AuditEntries
            .SubmissionsOf(id)
            .Select(e => e.ActorName)
            .FirstOrDefaultAsync(cancellationToken);

        // The result is passed through as the JSON its type wrote. The payload stays inside Nook.
        return new JobDetailResponse(
            job.Id,
            job.Type,
            job.Status,
            job.Error,
            job.ParentJobId,
            job.NotBefore,
            job.AttemptsMade,
            job.AttemptsAllowed,
            job.ReclaimsMade,
            job.CreatedAt,
            job.UpdatedAt,
            job.Result is null ? null : JsonSerializer.Deserialize<JsonElement>(job.Result),
            steps,
            children,
            submittedBy);
    }

    /// <summary>
    /// Sends a failed job back to pending, to run again from its first step with a fresh set of attempts. A job that
    /// is not failed is refused.
    /// </summary>
    [HttpPost("{id:guid}/retry")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Retry(Guid id, CancellationToken cancellationToken) =>
        (await actions.RetryAsync(User, id, cancellationToken)).ToResult(this);

    /// <summary>
    /// Deletes a job that is not running, with the steps of all its runs, and then lets its type clean up whatever the
    /// job worked on. A running job, or one with jobs enqueued under it, is refused.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        (await actions.DeleteAsync(User, id, cancellationToken)).ToResult(this);
}
