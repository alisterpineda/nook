using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Features;
using Nook.Api.Features.Jobs;
using Nook.Api.Features.Uploads;
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

    /// <summary>
    /// Posts a file to the upload endpoint, and returns the answer whatever it is. A null file or library id leaves
    /// that part out of the form.
    /// </summary>
    public static Task<HttpResponseMessage> UploadAsync(
        this HttpClient client, byte[]? bytes, string fileName, Guid? libraryId)
    {
        var form = new MultipartFormDataContent();
        if (libraryId is { } id)
        {
            form.Add(new StringContent(id.ToString()), "libraryId");
        }

        if (bytes is not null)
        {
            form.Add(new ByteArrayContent(bytes), "file", fileName);
        }

        return client.PostAsync("/api/uploads", form, TestContext.Current.CancellationToken);
    }

    /// <summary>Uploads a file, checks it was accepted, and returns its job once that has succeeded or failed.</summary>
    public static async Task<JobDetailResponse> IngestAsync(
        this HttpClient client, byte[] bytes, string fileName, Guid libraryId)
    {
        var response = await client.UploadAsync(bytes, fileName, libraryId);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var upload = await response.ReadAsync<UploadResponse>();
        return await client.WaitForJobAsync(upload.JobId);
    }

    /// <summary>How many jobs there are, as the jobs list reports it to an admin.</summary>
    public static async Task<int> CountJobsAsync(this HttpClient admin) =>
        (await admin.GetAsync<Page<JobSummaryResponse>>("/api/jobs?limit=1")).Total;

    /// <summary>The path of a file or folder inside a library's folder under the app's data root.</summary>
    public static string LibraryPath(this NookApiFactory factory, Library library, params string[] parts) =>
        Path.Combine([factory.DataPath, "library", library.FolderName, .. parts]);

    /// <summary>How many files wait in the app's staging folder.</summary>
    public static int CountStagedFiles(this NookApiFactory factory) =>
        Directory.GetFiles(Path.Combine(factory.DataPath, "staging")).Length;

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
