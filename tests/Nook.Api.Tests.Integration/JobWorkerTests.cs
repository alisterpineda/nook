using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Nook.Api.Data;
using Nook.Api.Features.Jobs;
using Nook.Api.Jobs;

namespace Nook.Api.Tests.Integration;

public sealed class JobWorkerTests : IClassFixture<NookApiFactory>, IDisposable
{
    // A job whose payload holds this loses the answer to the commit that records it as succeeded, once.
    private const string LostCommitAnswer = "[lose the commit's answer]";

    private readonly NookApiFactory _factory;

    // The class runs jobs only on this app, so no second worker shares its database.
    private readonly WebApplicationFactory<Program> _app;

    public JobWorkerTests(NookApiFactory factory)
    {
        _factory = factory;
        _app = factory.WithWebHostBuilder(builder => builder
            .ConfigureServices(services => services.AddTestJobs())
            .ConfigureTestServices(services => services.ConfigureDbContext<NookDbContext>(options =>
                options.AddInterceptors(new LostCommitAnswerInterceptor(context =>
                    context.ChangeTracker.Entries<Job>().Select(e => e.Entity).FirstOrDefault(job =>
                        job.Status == JobStatus.Succeeded
                        && job.Payload.Contains(LostCommitAnswer, StringComparison.Ordinal))?.Id)))));
    }

    [Fact]
    public async Task Job_runs_its_steps_in_order_and_succeeds_with_its_result()
    {
        using var client = await _app.CreateSignedInClientAsync(BuiltInRole.Admin);

        var id = await EnqueueAsync(RecordingJob.TypeName, "hello");
        var job = await client.WaitForJobAsync(id);

        Assert.Equal(RecordingJob.TypeName, job.Type);
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Null(job.Error);
        Assert.NotNull(job.Result);
        Assert.Equal(5, job.Result.Value.GetProperty("noteLength").GetInt32());
        Assert.Equal((1, 1, 0), (job.AttemptsMade, job.AttemptsAllowed, job.ReclaimsMade));
        Assert.Equal(
            [("record", 0, 1, JobStepStatus.Succeeded, (string?)null), ("summarise", 1, 1, JobStepStatus.Succeeded, null)],
            job.Steps.Select(s => (s.Name, s.Order, s.Attempt, s.Status, s.Error)));
        Assert.All(job.Steps, s =>
        {
            Assert.NotNull(s.FinishedAt);
            Assert.True(s.StartedAt <= s.FinishedAt);
        });
        Assert.True(job.Steps[0].FinishedAt <= job.Steps[1].StartedAt);
        Assert.Contains("hello", _app.Services.GetRequiredService<JobRecorder>().Notes);
    }

    [Fact]
    public async Task Step_that_throws_fails_the_job_with_its_error()
    {
        using var client = await _app.CreateSignedInClientAsync(BuiltInRole.Admin);
        var note = Guid.NewGuid().ToString();

        var id = await EnqueueAsync(FailingJob.TypeName, note);
        var job = await client.WaitForJobAsync(id);

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal(FailingJob.ErrorMessage, job.Error);
        Assert.Null(job.Result);
        Assert.Equal((1, 1), (job.AttemptsMade, job.AttemptsAllowed));
        Assert.Equal(
            [("prepare", 0, 1, JobStepStatus.Succeeded, (string?)null), ("fail", 1, 1, JobStepStatus.Failed, FailingJob.ErrorMessage)],
            job.Steps.Select(s => (s.Name, s.Order, s.Attempt, s.Status, s.Error)));
        Assert.All(job.Steps, s => Assert.NotNull(s.FinishedAt));
        Assert.DoesNotContain(note, _app.Services.GetRequiredService<JobRecorder>().SkippedStepsRun);
    }

    [Fact]
    public async Task Job_type_with_two_attempts_runs_twice_before_failing()
    {
        using var client = await _app.CreateSignedInClientAsync(BuiltInRole.Admin);

        var id = await EnqueueAsync(FailingTwiceJob.TypeName, "twice");
        var job = await client.WaitForJobAsync(id);

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal(FailingJob.ErrorMessage, job.Error);
        Assert.Equal((2, 2), (job.AttemptsMade, job.AttemptsAllowed));
        Assert.Equal(
            [
                ("prepare", 1, JobStepStatus.Succeeded),
                ("fail", 1, JobStepStatus.Failed),
                ("prepare", 2, JobStepStatus.Succeeded),
                ("fail", 2, JobStepStatus.Failed),
            ],
            job.Steps.Select(s => (s.Name, s.Attempt, s.Status)));
    }

    [Fact]
    public async Task Step_that_completes_the_job_early_skips_the_rest()
    {
        using var client = await _app.CreateSignedInClientAsync(BuiltInRole.Admin);
        var note = Guid.NewGuid().ToString();

        var id = await EnqueueAsync(EarlyExitJob.TypeName, note);
        var job = await client.WaitForJobAsync(id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.NotNull(job.Result);
        Assert.Equal("done early", job.Result.Value.GetProperty("outcome").GetString());
        Assert.Equal(1, job.AttemptsMade);
        var step = Assert.Single(job.Steps);
        Assert.Equal(("check", 0, 1, JobStepStatus.Succeeded), (step.Name, step.Order, step.Attempt, step.Status));
        Assert.DoesNotContain(note, _app.Services.GetRequiredService<JobRecorder>().SkippedStepsRun);
    }

    [Fact]
    public async Task Succeeded_job_runs_its_success_hook_after_success_is_recorded()
    {
        using var client = await _app.CreateSignedInClientAsync(BuiltInRole.Admin);
        var note = Guid.NewGuid().ToString();

        var id = await EnqueueAsync(RecordingJob.TypeName, note);
        var job = await client.WaitForJobAsync(id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var hooks = await WaitForSuccessHooksAsync(note);
        var hook = Assert.Single(hooks);
        Assert.Equal(JobStatus.Succeeded, hook.Status);
    }

    [Fact]
    public async Task Success_hook_runs_when_the_commit_that_recorded_success_lost_its_answer()
    {
        using var client = await _app.CreateSignedInClientAsync(BuiltInRole.Admin);
        var note = $"{LostCommitAnswer} {Guid.NewGuid()}";

        var id = await EnqueueAsync(RecordingJob.TypeName, note);
        var job = await client.WaitForJobAsync(id);

        // The write is tried again, finds its first try landed, and still counts as the one that ended the job.
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(1, job.AttemptsMade);
        var hook = Assert.Single(await WaitForSuccessHooksAsync(note));
        Assert.Equal(JobStatus.Succeeded, hook.Status);
    }

    [Fact]
    public async Task Failed_job_does_not_run_its_success_hook()
    {
        using var client = await _app.CreateSignedInClientAsync(BuiltInRole.Admin);
        var note = Guid.NewGuid().ToString();

        var id = await EnqueueAsync(FailingJob.TypeName, note);
        var job = await client.WaitForJobAsync(id);

        Assert.Equal(JobStatus.Failed, job.Status);

        // The one worker runs a job's success hook before it takes the next job, so once a later job's hook has run,
        // the failed job's would have too.
        var later = Guid.NewGuid().ToString();
        await client.WaitForJobAsync(await EnqueueAsync(RecordingJob.TypeName, later));
        await WaitForSuccessHooksAsync(later);
        Assert.DoesNotContain(_app.Services.GetRequiredService<JobRecorder>().SuccessHooks, h => h.Note == note);
    }

    [Fact]
    public async Task The_failure_hook_runs_once_a_job_stays_failed_and_not_while_it_may_run_again()
    {
        using var client = await _app.CreateSignedInClientAsync(BuiltInRole.Admin);
        var recorder = _app.Services.GetRequiredService<JobRecorder>();
        var twice = Guid.NewGuid().ToString();
        var once = Guid.NewGuid().ToString();

        var twiceJob = await client.WaitForJobAsync(await EnqueueAsync(FailingTwiceJob.TypeName, twice));
        var onceJob = await client.WaitForJobAsync(await EnqueueAsync(FailingJob.TypeName, once));

        // The one worker runs a job's hook before it takes the next job, so both hooks have run once a job that
        // succeeds after them has.
        var later = Guid.NewGuid().ToString();
        await client.WaitForJobAsync(await EnqueueAsync(RecordingJob.TypeName, later));
        await WaitForSuccessHooksAsync(later);

        Assert.Equal(JobStatus.Failed, twiceJob.Status);
        Assert.Equal(2, twiceJob.AttemptsMade);
        Assert.Equal(JobStatus.Failed, onceJob.Status);
        Assert.Equal([(twice, (JobStatus?)JobStatus.Failed, (string?)FailingJob.ErrorMessage)], recorder.FailureHooks.Where(h => h.Note == twice));
        Assert.Equal([(once, (JobStatus?)JobStatus.Failed, (string?)FailingJob.ErrorMessage)], recorder.FailureHooks.Where(h => h.Note == once));
        Assert.DoesNotContain(recorder.FailureHooks, h => h.Note == later);
    }

    [Fact]
    public async Task Success_hook_that_throws_leaves_the_job_succeeded()
    {
        using var client = await _app.CreateSignedInClientAsync(BuiltInRole.Admin);

        var id = await EnqueueAsync(ThrowingSuccessHookJob.TypeName, "throws");
        await client.WaitForJobAsync(id);

        // A later job's hook has run once the throwing hook is behind the worker, which is still running jobs.
        var later = Guid.NewGuid().ToString();
        var laterJob = await client.WaitForJobAsync(await EnqueueAsync(RecordingJob.TypeName, later));
        await WaitForSuccessHooksAsync(later);
        var job = await client.GetAsync<JobDetailResponse>($"/api/jobs/{id}");
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Null(job.Error);
        Assert.Equal(1, job.AttemptsMade);
        Assert.All(job.Steps, s => Assert.Equal(JobStepStatus.Succeeded, s.Status));
        Assert.Equal(JobStatus.Succeeded, laterJob.Status);
    }

    [Fact]
    public async Task Job_of_an_unknown_type_fails()
    {
        using var client = await _app.CreateSignedInClientAsync(BuiltInRole.Admin);

        var id = await EnqueueAsync("no-such-type", "unknown");
        var job = await client.WaitForJobAsync(id);

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Contains("unknown job type", job.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no-such-type", job.Error);
        Assert.Equal((1, 1), (job.AttemptsMade, job.AttemptsAllowed));
        Assert.Empty(job.Steps);
    }

    [Fact]
    public async Task Job_with_a_future_not_before_time_waits_until_then()
    {
        using var client = await _app.CreateSignedInClientAsync(BuiltInRole.Admin);
        var notBefore = DateTimeOffset.UtcNow.AddSeconds(2);

        var id = await EnqueueAsync(RecordingJob.TypeName, new RecordingPayload("later"), notBefore);
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        var early = await client.GetAsync<JobDetailResponse>($"/api/jobs/{id}");
        var job = await client.WaitForJobAsync(id);

        Assert.Equal(JobStatus.Pending, early.Status);
        Assert.Empty(early.Steps);
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.True(job.Steps[0].StartedAt >= notBefore, $"The job started at {job.Steps[0].StartedAt:O}, before {notBefore:O}.");
    }

    [Fact]
    public async Task Default_settings_run_one_job_at_a_time()
    {
        var options = _app.Services.GetRequiredService<IOptions<JobsOptions>>().Value;
        Assert.Equal(1, options.WorkerCount);
        Assert.Equal(5, options.LeaseMinutes);

        using var client = await _app.CreateSignedInClientAsync(BuiltInRole.Admin);
        var counter = _app.Services.GetRequiredService<JobCounter>();
        var keys = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList();
        var ids = new List<Guid>();
        foreach (var key in keys)
        {
            counter.Hold(key);
            ids.Add(await EnqueueAsync(CountingJob.TypeName, new CountingPayload(key)));
        }

        // Each held job is given time for a second worker, were there one, to start another before it is let go.
        var waiting = keys.ToList();
        while (waiting.Count > 0)
        {
            var started = await Task.WhenAny(waiting.Select(counter.WhenStarted))
                .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            var key = waiting.Single(k => counter.WhenStarted(k) == started);
            await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
            Assert.Equal(1, counter.Running);
            counter.Release(key);
            waiting.Remove(key);
        }

        foreach (var id in ids)
        {
            Assert.Equal(JobStatus.Succeeded, (await client.WaitForJobAsync(id)).Status);
        }

        Assert.Equal(1, counter.MostRunningAtOnce);
        Assert.Equal(keys.Order(), counter.Runs.Where(keys.Contains).Order());
    }

    [Fact]
    public void Worker_runs_inside_the_api()
    {
        Assert.Contains(_app.Services.GetServices<IHostedService>(), s => s is JobWorker);
    }

    [Fact]
    public void Negative_worker_count_stops_the_api_from_starting()
    {
        // This app never starts, so it runs no worker beside the class's own.
        using var app = _factory.WithWebHostBuilder(builder => builder.UseSetting("Nook:Jobs:WorkerCount", "-1"));

        var error = Assert.Throws<OptionsValidationException>(() => app.CreateClient());

        Assert.Contains(nameof(JobsOptions.WorkerCount), error.Message);
    }

    [Theory]
    [InlineData("Nook:Jobs:LeaseMinutes", "0", nameof(JobsOptions.LeaseMinutes))]
    [InlineData("Nook:Jobs:LeaseMinutes", "1e-10", nameof(JobsOptions.LeaseMinutes))]
    [InlineData("Nook:Jobs:LeaseMinutes", "1e300", nameof(JobsOptions.LeaseMinutes))]
    [InlineData("Nook:Jobs:PollSeconds", "0", nameof(JobsOptions.PollSeconds))]
    [InlineData("Nook:Jobs:PollSeconds", "5000000", nameof(JobsOptions.PollSeconds))]
    [InlineData("Nook:Jobs:PollSeconds", "1e300", nameof(JobsOptions.PollSeconds))]
    public void Lease_or_poll_time_the_worker_cannot_keep_stops_the_api_from_starting(string setting, string value, string option)
    {
        // This app never starts, so it runs no worker beside the class's own.
        using var app = _factory.WithWebHostBuilder(builder => builder.UseSetting(setting, value));

        var error = Assert.Throws<OptionsValidationException>(() => app.CreateClient());

        Assert.Contains(option, error.Message);
    }

    public void Dispose() => _app.Dispose();

    private Task<Guid> EnqueueAsync(string type, string note) => EnqueueAsync(type, new RecordingPayload(note));

    /// <summary>
    /// Waits until the success hook has run for the note, which happens just after its job is recorded as succeeded,
    /// and returns every run of the hook for it. Fails the test after a timeout.
    /// </summary>
    private async Task<List<(string Note, JobStatus? Status)>> WaitForSuccessHooksAsync(string note)
    {
        var recorder = _app.Services.GetRequiredService<JobRecorder>();
        var giveUpAt = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            var hooks = recorder.SuccessHooks.Where(h => h.Note == note).ToList();
            if (hooks.Count > 0)
            {
                return hooks;
            }

            Assert.True(DateTimeOffset.UtcNow < giveUpAt, $"The success hook for '{note}' did not run within 30 seconds.");
            await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        }
    }

    private Task<Guid> EnqueueAsync(string type, object payload, DateTimeOffset? notBefore = null) =>
        _app.EnqueueJobAsync(type, payload, notBefore: notBefore);
}
