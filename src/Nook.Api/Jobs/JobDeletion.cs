using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;

namespace Nook.Api.Jobs;

/// <summary>How a request to delete a job went.</summary>
public enum JobDeleteOutcome
{
    /// <summary>The job is gone and its type has cleaned up after it.</summary>
    Deleted,

    /// <summary>There is no job with that id.</summary>
    NotFound,

    /// <summary>The job is running, so it cannot be deleted.</summary>
    Running,

    /// <summary>Jobs are enqueued under the job, so it cannot be deleted.</summary>
    HasChildren,
}

/// <summary>Deletes a job that is not running, then lets its type clean up whatever the job worked on.</summary>
public sealed class JobDeletion(
    NookDbContext db, JobTypeRegistry types, IServiceProvider services, IJobChangeNotifier notifier)
{
    /// <summary>
    /// Deletes a job that is not running, with the steps of all its runs, and then lets its type clean up. A running
    /// job, or one with jobs enqueued under it, is refused. <paramref name="onDeleting"/> runs inside the delete's
    /// transaction with the job about to go, so the caller can add an audit entry that commits or fails with it.
    /// </summary>
    public async Task<JobDeleteOutcome> DeleteAsync(
        Guid id, Func<Job, CancellationToken, Task> onDeleting, CancellationToken cancellationToken)
    {
        // The database is set to retry, so the transaction has to run inside its retry strategy.
        var (outcome, deleted) = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
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
                return (JobDeleteOutcome.NotFound, (Job?)null);
            }

            if (job.Status == JobStatus.Running)
            {
                return (JobDeleteOutcome.Running, null);
            }

            // A child enqueued meanwhile waits on the lock, and then finds its parent gone.
            if (await db.Jobs.AnyAsync(j => j.ParentJobId == id, cancellationToken))
            {
                return (JobDeleteOutcome.HasChildren, null);
            }

            await onDeleting(job, cancellationToken);

            // The steps go with the job, by the database's cascade.
            db.Jobs.Remove(job);
            await db.SaveChangesAsync(cancellationToken);

            // Not cancelled: a delete the database made must be followed by its cleanup.
            await transaction.CommitAsync(CancellationToken.None);
            return (JobDeleteOutcome.Deleted, job);
        });

        if (outcome != JobDeleteOutcome.Deleted)
        {
            return outcome;
        }

        await notifier.RemovedAsync(id);

        // Only once the job is gone for good, so a cleanup never takes away what a job still to run needs. A type that
        // is no longer registered has no cleanup to run.
        if (types.Find(deleted!.Type) is { } type)
        {
            await type.OnDeletedAsync(deleted.Payload, services);
        }

        return outcome;
    }
}
