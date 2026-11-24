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
    /// <summary>
    /// A <c>Nook:Jobs:LeaseMinutes</c> of about a second, so the lease of a job left running by a dead worker runs out
    /// within a test.
    /// </summary>
    public const string ShortLeaseMinutes = "0.02";

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
    /// Posts a file to the upload endpoint, and returns the answer whatever it is. A null file or section id leaves
    /// that part out of the form.
    /// </summary>
    public static Task<HttpResponseMessage> UploadAsync(
        this HttpClient client, byte[]? bytes, string fileName, Guid? sectionId)
    {
        var form = new MultipartFormDataContent();
        if (sectionId is { } id)
        {
            form.Add(new StringContent(id.ToString()), "sectionId");
        }

        if (bytes is not null)
        {
            form.Add(new ByteArrayContent(bytes), "file", fileName);
        }

        return client.PostAsync("/api/uploads", form, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Uploads a file into the section, or into the Default area when the section id is null, checks it was accepted,
    /// and returns its job once that has succeeded or failed. The job is read through <paramref name="waitThrough"/>
    /// when the uploading client may not read jobs, as only an admin may.
    /// </summary>
    public static async Task<JobDetailResponse> IngestAsync(
        this HttpClient client, byte[] bytes, string fileName, Guid? sectionId, HttpClient? waitThrough = null)
    {
        var response = await client.UploadAsync(bytes, fileName, sectionId);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var upload = await response.ReadAsync<UploadResponse>();
        return await (waitThrough ?? client).WaitForJobAsync(upload.JobId);
    }

    /// <summary>How many jobs there are, as the jobs list reports it to an admin.</summary>
    public static async Task<int> CountJobsAsync(this HttpClient admin) =>
        (await admin.GetAsync<Page<JobSummaryResponse>>("/api/jobs?limit=1")).Total;

    /// <summary>The path of a file or folder inside the library folder under the app's data root.</summary>
    public static string LibraryPath(this NookApiFactory factory, params string[] parts) =>
        Path.Combine([factory.DataPath, "library", .. parts]);

    /// <summary>How many files wait in the app's staging folder.</summary>
    public static int CountStagedFiles(this NookApiFactory factory) =>
        Directory.GetFiles(Path.Combine(factory.DataPath, "staging")).Length;

    /// <summary>
    /// Waits until the app's staging folder holds this many files. A succeeded job's staged file goes a moment after
    /// the job reads as succeeded, so a count after success is waited for rather than read once. Fails the test after
    /// a timeout.
    /// </summary>
    public static async Task WaitForStagedFilesAsync(this NookApiFactory factory, int count)
    {
        var giveUpAt = DateTimeOffset.UtcNow + Timeout;
        while (true)
        {
            var staged = factory.CountStagedFiles();
            if (staged == count)
            {
                return;
            }

            Assert.True(
                DateTimeOffset.UtcNow < giveUpAt,
                $"Staging held {staged} files after {Timeout.TotalSeconds} seconds, not {count}.");
            await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        }
    }

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
