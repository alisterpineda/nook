using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Features.Activity;
using Nook.Api.Features.Jobs;
using Nook.Api.Features.Uploads;
using Nook.Api.Ingest;

namespace Nook.Api.Tests.Integration;

public class ActivityHubTests : IClassFixture<NookApiFactory>, IDisposable
{
    private readonly WebApplicationFactory<Program> factory;

    public ActivityHubTests(NookApiFactory factory)
    {
        // The jobs run on this class's own app, so another class's workers do not take them.
        this.factory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddTestJobs()));
    }

    // A test's app is gone before the next one's starts, so no earlier app's workers take its jobs.
    public void Dispose() => factory.Dispose();

    /// <summary>Reads the job's changes off the connection up to the one that ends in this status, and returns every status seen.</summary>
    private static async Task<List<JobStatus>> StatusesUntilAsync(ActivityHubConnection hub, Guid jobId, JobStatus last)
    {
        var seen = new List<JobStatus>();
        JobSummaryResponse change;
        do
        {
            change = await hub.NextAsync<JobSummaryResponse>("JobChanged", j => j.Id == jobId);
            seen.Add(change.Status);
        }
        while (change.Status != last);

        return seen;
    }

    [Fact]
    public async Task An_admin_is_told_as_a_job_is_queued_runs_and_ends()
    {
        var counter = factory.Services.GetRequiredService<JobCounter>();
        var key = Guid.NewGuid();
        counter.Hold(key);
        await using var admin = await factory.ConnectToActivityHubAsync(BuiltInRole.Admin);
        await using var contributor = await factory.ConnectToActivityHubAsync(BuiltInRole.Contributor);

        // A notice gives the job as it stands when it is read, so only a job no worker may claim yet is sure to be
        // told of as pending.
        var waiting = await factory.EnqueueJobAsync(
            CountingJob.TypeName, new CountingPayload(Guid.NewGuid()), notBefore: DateTimeOffset.UtcNow.AddYears(1));
        var queued = await admin.NextAsync<JobSummaryResponse>("JobChanged", j => j.Id == waiting);
        Assert.Equal(JobStatus.Pending, queued.Status);

        var id = await factory.EnqueueJobAsync(CountingJob.TypeName, new CountingPayload(key));
        await counter.WaitUntilStartedAsync(key);
        counter.Release(key);
        var seen = await StatusesUntilAsync(admin, id, JobStatus.Succeeded);

        Assert.Contains(JobStatus.Running, seen);
        Assert.Equal(JobStatus.Succeeded, seen[^1]);
        await contributor.AssertNoneAsync<JobSummaryResponse>("JobChanged", j => j.Id == id);
    }

    [Fact]
    public async Task An_admin_is_told_when_a_job_is_retried_and_when_it_is_deleted()
    {
        await using var admin = await factory.ConnectToActivityHubAsync(BuiltInRole.Admin);
        await using var contributor = await factory.ConnectToActivityHubAsync(BuiltInRole.Contributor);

        var id = await factory.EnqueueJobAsync(FlakyJob.TypeName, new RecordingPayload(Guid.NewGuid().ToString()));
        var failed = await admin.NextAsync<JobSummaryResponse>(
            "JobChanged", j => j.Id == id && j.Status == JobStatus.Failed);
        Assert.Equal(FlakyJob.ErrorMessage, failed.Error);

        var retry = await admin.Client.RetryJobAsync(id);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, retry.StatusCode);
        // The retry's own notice may already find the job running, so what is sure is that its new run is told of.
        await StatusesUntilAsync(admin, id, JobStatus.Succeeded);

        var delete = await admin.Client.DeleteJobAsync(id);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(id, await admin.NextAsync<Guid>("JobRemoved", removed => removed == id));
        await contributor.AssertNoneAsync<JobSummaryResponse>("JobChanged", j => j.Id == id);
        await contributor.AssertNoneAsync<Guid>("JobRemoved", removed => removed == id);
    }

    private static byte[] Epub(string title) =>
        new EpubBuilder().WithTitle(title).WithCreator($"Author {IngestTestExtensions.UniqueSuffix()}").Build();

    private static async Task<Guid> UploadAsync(ActivityHubConnection uploader, byte[] bytes, string fileName)
    {
        var response = await uploader.Client.UploadAsync(bytes, fileName, sectionId: null);
        Assert.Equal(System.Net.HttpStatusCode.Accepted, response.StatusCode);
        return (await response.ReadAsync<UploadResponse>()).JobId;
    }

    [Fact]
    public async Task The_uploader_is_told_as_their_upload_moves_to_added_and_another_contributor_hears_nothing_of_it()
    {
        await using var uploader = await factory.ConnectToActivityHubAsync(BuiltInRole.Contributor);
        await using var other = await factory.ConnectToActivityHubAsync(BuiltInRole.Contributor);

        var id = await UploadAsync(uploader, Epub("Kindred"), "kindred.epub");

        var queued = await uploader.NextAsync<UploadActivityResponse>("UploadChanged", u => u.JobId == id);
        Assert.Equal("kindred.epub", queued.FileName);
        var added = await uploader.NextAsync<UploadActivityResponse>(
            "UploadChanged", u => u.JobId == id && u.Status == UploadActivityStatus.Added);
        Assert.NotNull(added.WorkId);
        await other.AssertNoneAsync<UploadActivityResponse>("UploadChanged", u => u.JobId == id);
        await other.AssertNoneAsync<Guid>("UploadRemoved", removed => removed == id);
    }

    [Fact]
    public async Task The_uploader_is_told_the_kind_when_their_upload_fails()
    {
        await using var uploader = await factory.ConnectToActivityHubAsync(BuiltInRole.Contributor);

        var id = await UploadAsync(uploader, [1, 2, 3], "broken.epub");

        var failed = await uploader.NextAsync<UploadActivityResponse>(
            "UploadChanged", u => u.JobId == id && u.Status == UploadActivityStatus.Failed);
        Assert.Equal(UploadFailureKind.UnsupportedFormat, failed.Failure);
    }

    [Fact]
    public async Task The_uploader_keeps_an_added_upload_when_its_job_is_deleted()
    {
        await using var uploader = await factory.ConnectToActivityHubAsync(BuiltInRole.Contributor);
        await using var admin = await factory.ConnectToActivityHubAsync(BuiltInRole.Admin);
        var id = await UploadAsync(uploader, Epub("Dawn"), "dawn.epub");
        await uploader.NextAsync<UploadActivityResponse>(
            "UploadChanged", u => u.JobId == id && u.Status == UploadActivityStatus.Added);

        Assert.Equal(System.Net.HttpStatusCode.NoContent, (await admin.Client.DeleteJobAsync(id)).StatusCode);

        var row = await uploader.NextAsync<UploadActivityResponse>("UploadChanged", u => u.JobId == id);
        Assert.Equal(UploadActivityStatus.Added, row.Status);
        await uploader.AssertNoneAsync<Guid>("UploadRemoved", removed => removed == id);
    }

    [Fact]
    public async Task The_uploader_is_told_when_their_failed_upload_is_removed()
    {
        await using var uploader = await factory.ConnectToActivityHubAsync(BuiltInRole.Contributor);
        await using var other = await factory.ConnectToActivityHubAsync(BuiltInRole.Contributor);
        var id = await UploadAsync(uploader, [1, 2, 3], "broken.epub");
        await uploader.NextAsync<UploadActivityResponse>(
            "UploadChanged", u => u.JobId == id && u.Status == UploadActivityStatus.Failed);

        var response = await uploader.Client.DeleteAsync($"/api/activity/uploads/{id}", TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(id, await uploader.NextAsync<Guid>("UploadRemoved", removed => removed == id));
        await other.AssertNoneAsync<Guid>("UploadRemoved", removed => removed == id);
    }

    [Fact]
    public async Task An_admin_hears_of_their_own_uploads_as_uploads_and_of_other_users_uploads_only_as_jobs()
    {
        await using var admin = await factory.ConnectToActivityHubAsync(BuiltInRole.Admin);
        await using var contributor = await factory.ConnectToActivityHubAsync(BuiltInRole.Contributor);

        var theirs = await UploadAsync(contributor, Epub("Wild Seed"), "seed.epub");
        await admin.NextAsync<JobSummaryResponse>("JobChanged", j => j.Id == theirs && j.Status == JobStatus.Succeeded);
        await admin.AssertNoneAsync<UploadActivityResponse>("UploadChanged", u => u.JobId == theirs);

        var mine = await UploadAsync(admin, Epub("Mind of My Mind"), "mind.epub");

        await admin.NextAsync<JobSummaryResponse>("JobChanged", j => j.Id == mine && j.Status == JobStatus.Succeeded);
        await admin.NextAsync<UploadActivityResponse>(
            "UploadChanged", u => u.JobId == mine && u.Status == UploadActivityStatus.Added);
    }

    [Theory]
    [InlineData(BuiltInRole.Admin)]
    [InlineData(BuiltInRole.Contributor)]
    [InlineData(BuiltInRole.Viewer)]
    public async Task A_signed_in_user_can_connect(BuiltInRole role)
    {
        await using var hub = await factory.ConnectToActivityHubAsync(role);

        Assert.Equal(Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected, hub.Connection.State);
    }

    [Fact]
    public async Task A_visitor_cannot_connect()
    {
        await using var connection = factory.BuildActivityHubConnection();

        var failure = await Assert.ThrowsAnyAsync<Exception>(
            () => connection.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains(failure.Flatten(), e => e is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized });
    }
}
