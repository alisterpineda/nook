using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;
using Nook.Api.Features.Audit;
using Nook.Api.Jobs;

namespace Nook.Api.Features.Jobs;

/// <summary>
/// Retries and deletes a job on a person's behalf, writing who did it to the audit log. Whether that person may act
/// on the job is the caller's to decide first.
/// </summary>
public sealed class JobActions(
    NookDbContext db, JobQueue queue, JobDeletion deletion, JobTypeRegistry types, AuditLog audit)
{
    /// <summary>
    /// Sends a failed job back to pending, to run again from its first step with a fresh set of attempts, and logs
    /// the retry. A job that is not failed is refused.
    /// </summary>
    public async Task<JobRetryOutcome> RetryAsync(ClaimsPrincipal actor, Guid id, CancellationToken cancellationToken)
    {
        var found = await db.Jobs.AsNoTracking()
            .Where(j => j.Id == id)
            .Select(j => new { j.Type, j.Payload })
            .SingleOrDefaultAsync(cancellationToken);
        var outcome = await queue.RetryAsync(id, cancellationToken);
        if (outcome == JobRetryOutcome.Retried && found is not null)
        {
            await audit.AddAsync(
                actor,
                AuditActions.JobRetried,
                new AuditSubject(AuditSubjectTypes.Job, id, found.Type),
                new { },
                types.SubmissionIdOf(found.Type, found.Payload));

            // The retry has landed, so its entry is written whether or not the caller is still there.
            await db.SaveChangesAsync(CancellationToken.None);
        }

        return outcome;
    }

    /// <summary>
    /// Deletes a job that is not running, with the steps of all its runs, lets its type clean up whatever the job
    /// worked on, and logs the delete with it. A running job, or one with jobs enqueued under it, is refused.
    /// </summary>
    public Task<JobDeleteOutcome> DeleteAsync(ClaimsPrincipal actor, Guid id, CancellationToken cancellationToken) =>
        deletion.DeleteAsync(
            id,
            (job, ct) => audit.AddAsync(
                actor,
                AuditActions.JobDeleted,
                new AuditSubject(AuditSubjectTypes.Job, job.Id, job.Type),
                new { },
                types.SubmissionIdOf(job.Type, job.Payload),
                cancellationToken: ct),
            cancellationToken);
}

/// <summary>The answers a controller gives for how a retry or a delete went.</summary>
public static class JobOutcomeResults
{
    public static IActionResult ToResult(this JobRetryOutcome outcome, ControllerBase controller) => outcome switch
    {
        JobRetryOutcome.Retried => controller.NoContent(),
        JobRetryOutcome.NotFound => controller.NotFound(),
        _ => controller.Problem(statusCode: StatusCodes.Status409Conflict, title: "Only a failed job can be retried."),
    };

    public static IActionResult ToResult(this JobDeleteOutcome outcome, ControllerBase controller) => outcome switch
    {
        JobDeleteOutcome.Deleted => controller.NoContent(),
        JobDeleteOutcome.NotFound => controller.NotFound(),
        JobDeleteOutcome.Running =>
            controller.Problem(statusCode: StatusCodes.Status409Conflict, title: "A running job cannot be deleted."),
        _ => controller.Problem(
            statusCode: StatusCodes.Status409Conflict, title: "A job with jobs under it cannot be deleted."),
    };
}
