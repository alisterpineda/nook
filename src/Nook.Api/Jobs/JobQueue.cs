using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;

namespace Nook.Api.Jobs;

/// <summary>How a request to retry a job went.</summary>
public enum JobRetryOutcome
{
    /// <summary>The job is pending again.</summary>
    Retried,

    /// <summary>There is no job with that id.</summary>
    NotFound,

    /// <summary>The job is not failed, so there is nothing to retry.</summary>
    NotFailed,
}

/// <summary>Adds jobs for the worker to run, and hands failed ones back to it.</summary>
public sealed class JobQueue(
    NookDbContext db, JobTypeRegistry types, JobSignal signal, TimeProvider timeProvider, IJobChangeNotifier notifier)
{
    /// <summary>
    /// Stores a pending job and returns its id. A type that is not registered is still stored, with one
    /// attempt, so the job shows up and fails where it can be seen rather than vanishing.
    /// </summary>
    /// <param name="type">The name of the job type that runs it.</param>
    /// <param name="payload">What the job works on, stored as JSON for its type to read.</param>
    /// <param name="parentJobId">The job this one belongs under, if any.</param>
    /// <param name="notBefore">The job is not run before this time.</param>
    /// <param name="id">The job's id, when the caller needs to name the job before it is saved; otherwise a new one.</param>
    public async Task<Guid> EnqueueAsync(
        string type,
        object payload,
        Guid? parentJobId = null,
        DateTimeOffset? notBefore = null,
        CancellationToken cancellationToken = default,
        Guid? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(payload);

        var job = new Job
        {
            Type = type,
            Status = JobStatus.Pending,
            Payload = JobJson.Serialize(payload),
            ParentJobId = parentJobId,
            NotBefore = notBefore,
            AttemptsAllowed = types.AttemptsFor(type),
        };
        if (id is { } given)
        {
            job.Id = given;
        }

        // The context is the scope's, so this save also writes anything else the scope has changed.
        db.Jobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);

        // Told while it is still pending: the notifier reads the job as it stands, and a worker woken first could
        // have claimed it by then, so no one would hear it was queued.
        await notifier.ChangedAsync(job.Id);

        // Workers in this process start on it now; others find it at their next poll.
        signal.Wake();
        return job.Id;
    }

    /// <summary>
    /// Sends a failed job back to pending to run again from its first step, with its error, "not before" time and
    /// reclaims cleared and as many runs ahead of it as its type allows. Attempts made stay as a running count of the
    /// runs that ended. The new run's step rows are numbered apart from that, by the worker: one past the highest
    /// number an earlier run used.
    /// </summary>
    public async Task<JobRetryOutcome> RetryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var type = await db.Jobs.Where(j => j.Id == id).Select(j => j.Type).SingleOrDefaultAsync(cancellationToken);
        if (type is null)
        {
            return JobRetryOutcome.NotFound;
        }

        var attempts = types.AttemptsFor(type);
        var now = timeProvider.GetUtcNow();

        // One guarded statement: only a job still failed is changed. The worker never claims or writes a failed job,
        // and two retries at once change it only once.
        var retried = await db.Jobs
            .Where(j => j.Id == id && j.Status == JobStatus.Failed)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(j => j.Status, JobStatus.Pending)
                    .SetProperty(j => j.Error, (string?)null)
                    .SetProperty(j => j.NotBefore, (DateTimeOffset?)null)
                    .SetProperty(j => j.LeaseExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(j => j.ReclaimsMade, 0)
                    .SetProperty(j => j.AttemptsAllowed, j => j.AttemptsMade + attempts)
                    .SetProperty(j => j.UpdatedAt, now),
                cancellationToken);
        if (retried == 0)
        {
            // Not failed, or deleted since it was read.
            return await db.Jobs.AnyAsync(j => j.Id == id, cancellationToken)
                ? JobRetryOutcome.NotFailed
                : JobRetryOutcome.NotFound;
        }

        // Told before the worker is woken, as for a new job, so the notice says pending.
        await notifier.ChangedAsync(id);
        signal.Wake();
        return JobRetryOutcome.Retried;
    }
}
