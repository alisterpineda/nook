using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Jobs;

namespace Nook.Api.Tests.Integration;

public sealed class JobConcurrencyTests : IClassFixture<NookApiFactory>, IDisposable
{
    private const int WorkerCount = 4;

    // The class runs jobs only on this app, so no second app's workers share its database.
    private readonly WebApplicationFactory<Program> _app;

    public JobConcurrencyTests(NookApiFactory factory)
    {
        _app = factory.WithWebHostBuilder(builder => builder
            .UseSetting("Nook:Jobs:WorkerCount", WorkerCount.ToString())
            .ConfigureServices(services => services.AddTestJobs()));
    }

    [Fact]
    public async Task Several_workers_never_run_the_same_job()
    {
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);
        var counter = _app.Services.GetRequiredService<JobCounter>();
        var keys = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToList();

        var ids = new List<Guid>();
        foreach (var key in keys)
        {
            ids.Add(await EnqueueAsync(new CountingPayload(key)));
        }

        foreach (var id in ids)
        {
            var job = await client.WaitForJobAsync(id);
            Assert.Equal(JobStatus.Succeeded, job.Status);
            Assert.Single(job.Steps);
        }

        Assert.Equal(keys.Order(), counter.Runs.Where(keys.Contains).Order());
    }

    [Fact]
    public async Task A_higher_worker_count_runs_jobs_together()
    {
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);
        var counter = _app.Services.GetRequiredService<JobCounter>();
        var keys = Enumerable.Range(0, WorkerCount).Select(_ => Guid.NewGuid()).ToList();
        var ids = new List<Guid>();
        foreach (var key in keys)
        {
            counter.Hold(key);
            ids.Add(await EnqueueAsync(new CountingPayload(key)));
        }

        // Every job is held until all of them have started, which only workers running side by side can do.
        try
        {
            await Task.WhenAll(keys.Select(counter.WaitUntilStartedAsync));
        }
        finally
        {
            keys.ForEach(counter.Release);
        }

        foreach (var id in ids)
        {
            Assert.Equal(JobStatus.Succeeded, (await client.WaitForJobAsync(id)).Status);
        }

        Assert.True(counter.MostRunningAtOnce > 1, $"At most {counter.MostRunningAtOnce} job ran at once.");
    }

    public void Dispose() => _app.Dispose();

    private Task<Guid> EnqueueAsync(object payload) => _app.EnqueueJobAsync(CountingJob.TypeName, payload);
}
