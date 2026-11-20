using System.Linq.Expressions;
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
public class JobsController(NookDbContext db, JobQueue queue, JobTypeRegistry types, AuditLog audit) : ControllerBase
{
    private static readonly Expression<Func<Job, JobSummaryResponse>> ToSummary = j => new JobSummaryResponse(
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
        j.UpdatedAt);

    /// <summary>One page of all jobs, newest first.</summary>
    [HttpGet]
    [ProducesResponseType<Page<JobSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<Page<JobSummaryResponse>>> List(
        [FromQuery] JobListQuery query, CancellationToken cancellationToken)
    {
        var total = await db.Jobs.CountAsync(cancellationToken);

        // The id breaks ties, so a job never shows on two pages or on none.
        var items = await db.Jobs
            .OrderByDescending(j => j.CreatedAt)
            .ThenByDescending(j => j.Id)
            .Skip(query.Offset)
            .Take(query.Limit)
            .Select(ToSummary)
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

        var children = await db.Jobs
            .Where(j => j.ParentJobId == id)
            .OrderBy(j => j.CreatedAt)
            .ThenBy(j => j.Id)
            .Select(ToSummary)
            .ToListAsync(cancellationToken);

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
            children);
    }

    /// <summary>The upload a job belongs to, or null when its type is gone or it belongs to none.</summary>
    private Guid? SubmissionIdOf(string type, string payload) => types.Find(type)?.SubmissionIdOf(payload);

    /// <summary>
    /// Sends a failed job back to pending, to run again from its first step with a fresh set of attempts. A job that
    /// is not failed is refused.
    /// </summary>
    [HttpPost("{id:guid}/retry")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Retry(Guid id, CancellationToken cancellationToken)
    {
        var found = await db.Jobs.AsNoTracking()
            .Where(j => j.Id == id)
            .Select(j => new { j.Type, j.Payload })
            .SingleOrDefaultAsync(cancellationToken);
        var outcome = await queue.RetryAsync(id, cancellationToken);
        if (outcome == JobRetryOutcome.Retried && found is not null)
        {
            await audit.AddAsync(
                User,
                AuditActions.JobRetried,
                new AuditSubject(AuditSubjectTypes.Job, id, found.Type),
                new { },
                SubmissionIdOf(found.Type, found.Payload));

            // The retry has landed, so its entry is written whether or not the caller is still there.
            await db.SaveChangesAsync(CancellationToken.None);
        }

        return outcome switch
        {
            JobRetryOutcome.Retried => NoContent(),
            JobRetryOutcome.NotFound => NotFound(),
            _ => Problem(statusCode: StatusCodes.Status409Conflict, title: "Only a failed job can be retried."),
        };
    }

    /// <summary>
    /// Deletes a job that is not running, with the steps of all its runs, and then lets its type clean up whatever the
    /// job worked on. A running job, or one with jobs enqueued under it, is refused.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        // The database is set to retry, so the transaction has to run inside its retry strategy.
        var (refusal, deleted) = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // A retry starts from what the database holds, not from what a failed attempt loaded.
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            // Locked until the delete commits, so a worker's claim passes over the job rather than starting it, and a
            // job a worker claimed first is seen running. Nothing is composed on the query: EF would wrap the SQL in a
            // subquery.
            var found = await db.Jobs
                .FromSql($"SELECT * FROM jobs WHERE id = {id} FOR UPDATE")
                .ToListAsync(cancellationToken);
            if (found is not [var job])
            {
                return ((IActionResult?)NotFound(), (Job?)null);
            }

            if (job.Status == JobStatus.Running)
            {
                return (Problem(statusCode: StatusCodes.Status409Conflict, title: "A running job cannot be deleted."), null);
            }

            // A child enqueued meanwhile waits on the lock, and then finds its parent gone.
            if (await db.Jobs.AnyAsync(j => j.ParentJobId == id, cancellationToken))
            {
                return (
                    Problem(statusCode: StatusCodes.Status409Conflict, title: "A job with jobs under it cannot be deleted."),
                    null);
            }

            await audit.AddAsync(
                User,
                AuditActions.JobDeleted,
                new AuditSubject(AuditSubjectTypes.Job, job.Id, job.Type),
                new { },
                SubmissionIdOf(job.Type, job.Payload),
                cancellationToken: cancellationToken);

            // The steps go with the job, by the database's cascade.
            db.Jobs.Remove(job);
            await db.SaveChangesAsync(cancellationToken);

            // Not cancelled: a delete the database made must be followed by its cleanup.
            await transaction.CommitAsync(CancellationToken.None);
            return (null, job);
        });

        if (refusal is not null)
        {
            return refusal;
        }

        // Only once the job is gone for good, so a cleanup never takes away what a job still to run needs. A type that
        // is no longer registered has no cleanup to run.
        if (types.Find(deleted!.Type) is { } type)
        {
            await type.OnDeletedAsync(deleted.Payload, HttpContext.RequestServices);
        }

        return NoContent();
    }
}
