using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Jobs;

namespace Nook.Api.Tests.Integration;

/// <summary>Job types that exist only in the tests, and what they record.</summary>
internal static class TestJobs
{
    /// <summary>Registers the test job types and their recorder. Call it on a test class's own derived app.</summary>
    public static IServiceCollection AddTestJobs(this IServiceCollection services)
    {
        services.AddSingleton<JobRecorder>();
        services.AddJobType<RecordingJob>();
        services.AddJobType<FailingJob>();
        services.AddJobType<FailingTwiceJob>();
        services.AddJobType<FlakyJob>();
        services.AddJobType<EarlyExitJob>();
        services.AddJobType<ThrowingSuccessHookJob>();
        services.AddSingleton<JobCounter>();
        services.AddJobType<CountingJob>();
        return services;
    }
}

/// <summary>
/// What test jobs did, kept per app. Test classes run in parallel, so each class reads the recorder of its own app
/// through that app's <c>Services</c>, never shared static state.
/// </summary>
internal sealed class JobRecorder
{
    /// <summary>The note of each <see cref="RecordingJob"/> run, in the order they ran.</summary>
    public ConcurrentQueue<string> Notes { get; } = new();

    /// <summary>The note of each deleted <see cref="RecordingJob"/>, in the order their cleanups ran.</summary>
    public ConcurrentQueue<string> Cleanups { get; } = new();

    /// <summary>
    /// The note of each job whose success hook ran, in the order they ran, with the job's status as the hook read it
    /// from the database.
    /// </summary>
    public ConcurrentQueue<(string Note, JobStatus? Status)> SuccessHooks { get; } = new();

    /// <summary>The note of each payload whose job ran a step that should never run.</summary>
    public ConcurrentQueue<string> SkippedStepsRun { get; } = new();

    /// <summary>The note of each payload whose <see cref="FlakyJob"/> has already failed its one time.</summary>
    public ConcurrentDictionary<string, bool> FlakesFailed { get; } = new();
}

/// <summary>What a <see cref="RecordingJob"/> works on.</summary>
internal sealed record RecordingPayload(string Note);

/// <summary>What a <see cref="RecordingJob"/> produces: the length of its note, so the note itself stays out of the result.</summary>
internal sealed record RecordingResult(int NoteLength);

/// <summary>
/// A job whose first step records its payload's note and whose second sets a result. Its success hook records the
/// note with the job's status as the database holds it then, and deleting it records the note among the cleanups.
/// </summary>
internal sealed class RecordingJob : JobType
{
    public const string TypeName = "test-recording";

    public override string Name => TypeName;

    public override JobRun CreateRun(string payloadJson, IServiceProvider services)
    {
        var payload = JobJson.Deserialize<RecordingPayload>(payloadJson);
        var recorder = services.GetRequiredService<JobRecorder>();

        var run = new JobRun();
        run.AddStep("record", _ =>
        {
            recorder.Notes.Enqueue(payload.Note);
            return Task.CompletedTask;
        });
        run.AddStep("summarise", _ =>
        {
            run.SetResult(new RecordingResult(payload.Note.Length));
            return Task.CompletedTask;
        });
        return run;
    }

    public override async Task OnSucceededAsync(string payloadJson, IServiceProvider services)
    {
        var note = JobJson.Deserialize<RecordingPayload>(payloadJson).Note;
        var status = await ReadStatusAsync(TypeName, note, services);
        services.GetRequiredService<JobRecorder>().SuccessHooks.Enqueue((note, status));
    }

    public override Task OnDeletedAsync(string payloadJson, IServiceProvider services)
    {
        var payload = JobJson.Deserialize<RecordingPayload>(payloadJson);
        services.GetRequiredService<JobRecorder>().Cleanups.Enqueue(payload.Note);
        return Task.CompletedTask;
    }

    /// <summary>The status of the job of this type whose payload holds the note, read from the database.</summary>
    internal static async Task<JobStatus?> ReadStatusAsync(string type, string note, IServiceProvider services)
    {
        var db = services.GetRequiredService<NookDbContext>();
        var jobs = await db.Jobs.AsNoTracking().Where(j => j.Type == type).ToListAsync();
        return jobs.SingleOrDefault(j => JobJson.Deserialize<RecordingPayload>(j.Payload).Note == note)?.Status;
    }
}

/// <summary>A job whose second of three steps throws, so its third never runs.</summary>
internal class FailingJob : JobType
{
    public const string TypeName = "test-failing";
    public const string ErrorMessage = "The test step failed on purpose.";

    public override string Name => TypeName;

    public override JobRun CreateRun(string payloadJson, IServiceProvider services)
    {
        var payload = JobJson.Deserialize<RecordingPayload>(payloadJson);
        var recorder = services.GetRequiredService<JobRecorder>();

        var run = new JobRun();
        run.AddStep("prepare", _ => Task.CompletedTask);
        run.AddStep("fail", _ => throw new InvalidOperationException(ErrorMessage));
        run.AddStep("never", _ =>
        {
            recorder.SkippedStepsRun.Enqueue(payload.Note);
            return Task.CompletedTask;
        });
        return run;
    }

    /// <summary>Records the note, as <see cref="RecordingJob"/> does, should the hook ever run for a failed job.</summary>
    public override async Task OnSucceededAsync(string payloadJson, IServiceProvider services)
    {
        var note = JobJson.Deserialize<RecordingPayload>(payloadJson).Note;
        var status = await RecordingJob.ReadStatusAsync(Name, note, services);
        services.GetRequiredService<JobRecorder>().SuccessHooks.Enqueue((note, status));
    }
}

/// <summary>A <see cref="FailingJob"/> whose type allows two attempts.</summary>
internal sealed class FailingTwiceJob : FailingJob
{
    public new const string TypeName = "test-failing-twice";

    public override string Name => TypeName;

    public override int Attempts => 2;
}

/// <summary>A job with two steps whose second throws the first time a payload's note reaches it, and passes after that.</summary>
internal sealed class FlakyJob : JobType
{
    public const string TypeName = "test-flaky";
    public const string ErrorMessage = "The test step failed its first time on purpose.";

    public override string Name => TypeName;

    public override JobRun CreateRun(string payloadJson, IServiceProvider services)
    {
        var payload = JobJson.Deserialize<RecordingPayload>(payloadJson);
        var recorder = services.GetRequiredService<JobRecorder>();

        var run = new JobRun();
        run.AddStep("prepare", _ => Task.CompletedTask);
        run.AddStep("flake", _ => recorder.FlakesFailed.TryAdd(payload.Note, true)
            ? throw new InvalidOperationException(ErrorMessage)
            : Task.CompletedTask);
        return run;
    }
}

/// <summary>What an <see cref="EarlyExitJob"/> produces.</summary>
internal sealed record EarlyExitResult(string Outcome);

/// <summary>A job whose first of two steps completes it with a result, so its second never runs.</summary>
internal sealed class EarlyExitJob : JobType
{
    public const string TypeName = "test-early-exit";

    public override string Name => TypeName;

    public override JobRun CreateRun(string payloadJson, IServiceProvider services)
    {
        var payload = JobJson.Deserialize<RecordingPayload>(payloadJson);
        var recorder = services.GetRequiredService<JobRecorder>();

        var run = new JobRun();
        run.AddStep("check", _ =>
        {
            run.Complete(new EarlyExitResult("done early"));
            return Task.CompletedTask;
        });
        run.AddStep("never", _ =>
        {
            recorder.SkippedStepsRun.Enqueue(payload.Note);
            return Task.CompletedTask;
        });
        return run;
    }
}

/// <summary>A job with one step that passes, whose success hook throws.</summary>
internal sealed class ThrowingSuccessHookJob : JobType
{
    public const string TypeName = "test-throwing-success-hook";
    public const string ErrorMessage = "The test success hook failed on purpose.";

    public override string Name => TypeName;

    public override JobRun CreateRun(string payloadJson, IServiceProvider services) =>
        new JobRun().AddStep("pass", _ => Task.CompletedTask);

    public override Task OnSucceededAsync(string payloadJson, IServiceProvider services) =>
        throw new InvalidOperationException(ErrorMessage);
}

/// <summary>What a <see cref="CountingJob"/> works on: a key of the test's choosing that names the job to its <see cref="JobCounter"/>.</summary>
internal sealed record CountingPayload(Guid Key);

/// <summary>A job with one step that reports to the app's <see cref="JobCounter"/>, and waits there while its key is held.</summary>
internal sealed class CountingJob : JobType
{
    public const string TypeName = "test-counting";

    public override string Name => TypeName;

    public override JobRun CreateRun(string payloadJson, IServiceProvider services)
    {
        var payload = JobJson.Deserialize<CountingPayload>(payloadJson);
        var counter = services.GetRequiredService<JobCounter>();
        return new JobRun().AddStep("count", cancellationToken => counter.RunAsync(payload.Key, cancellationToken));
    }
}

/// <summary>
/// What <see cref="CountingJob"/> runs did, kept per app: the key of every run, how many run at once, and the most
/// that ever did. A key that is held before its job is enqueued keeps that job's step waiting until it is released
/// or the run's token is cancelled.
/// </summary>
internal sealed class JobCounter
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _holds = new();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _started = new();
    private int _running;
    private int _mostRunning;

    /// <summary>The key of each run, in the order they started. A key appears once for each time its job ran.</summary>
    public ConcurrentQueue<Guid> Runs { get; } = new();

    /// <summary>How many runs are in their step now.</summary>
    public int Running => Volatile.Read(ref _running);

    /// <summary>The most runs that were ever in their step at once.</summary>
    public int MostRunningAtOnce => Volatile.Read(ref _mostRunning);

    /// <summary>Makes the job with this key wait in its step until <see cref="Release"/>. Call it before enqueueing the job.</summary>
    public void Hold(Guid key) => _holds[key] = NewSource();

    /// <summary>Lets the job with this key finish its step, now or whenever it reaches it.</summary>
    public void Release(Guid key)
    {
        if (_holds.TryGetValue(key, out var hold))
        {
            hold.TrySetResult();
        }
    }

    /// <summary>Completes once the job with this key has started its step. Fails the test after a timeout.</summary>
    public Task WaitUntilStartedAsync(Guid key) =>
        StartedSource(key).Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);

    /// <summary>Completes once the job with this key has started its step; without a timeout, so it can race others.</summary>
    public Task WhenStarted(Guid key) => StartedSource(key).Task;

    internal async Task RunAsync(Guid key, CancellationToken cancellationToken)
    {
        var running = Interlocked.Increment(ref _running);
        try
        {
            RaiseMostRunning(running);
            Runs.Enqueue(key);
            StartedSource(key).TrySetResult();

            if (_holds.TryGetValue(key, out var hold))
            {
                await hold.Task.WaitAsync(cancellationToken);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _running);
        }
    }

    private void RaiseMostRunning(int running)
    {
        var most = Volatile.Read(ref _mostRunning);
        while (running > most)
        {
            var seen = Interlocked.CompareExchange(ref _mostRunning, running, most);
            if (seen == most)
            {
                return;
            }

            most = seen;
        }
    }

    private TaskCompletionSource StartedSource(Guid key) => _started.GetOrAdd(key, _ => NewSource());

    private static TaskCompletionSource NewSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
