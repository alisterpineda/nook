using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nook.Api.Data;

namespace Nook.Api.Jobs;

/// <summary>
/// Runs jobs inside the API process. Each of its loops claims the oldest job that is ready, runs its type's steps in
/// order and records how each went, then looks for the next; with nothing to claim it waits for a wake from
/// <see cref="JobQueue"/> or its next poll.
/// </summary>
/// <remarks>
/// <para>
/// The worker keeps its own writes apart from the job's. Its bookkeeping (the claim, the step rows, the job's
/// status) uses a short scope for each write, so its saves never carry what a job has changed. A run gets one
/// scope of its own for its whole life, which its job type takes services from, and the worker never saves on it.
/// </para>
/// <para>
/// A claimed job holds a lease, which the worker renews while the run goes on. A running job whose lease has run
/// out belonged to a worker that died, and is claimed again, up to <see cref="ReclaimsAllowed"/> times before it
/// fails. A worker that finds its lease taken leaves the job to whoever took it: it starts no further step and does
/// not end the job.
/// </para>
/// <para>
/// When the application stops cleanly, a worker hands the job it holds back to pending for the next start to run
/// from the top, counting it neither as an attempt nor as a reclaim.
/// </para>
/// </remarks>
public sealed partial class JobWorker(
    IServiceScopeFactory scopeFactory,
    JobTypeRegistry types,
    JobSignal signal,
    IJobChangeNotifier notifier,
    TimeProvider timeProvider,
    IOptions<JobsOptions> options,
    ILogger<JobWorker> logger) : BackgroundService
{
    /// <summary>How many times a job is taken back from a worker that died before the job fails instead.</summary>
    private const int ReclaimsAllowed = 3;

    private const string WorkerStoppedError = "The worker stopped before this step finished.";

    private const string WorkerKeptDyingError = "The worker running this job kept dying.";

    private const string ApplicationShutDownError = "The application shut down.";

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A count of zero starts no loop, so jobs stay pending.
        var loops = Enumerable.Range(0, options.Value.WorkerCount)
            .Select(_ => Task.Run(() => RunLoopAsync(stoppingToken), CancellationToken.None));
        return Task.WhenAll(loops);
    }

    private TimeSpan Lease => TimeSpan.FromMinutes(options.Value.LeaseMinutes);

    private TimeSpan Poll => TimeSpan.FromSeconds(options.Value.PollSeconds);

    private DateTimeOffset UtcNow() => timeProvider.GetUtcNow();

    /// <summary>
    /// When a lease taken or renewed now runs out. Cut to the microsecond the database keeps, so the worker can tell
    /// the lease it wrote from one another worker wrote by comparing the two.
    /// </summary>
    private DateTimeOffset NewLeaseExpiry()
    {
        var expiry = UtcNow() + Lease;
        return expiry.AddTicks(-(expiry.Ticks % TimeSpan.TicksPerMicrosecond));
    }

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Taken before looking, so a job enqueued while this loop looks still wakes it.
            var wake = signal.NextWakeAsync();
            bool ranJob;
            try
            {
                ranJob = await RunNextJobAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // The loop outlives a failure, such as the database being away for a moment: a background service
                // that throws stops the whole host.
                LogLoopFailed(logger, exception);
                ranJob = false;
            }

            if (!ranJob)
            {
                await Task.WhenAny(wake, Task.Delay(Poll, stoppingToken));
            }
        }
    }

    /// <summary>Claims a job and runs it. False when there was none to claim.</summary>
    private async Task<bool> RunNextJobAsync(CancellationToken stoppingToken)
    {
        var (job, found) = await ClaimAsync(stoppingToken);
        if (job is not null)
        {
            await RunAsync(job, stoppingToken);
        }

        // A job that was failed rather than run still counts, so the loop looks again at once.
        return found;
    }

    private sealed record ClaimedJob(Guid Id, string Type, string Payload, DateTimeOffset LeaseExpiresAt);

    /// <summary>
    /// Takes the oldest job that is ready: a pending one whose "not before" time has come, or a running one whose
    /// worker's lease has run out. Returns it running under a new lease, or no job when there was none
    /// (<c>Found</c> false) or when the one found had been reclaimed too often and was failed instead.
    /// </summary>
    private async Task<(ClaimedJob? Job, bool Found)> ClaimAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();

        // The job given up on, for its type's failure hook once the claim has committed.
        ClaimedJob? givenUp = null;

        // The database is set to retry, so the transaction has to run inside its retry strategy.
        var result = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // A retry starts from what the database holds, not from what a failed attempt loaded.
            db.ChangeTracker.Clear();
            givenUp = null;
            var now = UtcNow();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            // SKIP LOCKED passes over a job another worker holds, so two workers never claim the same one.
            // Nothing is composed on the query: EF would wrap the SQL in a subquery.
            var found = await db.Jobs
                .FromSql($"""
                    SELECT * FROM jobs
                    WHERE (status = 'Pending' AND (not_before IS NULL OR not_before <= {now}))
                       OR (status = 'Running' AND lease_expires_at < {now})
                    ORDER BY created_at, id
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(cancellationToken);
            if (found is not [var job])
            {
                return ((ClaimedJob?)null, false);
            }

            // Steps still running belong to a run whose worker is gone, and will not finish.
            await FailRunningStepsAsync(db, job.Id, WorkerStoppedError, now, cancellationToken);

            ClaimedJob? claimed = null;
            if (job.Status == JobStatus.Running && job.ReclaimsMade >= ReclaimsAllowed)
            {
                // Its runs keep taking their worker down with them. Attempts count runs that ended, so they stay as they are.
                LogJobGivenUp(logger, job.Id, job.Type, job.ReclaimsMade);
                job.Status = JobStatus.Failed;
                job.Error = WorkerKeptDyingError;
                job.LeaseExpiresAt = null;
                givenUp = new ClaimedJob(job.Id, job.Type, job.Payload, now);
            }
            else
            {
                if (job.Status == JobStatus.Running)
                {
                    LogJobReclaimed(logger, job.Id, job.Type);
                    job.ReclaimsMade++;
                }

                job.Status = JobStatus.Running;
                job.LeaseExpiresAt = NewLeaseExpiry();
                claimed = new ClaimedJob(job.Id, job.Type, job.Payload, job.LeaseExpiresAt.Value);
            }

            await db.SaveChangesAsync(cancellationToken);

            // Not cancelled: a claim the database made but the worker did not see would leave the job running with
            // no one to run it or hand it back.
            await transaction.CommitAsync(CancellationToken.None);
            return (claimed, true);
        });

        if (result.Item1 is { } claimedJob)
        {
            await notifier.ChangedAsync(claimedJob.Id);
        }

        if (givenUp is not null)
        {
            await RunFailureHookAsync(givenUp, error: null);
            await notifier.ChangedAsync(givenUp.Id);
        }

        return result;
    }

    private async Task RunAsync(ClaimedJob claimed, CancellationToken stoppingToken)
    {
        await using var lease = new HeldLease(this, claimed.Id, claimed.LeaseExpiresAt, stoppingToken);
        try
        {
            await RunStepsAsync(claimed, lease, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The application is stopping, whether in a step or in the worker's own writes around one.
            await HandBackAsync(lease);
            throw;
        }
    }

    private async Task RunStepsAsync(ClaimedJob claimed, HeldLease lease, CancellationToken stoppingToken)
    {
        var type = types.Find(claimed.Type);
        if (type is null)
        {
            // No later run could find the type either, so the job fails whatever attempts it has left.
            if (await RecordAsync(lease, step: null, job => EndInFailure(job, $"Unknown job type '{claimed.Type}'.", mayRunAgain: false)))
            {
                await notifier.ChangedAsync(claimed.Id);
            }

            return;
        }

        var attempt = await NextAttemptAsync(claimed.Id, stoppingToken);

        await using var runScope = scopeFactory.CreateAsyncScope();
        JobRun run;
        try
        {
            run = type.CreateRun(claimed.Payload, runScope.ServiceProvider);
        }
        catch (Exception exception)
        {
            LogRunFailed(logger, exception, claimed.Id, claimed.Type, null);
            await FailAsync(type, claimed, lease, step: null, exception);
            return;
        }

        for (var order = 0; order < run.Steps.Count; order++)
        {
            var step = run.Steps[order];
            var stepId = await StartStepAsync(claimed.Id, step.Name, order, attempt, stoppingToken);
            try
            {
                await step.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // The application is stopping, which is no failure of the step: the job is handed back instead.
                throw;
            }
            catch (Exception exception)
            {
                LogRunFailed(logger, exception, claimed.Id, claimed.Type, step.Name);
                await FailAsync(type, claimed, lease, (stepId, exception.Message), exception);
                return;
            }

            if (run.IsCompleted || order == run.Steps.Count - 1)
            {
                await SucceedAsync(type, claimed, lease, stepId, run.ResultJson);
                return;
            }

            await RecordAsync(lease, (stepId, null), endRun: null);
            if (lease.IsLost)
            {
                // Another worker has taken the job and runs it from the top, so this run goes no further.
                return;
            }
        }

        // A run with no steps has nothing left to do.
        await SucceedAsync(type, claimed, lease, stepId: null, run.ResultJson);
    }

    /// <summary>
    /// Records a run that failed, finishing its step with the error, and when that leaves the job failed for good,
    /// which this worker's write must have done, runs the type's failure hook in a scope of its own. An exception from
    /// the hook is logged and changes nothing on the job, which stays failed.
    /// </summary>
    private async Task FailAsync(
        JobType type, ClaimedJob claimed, HeldLease lease, (Guid Id, string? Error)? step, Exception exception)
    {
        var stays = false;
        var ended = await RecordAsync(lease, step, job =>
        {
            EndInFailure(job, exception.Message);
            stays = job.Status == JobStatus.Failed;
        });
        if (ended && stays)
        {
            await RunFailureHookAsync(claimed, exception, type);
        }

        if (ended)
        {
            await notifier.ChangedAsync(claimed.Id);
        }
    }

    private async Task RunFailureHookAsync(ClaimedJob claimed, Exception? error, JobType? type = null)
    {
        type ??= types.Find(claimed.Type);
        if (type is null)
        {
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await type.OnFailedAsync(claimed.Payload, error, scope.ServiceProvider);
        }
        catch (Exception exception)
        {
            LogFailureHookFailed(logger, exception, claimed.Id, claimed.Type);
        }
    }

    /// <summary>
    /// Records the job as succeeded, finishing its last step with it, and once that has committed runs the type's
    /// success hook in a scope of its own. The hook runs only when this worker's write is the one that ended the job.
    /// An exception from it is logged and changes nothing on the job, which stays succeeded.
    /// </summary>
    private async Task SucceedAsync(JobType type, ClaimedJob claimed, HeldLease lease, Guid? stepId, string? resultJson)
    {
        var ended = await RecordAsync(
            lease, stepId is { } id ? (id, null) : null, job => EndInSuccess(job, resultJson));
        if (!ended)
        {
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await type.OnSucceededAsync(claimed.Payload, scope.ServiceProvider);
        }
        catch (Exception exception)
        {
            LogSuccessHookFailed(logger, exception, claimed.Id, claimed.Type);
        }

        await notifier.ChangedAsync(claimed.Id);
    }

    /// <summary>The number for a new run's step rows: one more than the highest any earlier run used.</summary>
    private async Task<int> NextAttemptAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        var highest = await db.JobSteps.Where(s => s.JobId == jobId).MaxAsync(s => (int?)s.Attempt, cancellationToken);
        return (highest ?? 0) + 1;
    }

    private async Task<Guid> StartStepAsync(Guid jobId, string name, int order, int attempt, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        var step = new JobStep
        {
            JobId = jobId,
            Name = name,
            Order = order,
            Attempt = attempt,
            Status = JobStepStatus.Running,
            StartedAt = UtcNow(),
        };
        db.JobSteps.Add(step);
        await db.SaveChangesAsync(cancellationToken);
        return step.Id;
    }

    /// <summary>
    /// Finishes a step, failed when it has an error, and when <paramref name="endRun"/> is given ends the job's run
    /// with it, both in one save. The save is not cancelled: what a finished step did is always recorded. The job's
    /// run is ended only while the worker still holds its lease; once another worker has taken the job, the run it
    /// started is the one that ends it.
    /// </summary>
    /// <returns>True when <paramref name="endRun"/> was given and the job's run was ended with it.</returns>
    private async Task<bool> RecordAsync(HeldLease lease, (Guid Id, string? Error)? step, Action<Job>? endRun)
    {
        if (endRun is not null)
        {
            // Stopped first, so the lease compared below is the last one written.
            await lease.StopRenewingAsync();
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();

        // The job's updated time as a pass wrote it with the run's end, since that pass's commit can land and still
        // report an error. No other write gives the job this time, so it tells this run's ending from another's.
        DateTimeOffset? endWrittenAt = null;
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();

            // The earlier pass's commit landed when the job still carries the time that pass gave it: its step is
            // finished too, and this worker's write is the one that ended the run. Any other job, still running or
            // since taken and ended by another worker, is handled below as on a first pass.
            if (endWrittenAt is { } writtenAt && await db.Jobs.AnyAsync(
                    j => j.Id == lease.JobId && j.Status != JobStatus.Running && j.LeaseExpiresAt == null
                        && j.UpdatedAt == writtenAt,
                    CancellationToken.None))
            {
                return true;
            }

            await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);

            if (step is var (stepId, error))
            {
                var row = await db.JobSteps.SingleAsync(s => s.Id == stepId, CancellationToken.None);
                row.Status = error is null ? JobStepStatus.Succeeded : JobStepStatus.Failed;
                row.Error = error;
                row.FinishedAt = UtcNow();
            }

            Job? ended = null;
            if (endRun is not null && await LockHeldJobAsync(db, lease) is { } job)
            {
                endRun(job);
                job.LeaseExpiresAt = null;
                ended = job;
            }

            await db.SaveChangesAsync(CancellationToken.None);
            endWrittenAt = ended?.UpdatedAt ?? endWrittenAt;
            await transaction.CommitAsync(CancellationToken.None);
            return ended is not null;
        });
    }

    /// <summary>
    /// Returns a job whose run the application's stopping cut short to pending with no lease, so the next start runs
    /// it again from the top, and fails its steps still running. Attempts and reclaims stay as they are: the run
    /// neither ended nor lost its worker. Written with no cancellation, since the stopping token already is
    /// cancelled, and only while the worker still holds its lease; a job another worker has taken is left to it.
    /// </summary>
    private async Task HandBackAsync(HeldLease lease)
    {
        // Stopped first, so the lease compared below is the last one written.
        await lease.StopRenewingAsync();

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();

        var handedBack = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            var now = UtcNow();
            await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);

            var job = await LockHeldJobAsync(db, lease);
            if (job is not null)
            {
                job.Status = JobStatus.Pending;
                job.LeaseExpiresAt = null;

                // While the lease is held, every step still running is this run's, including one whose start was
                // saved just as the stop came.
                await FailRunningStepsAsync(db, job.Id, ApplicationShutDownError, now, CancellationToken.None);
            }

            await db.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
            return job is not null;
        });

        if (handedBack)
        {
            LogJobHandedBack(logger, lease.JobId);
            await notifier.ChangedAsync(lease.JobId);
        }
    }

    /// <summary>Marks the job's steps that are still running as failed with this error, for the caller to save.</summary>
    private static async Task FailRunningStepsAsync(
        NookDbContext db, Guid jobId, string error, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var running = await db.JobSteps
            .Where(s => s.JobId == jobId && s.Status == JobStepStatus.Running)
            .ToListAsync(cancellationToken);
        foreach (var step in running)
        {
            step.Status = JobStepStatus.Failed;
            step.Error = error;
            step.FinishedAt = now;
        }
    }

    /// <summary>
    /// Locks the job's row in the caller's transaction, and returns the job while it is still running under this
    /// worker's lease, or null once another worker has taken it. Locked, so a worker reclaiming the job either waits
    /// for the caller to commit, or has already written its own lease, which this one then does not match.
    /// </summary>
    private async Task<Job?> LockHeldJobAsync(NookDbContext db, HeldLease lease)
    {
        var found = await db.Jobs
            .FromSql($"SELECT * FROM jobs WHERE id = {lease.JobId} FOR UPDATE")
            .ToListAsync(CancellationToken.None);
        var job = found.Single();
        if (job.Status == JobStatus.Running && lease.Holds(job.LeaseExpiresAt))
        {
            return job;
        }

        LogLeaseLost(logger, job.Id, job.Type);
        return null;
    }

    /// <summary>
    /// Moves the lease on every third of its length until the run ends, so a renewal or two can fail, as when the
    /// database is away for a moment, before the lease runs out. Stops for good once the job's lease is no longer
    /// the one this worker wrote, which means another worker has taken the job.
    /// </summary>
    private async Task RenewLeaseAsync(HeldLease lease, CancellationToken stopToken)
    {
        var every = Lease / 3;
        while (true)
        {
            try
            {
                await Task.Delay(every, timeProvider, stopToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                var expiresAt = NewLeaseExpiry();
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();

                // The database can make a renewal whose answer never arrives. So the row may hold any expiry this
                // worker has tried since the last answer, and the one tried now is remembered before it is sent.
                var possible = lease.PossibleExpiries();
                lease.Trying(expiresAt);

                // Not cancelled, so that happens as seldom as it can.
                var renewed = await db.Jobs
                    .Where(j => j.Id == lease.JobId
                        && j.Status == JobStatus.Running
                        && j.LeaseExpiresAt != null
                        && possible.Contains(j.LeaseExpiresAt.Value))
                    .ExecuteUpdateAsync(s => s.SetProperty(j => j.LeaseExpiresAt, expiresAt), CancellationToken.None);
                if (renewed == 0)
                {
                    // A statement the database ran twice, after a dropped connection, changes no row the second
                    // time: the lease is then already the one being written, and is still this worker's.
                    var current = await db.Jobs
                        .Where(j => j.Id == lease.JobId && j.Status == JobStatus.Running)
                        .Select(j => j.LeaseExpiresAt)
                        .SingleOrDefaultAsync(CancellationToken.None);
                    if (current != expiresAt)
                    {
                        lease.IsLost = true;
                        LogLeaseLost(logger, lease.JobId, null);
                        return;
                    }
                }

                lease.Renewed(expiresAt);
            }
            catch (Exception exception)
            {
                LogLeaseRenewalFailed(logger, exception, lease.JobId);
            }
        }
    }

    /// <summary>A claimed job's lease, renewed in the background from when the run starts until it ends.</summary>
    private sealed class HeldLease : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop;
        private readonly Task _renewing;
        private volatile bool _isLost;

        // The expiries the job's row may hold while the lease is this worker's: the last one known to be written,
        // then any tried since whose answer never came. Only the renewal changes it; others read it once renewal
        // has stopped.
        private readonly List<DateTimeOffset> _written;

        public HeldLease(JobWorker worker, Guid jobId, DateTimeOffset expiresAt, CancellationToken stoppingToken)
        {
            JobId = jobId;
            _written = [expiresAt];
            _stop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            _renewing = Task.Run(() => worker.RenewLeaseAsync(this, _stop.Token), CancellationToken.None);
        }

        public Guid JobId { get; }

        /// <summary>Every expiry the job's row may hold while the lease is still this worker's.</summary>
        public DateTimeOffset[] PossibleExpiries() => [.. _written];

        /// <summary>Notes an expiry about to be written, in case its answer never comes.</summary>
        public void Trying(DateTimeOffset expiresAt) => _written.Add(expiresAt);

        /// <summary>Notes that the database holds this expiry, so the earlier ones no longer count.</summary>
        public void Renewed(DateTimeOffset expiresAt)
        {
            _written.Clear();
            _written.Add(expiresAt);
        }

        /// <summary>Whether a lease read from the job's row is one this worker wrote. Call it once renewal has stopped.</summary>
        public bool Holds(DateTimeOffset? expiresAt) => expiresAt is { } value && _written.Contains(value);

        /// <summary>Whether a renewal found the job's lease no longer the one this worker wrote.</summary>
        public bool IsLost
        {
            get => _isLost;
            set => _isLost = value;
        }

        /// <summary>Stops renewing, and waits for a renewal under way to finish.</summary>
        public async Task StopRenewingAsync()
        {
            await _stop.CancelAsync();
            await _renewing;
        }

        public async ValueTask DisposeAsync()
        {
            await StopRenewingAsync();
            _stop.Dispose();
        }
    }

    private static void EndInSuccess(Job job, string? resultJson)
    {
        job.AttemptsMade++;
        job.Status = JobStatus.Succeeded;
        job.Result = resultJson;
        job.Error = null;
    }

    /// <summary>Records a failed run. The job goes back to pending while it has attempts left, else it stays failed.</summary>
    private static void EndInFailure(Job job, string error, bool mayRunAgain = true)
    {
        job.AttemptsMade++;
        job.Error = error;
        job.Status = mayRunAgain && job.AttemptsMade < job.AttemptsAllowed ? JobStatus.Pending : JobStatus.Failed;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The job worker failed while claiming or recording a job.")]
    private static partial void LogLoopFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job {JobId} of type {JobType} failed at step {StepName}.")]
    private static partial void LogRunFailed(ILogger logger, Exception exception, Guid jobId, string jobType, string? stepName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job {JobId} of type {JobType} succeeded, but its success hook failed.")]
    private static partial void LogSuccessHookFailed(ILogger logger, Exception exception, Guid jobId, string jobType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job {JobId} of type {JobType} failed, and its failure hook failed too.")]
    private static partial void LogFailureHookFailed(ILogger logger, Exception exception, Guid jobId, string jobType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job {JobId} of type {JobType} outlived its worker's lease and is run again.")]
    private static partial void LogJobReclaimed(ILogger logger, Guid jobId, string jobType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job {JobId} of type {JobType} outlived its worker's lease after {ReclaimsMade} reclaims and has failed.")]
    private static partial void LogJobGivenUp(ILogger logger, Guid jobId, string jobType, int reclaimsMade);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The worker lost its lease on job {JobId} of type {JobType} to another worker, and leaves the job to it.")]
    private static partial void LogLeaseLost(ILogger logger, Guid jobId, string? jobType);

    [LoggerMessage(Level = LogLevel.Information, Message = "The application is stopping, so job {JobId} goes back to pending.")]
    private static partial void LogJobHandedBack(ILogger logger, Guid jobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The worker could not renew its lease on job {JobId}, and tries again.")]
    private static partial void LogLeaseRenewalFailed(ILogger logger, Exception exception, Guid jobId);
}
