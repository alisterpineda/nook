using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Features.Jobs;
using Nook.Api.Jobs;

namespace Nook.Api.Tests.Integration;

/// <summary>What tests of background jobs share.</summary>
internal static class JobTestExtensions
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>Enqueues a job on the app, as code inside the API would, and returns its id.</summary>
    public static async Task<Guid> EnqueueJobAsync(
        this WebApplicationFactory<Program> app,
        string type,
        object payload,
        Guid? parentJobId = null,
        DateTimeOffset? notBefore = null)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<JobQueue>();
        return await queue.EnqueueAsync(type, payload, parentJobId, notBefore, TestContext.Current.CancellationToken);
    }

    /// <summary>Asks for a job to be retried, and returns the answer whatever it is.</summary>
    public static Task<HttpResponseMessage> RetryJobAsync(this HttpClient client, Guid id) =>
        client.PostAsync($"/api/jobs/{id}/retry", content: null, TestContext.Current.CancellationToken);

    /// <summary>Asks for a job to be deleted, and returns the answer whatever it is.</summary>
    public static Task<HttpResponseMessage> DeleteJobAsync(this HttpClient client, Guid id) =>
        client.DeleteAsync($"/api/jobs/{id}", TestContext.Current.CancellationToken);

    /// <summary>Reads the job until it has succeeded or failed, and returns it then. Fails the test after a timeout.</summary>
    public static async Task<JobDetailResponse> WaitForJobAsync(this HttpClient client, Guid id)
    {
        var giveUpAt = DateTimeOffset.UtcNow + Timeout;
        while (true)
        {
            var job = await client.GetAsync<JobDetailResponse>($"/api/jobs/{id}");
            if (job.Status is JobStatus.Succeeded or JobStatus.Failed)
            {
                return job;
            }

            Assert.True(
                DateTimeOffset.UtcNow < giveUpAt,
                $"Job {id} was still {job.Status} after {Timeout.TotalSeconds} seconds.");
            await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        }
    }
}
