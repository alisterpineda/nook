using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Features.Jobs;
using Nook.Api.Jobs;

namespace Nook.Api.Tests.Integration;

public sealed class JobLeaseTests : IClassFixture<NookApiFactory>, IDisposable
{
    // About a second, so a lease that is not renewed runs out within a test.
    private const string LeaseMinutes = "0.02";

    private const string WorkerStoppedError = "The worker stopped before this step finished.";

    // The class runs jobs only on this app, so no second app's workers share its database. Two workers, so one
    // is free to take a job whose lease runs out while the other holds it.
    private readonly WebApplicationFactory<Program> _app;

    public JobLeaseTests(NookApiFactory factory)
    {
        _app = factory.WithWebHostBuilder(builder => builder
            .UseSetting("Nook:Jobs:LeaseMinutes", LeaseMinutes)
            .UseSetting("Nook:Jobs:WorkerCount", "2")
            .ConfigureServices(services => services.AddTestJobs()));
    }

    [Fact]
    public async Task Running_job_with_an_expired_lease_is_run_again()
    {
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);
        var key = Guid.NewGuid();

        var id = await InsertJobOfADeadWorkerAsync(key, reclaimsMade: 0);
        var job = await client.WaitForJobAsync(id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Null(job.Error);
        Assert.Equal((1, 1), (job.ReclaimsMade, job.AttemptsMade));
        Assert.Equal(
            [(1, JobStepStatus.Failed, WorkerStoppedError), (2, JobStepStatus.Succeeded, (string?)null)],
            job.Steps.Select(s => (s.Attempt, s.Status, s.Error)));
        Assert.All(job.Steps, s => Assert.NotNull(s.FinishedAt));
        Assert.Single(_app.Services.GetRequiredService<JobCounter>().Runs, k => k == key);
    }

    [Fact]
    public async Task Job_that_outlives_its_lease_is_not_reclaimed_while_its_worker_is_alive()
    {
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);
        var counter = _app.Services.GetRequiredService<JobCounter>();
        var key = Guid.NewGuid();
        counter.Hold(key);

        var id = await EnqueueAsync(new CountingPayload(key));
        try
        {
            await counter.WaitUntilStartedAsync(key);

            // Several leases' worth, so a lease that was not renewed would be taken by the other worker.
            await Task.Delay(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);
        }
        finally
        {
            counter.Release(key);
        }

        var job = await client.WaitForJobAsync(id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal((0, 1), (job.ReclaimsMade, job.AttemptsMade));
        var step = Assert.Single(job.Steps);
        Assert.Equal((1, JobStepStatus.Succeeded), (step.Attempt, step.Status));
        Assert.Single(counter.Runs, k => k == key);
    }

    [Fact]
    public async Task Job_whose_lease_expires_a_fourth_time_fails_saying_its_worker_kept_dying()
    {
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);
        var key = Guid.NewGuid();

        var id = await InsertJobOfADeadWorkerAsync(key, reclaimsMade: 3);
        var job = await client.WaitForJobAsync(id);

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Contains("kept dying", job.Error);
        Assert.Equal((0, 3), (job.AttemptsMade, job.ReclaimsMade));
        var step = Assert.Single(job.Steps);
        Assert.Equal((JobStepStatus.Failed, WorkerStoppedError), (step.Status, step.Error));
        Assert.DoesNotContain(key, _app.Services.GetRequiredService<JobCounter>().Runs);
    }

    [Fact]
    public async Task Job_failed_for_a_dying_worker_can_be_retried()
    {
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);
        var key = Guid.NewGuid();
        var id = await InsertJobOfADeadWorkerAsync(key, reclaimsMade: 3);
        var failed = await client.WaitForJobAsync(id);
        Assert.Equal(JobStatus.Failed, failed.Status);

        // Held, so the retried job is read before its run can end.
        var counter = _app.Services.GetRequiredService<JobCounter>();
        counter.Hold(key);
        JobDetailResponse retried;
        try
        {
            var response = await client.RetryJobAsync(id);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            retried = await client.GetAsync<JobDetailResponse>($"/api/jobs/{id}");
        }
        finally
        {
            counter.Release(key);
        }

        var job = await client.WaitForJobAsync(id);

        Assert.Contains(retried.Status, (JobStatus[])[JobStatus.Pending, JobStatus.Running]);
        Assert.Null(retried.Error);
        Assert.Equal((0, 1, 0), (retried.AttemptsMade, retried.AttemptsAllowed, retried.ReclaimsMade));
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Null(job.Error);
        Assert.Equal((1, 1, 0), (job.AttemptsMade, job.AttemptsAllowed, job.ReclaimsMade));
        Assert.Equal(
            [(1, JobStepStatus.Failed, WorkerStoppedError), (2, JobStepStatus.Succeeded, (string?)null)],
            job.Steps.Select(s => (s.Attempt, s.Status, s.Error)));
        Assert.Single(_app.Services.GetRequiredService<JobCounter>().Runs, k => k == key);
    }

    public void Dispose() => _app.Dispose();

    /// <summary>
    /// Writes a job as a worker that died mid-step leaves it: running, its lease run out, and its step still running.
    /// </summary>
    private async Task<Guid> InsertJobOfADeadWorkerAsync(Guid key, int reclaimsMade)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        var died = DateTimeOffset.UtcNow.AddMinutes(-1);
        var job = new Job
        {
            Type = CountingJob.TypeName,
            Status = JobStatus.Running,
            Payload = JobJson.Serialize(new CountingPayload(key)),
            AttemptsAllowed = 1,
            ReclaimsMade = reclaimsMade,
            LeaseExpiresAt = died,
        };
        db.Jobs.Add(job);
        db.JobSteps.Add(new JobStep
        {
            JobId = job.Id,
            Name = "count",
            Order = 0,
            Attempt = 1,
            Status = JobStepStatus.Running,
            StartedAt = died.AddSeconds(-30),
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return job.Id;
    }

    private Task<Guid> EnqueueAsync(object payload) => _app.EnqueueJobAsync(CountingJob.TypeName, payload);
}
