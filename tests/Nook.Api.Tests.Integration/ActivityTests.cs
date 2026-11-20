using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Features.Activity;
using Nook.Api.Features.Uploads;
using Nook.Api.Ingest;
using Nook.Api.Jobs;

namespace Nook.Api.Tests.Integration;

public class ActivityTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    private const string Url = "/api/activity/uploads";

    [Fact]
    public async Task Uploads_list_gives_the_caller_s_own_newest_first_with_where_each_stands()
    {
        var section = await factory.AddSectionAsync();
        var (user, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Contributor, "Ursula");
        using var clientSession = client;
        var epub = new EpubBuilder().WithTitle("Always Coming Home").WithCreator($"Ursula Le Guin {IngestTestExtensions.UniqueSuffix()}").Build();
        var addedJob = await UploadAndWaitAsync(client, epub, "home.epub", section.Id);
        var queued = await SubmitAsync(user, "queued.epub", section, JobStatus.Pending);
        var running = await SubmitAsync(user, "running.epub", null, JobStatus.Running);

        var list = await client.GetAsync<List<UploadActivityResponse>>(Url);

        Assert.Equal([running.Id, queued.Id, addedJob], list.Select(r => r.JobId));
        var runningRow = list[0];
        Assert.Equal("running.epub", runningRow.FileName);
        Assert.Equal(UploadActivityStatus.Running, runningRow.Status);
        Assert.Null(runningRow.SectionId);
        Assert.Null(runningRow.SectionName);
        Assert.Null(runningRow.WorkId);
        Assert.Null(runningRow.Failure);
        Assert.True(runningRow.SubmittedAt > DateTimeOffset.UtcNow.AddMinutes(-5));
        Assert.Equal(UploadActivityStatus.Queued, list[1].Status);
        Assert.Equal(section.Id, list[1].SectionId);
        Assert.Equal(section.Name, list[1].SectionName);
        Assert.Equal(UploadActivityStatus.Added, list[2].Status);
        Assert.NotNull(list[2].WorkId);
    }

    [Fact]
    public async Task A_duplicate_upload_is_listed_with_the_work_already_held()
    {
        var section = await factory.AddSectionAsync();
        var (_, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Contributor);
        using var clientSession = client;
        var epub = new EpubBuilder().WithTitle("Parable of the Sower").WithCreator($"Octavia Butler {IngestTestExtensions.UniqueSuffix()}").Build();
        var first = await UploadAndWaitAsync(client, epub, "a.epub", section.Id);
        var second = await UploadAndWaitAsync(client, epub, "b.epub", section.Id);

        var list = await client.GetAsync<List<UploadActivityResponse>>(Url);

        Assert.Equal(UploadActivityStatus.Duplicate, list[0].Status);
        Assert.Equal(second, list[0].JobId);
        Assert.Equal(UploadActivityStatus.Added, list[1].Status);
        Assert.Equal(first, list[1].JobId);
        Assert.Equal(list[1].WorkId, list[0].WorkId);
    }

    [Fact]
    public async Task A_failed_upload_gives_its_kind_and_never_the_job_s_error()
    {
        var (user, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Contributor);
        using var clientSession = client;
        var failed = await SubmitAsync(user, "broken.epub", null, JobStatus.Failed, error: "boom at /srv/nook/staging/secret-path");
        await factory.AddRowsAsync(Outcome(failed, "upload.failed", "job", failed.Id, """{"kind":"unreadable"}"""));
        var unknown = await SubmitAsync(user, "other.epub", null, JobStatus.Failed, error: "another /srv/nook/secret");

        var response = await client.GetAsync(Url, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("secret", body);
        Assert.DoesNotContain("boom", body);
        Assert.Contains("\"failure\":\"unreadable\"", body);
        var list = await response.ReadAsync<List<UploadActivityResponse>>();
        Assert.Equal(UploadActivityStatus.Failed, list[1].Status);
        Assert.Equal(UploadFailureKind.Unreadable, list[1].Failure);
        Assert.Equal(unknown.Id, list[0].JobId);
        Assert.Equal(UploadFailureKind.Internal, list[0].Failure);
    }

    [Fact]
    public async Task An_added_upload_stays_listed_after_its_job_is_deleted()
    {
        var (user, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Contributor);
        using var clientSession = client;
        var workId = Guid.CreateVersion7();
        var submission = await SubmitAsync(user, "kept.epub", null, JobStatus.Succeeded);
        await factory.AddRowsAsync(Outcome(submission, "upload.added", "work", workId, "{}"));
        await DeleteJobAsync(submission.Id);

        var list = await client.GetAsync<List<UploadActivityResponse>>(Url);

        var row = Assert.Single(list);
        Assert.Equal(submission.Id, row.JobId);
        Assert.Equal(UploadActivityStatus.Added, row.Status);
        Assert.Equal(workId, row.WorkId);
    }

    [Fact]
    public async Task A_failed_upload_whose_job_is_deleted_is_no_longer_listed()
    {
        var (user, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Contributor);
        using var clientSession = client;
        var jobs = new List<Submitted>();
        for (var i = 0; i < 21; i++)
        {
            jobs.Add(await SubmitAsync(user, $"file-{i}.epub", null, JobStatus.Pending));
        }

        // The newest is removed, so the list reads past it to still give 20.
        await DeleteJobAsync(jobs[^1].Id);

        var list = await client.GetAsync<List<UploadActivityResponse>>(Url);

        Assert.Equal(20, list.Count);
        Assert.DoesNotContain(list, r => r.JobId == jobs[^1].Id);
        Assert.Equal(jobs[^2].Id, list[0].JobId);
        Assert.Equal(jobs[0].Id, list[^1].JobId);
    }

    [Fact]
    public async Task Another_user_s_uploads_are_not_listed()
    {
        var (other, otherClient) = await factory.CreateSignedInUserAsync(BuiltInRole.Contributor);
        using var otherSession = otherClient;
        var (_, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Contributor);
        using var clientSession = client;
        await SubmitAsync(other, "theirs.epub", null, JobStatus.Pending);

        Assert.Empty(await client.GetAsync<List<UploadActivityResponse>>(Url));
        Assert.Single(await otherClient.GetAsync<List<UploadActivityResponse>>(Url));
    }

    [Fact]
    public async Task A_visitor_is_refused()
    {
        using var visitor = factory.CreateClient();

        var response = await visitor.GetAsync(Url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_job_s_upload_is_found_by_its_job_with_its_uploader()
    {
        var (user, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Contributor);
        using var clientSession = client;
        var job = await SubmitAsync(user, "found.epub", null, JobStatus.Pending);
        await using var scope = factory.Services.CreateAsyncScope();
        var activity = scope.ServiceProvider.GetRequiredService<UploadActivity>();

        var found = await activity.ForJobAsync(job.Id, TestContext.Current.CancellationToken);
        await DeleteJobAsync(job.Id);
        var gone = await activity.ForJobAsync(job.Id, TestContext.Current.CancellationToken);
        var unknown = await activity.ForJobAsync(Guid.CreateVersion7(), TestContext.Current.CancellationToken);

        Assert.Equal(user.Id, found!.UserId);
        Assert.Equal(UploadActivityStatus.Queued, found.Row!.Status);
        Assert.Equal("found.epub", found.Row.FileName);
        Assert.Equal(user.Id, gone!.UserId);
        Assert.Null(gone.Row);
        Assert.Null(unknown);
    }

    [Fact]
    public async Task The_uploader_can_retry_their_failed_upload()
    {
        var (user, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Contributor);
        using var clientSession = client;
        var epub = new EpubBuilder().WithTitle("The Left Hand of Darkness").WithCreator($"Ursula Le Guin {IngestTestExtensions.UniqueSuffix()}").Build();
        var failed = await SubmitAsync(user, "retry.epub", null, JobStatus.Failed, error: "first run broke", staged: epub);

        var response = await RetryAsync(client, failed.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        Assert.Equal(JobStatus.Succeeded, (await admin.WaitForJobAsync(failed.Id)).Status);
    }

    [Fact]
    public async Task The_uploader_can_remove_their_failed_upload_and_its_staged_file()
    {
        var (user, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Contributor);
        using var clientSession = client;
        var failed = await SubmitAsync(user, "remove.epub", null, JobStatus.Failed, error: "broke", staged: [1, 2, 3]);
        var stagedFile = Path.Combine(factory.DataPath, "staging", failed.StagedName);
        Assert.True(File.Exists(stagedFile));

        var response = await RemoveAsync(client, failed.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(File.Exists(stagedFile));
        Assert.Empty(await client.GetAsync<List<UploadActivityResponse>>(Url));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        Assert.False(await db.Jobs.AnyAsync(j => j.Id == failed.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Retrying_or_removing_someone_else_s_upload_is_not_found()
    {
        var (other, otherClient) = await factory.CreateSignedInUserAsync(BuiltInRole.Contributor);
        using var otherSession = otherClient;
        var (_, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Contributor);
        using var clientSession = client;
        var failed = await SubmitAsync(other, "theirs.epub", null, JobStatus.Failed, error: "broke");
        // A job nobody submitted through an upload is as unreachable as one that never was.
        var bare = new Job { Type = IngestJob.TypeName, Status = JobStatus.Failed, Payload = "{}", AttemptsAllowed = 1 };
        await factory.AddRowsAsync(bare);

        Assert.Equal(HttpStatusCode.NotFound, (await RetryAsync(client, failed.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RemoveAsync(client, failed.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RetryAsync(client, bare.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RemoveAsync(client, bare.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RetryAsync(client, Guid.CreateVersion7())).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        var job = await db.Jobs.SingleAsync(j => j.Id == failed.Id, TestContext.Current.CancellationToken);
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.False(await db.AuditEntries.AnyAsync(
            e => e.SubmissionId == failed.Entry.Id && e.Id != failed.Entry.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Retrying_an_upload_that_has_not_failed_is_refused()
    {
        var (user, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Contributor);
        using var clientSession = client;
        var queued = await SubmitAsync(user, "queued.epub", null, JobStatus.Pending);
        var running = await SubmitAsync(user, "running.epub", null, JobStatus.Running);

        var retryQueued = await RetryAsync(client, queued.Id);
        var retryRunning = await RetryAsync(client, running.Id);
        var removeRunning = await RemoveAsync(client, running.Id);

        Assert.Equal(HttpStatusCode.Conflict, retryQueued.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, retryRunning.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, removeRunning.StatusCode);
        Assert.Equal("application/problem+json", retryQueued.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Only a failed job can be retried.", await retryQueued.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var list = await client.GetAsync<List<UploadActivityResponse>>(Url);
        Assert.Equal([UploadActivityStatus.Running, UploadActivityStatus.Queued], list.Select(r => r.Status));
    }

    [Fact]
    public async Task Retrying_and_removing_are_logged()
    {
        var (user, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Contributor, "Ursula");
        using var clientSession = client;
        var retried = await SubmitAsync(user, "again.epub", null, JobStatus.Failed, error: "broke");
        var removed = await SubmitAsync(user, "gone.epub", null, JobStatus.Failed, error: "broke");

        Assert.Equal(HttpStatusCode.NoContent, (await RetryAsync(client, retried.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await RemoveAsync(client, removed.Id)).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        var retryEntry = await db.AuditEntries.SingleAsync(
            e => e.Action == "job.retried" && e.SubmissionId == retried.Entry.Id, TestContext.Current.CancellationToken);
        var removeEntry = await db.AuditEntries.SingleAsync(
            e => e.Action == "job.deleted" && e.SubmissionId == removed.Entry.Id, TestContext.Current.CancellationToken);
        Assert.Equal((user.Id, "job", retried.Id), (retryEntry.ActorId, retryEntry.SubjectType, retryEntry.SubjectId));
        Assert.Equal(user.ShownName, retryEntry.ActorName);
        Assert.Equal((user.Id, "job", removed.Id), (removeEntry.ActorId, removeEntry.SubjectType, removeEntry.SubjectId));
    }

    [Fact]
    public async Task A_visitor_cannot_retry_or_remove_an_upload()
    {
        using var visitor = factory.CreateClient();
        var id = Guid.CreateVersion7();

        Assert.Equal(HttpStatusCode.Unauthorized, (await RetryAsync(visitor, id)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RemoveAsync(visitor, id)).StatusCode);
    }

    private static Task<HttpResponseMessage> RetryAsync(HttpClient client, Guid jobId) =>
        client.PostAsync($"{Url}/{jobId}/retry", content: null, TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> RemoveAsync(HttpClient client, Guid jobId) =>
        client.DeleteAsync($"{Url}/{jobId}", TestContext.Current.CancellationToken);

    private async Task<Guid> UploadAndWaitAsync(HttpClient client, byte[] bytes, string fileName, Guid sectionId)
    {
        var response = await client.UploadAsync(bytes, fileName, sectionId);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var jobId = (await response.ReadAsync<UploadResponse>()).JobId;
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        Assert.Equal(JobStatus.Succeeded, (await admin.WaitForJobAsync(jobId)).Status);
        return jobId;
    }

    /// <summary>An upload as it stands in the database: its job and its submitted entry.</summary>
    private sealed record Submitted(Job Job, AuditEntry Entry, string StagedName)
    {
        public Guid Id => Job.Id;
    }

    /// <summary>Adds an upload's submitted entry and a job in the status, as an accepted upload leaves them.</summary>
    private async Task<Submitted> SubmitAsync(
        NookUser user, string fileName, LibrarySection? section, JobStatus status, string? error = null,
        byte[]? staged = null)
    {
        var job = new Job
        {
            Type = IngestJob.TypeName,
            Status = status,
            Payload = "{}",
            Error = error,
            // Held back or leased so no worker picks it up.
            NotBefore = status == JobStatus.Pending ? DateTimeOffset.UtcNow.AddDays(1) : null,
            LeaseExpiresAt = status == JobStatus.Running ? DateTimeOffset.UtcNow.AddDays(1) : null,
            AttemptsAllowed = 1,
        };
        var entry = new AuditEntry
        {
            ActorId = user.Id,
            ActorName = user.ShownName,
            ActorEmail = user.ShownEmail,
            Action = "upload.submitted",
            SubjectType = "job",
            SubjectId = job.Id,
            SubjectName = fileName,
            Details = JobJson.Serialize(new { fileName, sectionId = section?.Id, sectionName = section?.Name }),
        };
        entry.SubmissionId = entry.Id;
        var stagedName = Guid.NewGuid().ToString();
        job.Payload = JobJson.Serialize(new IngestPayload(section?.Id, stagedName, fileName, entry.Id));
        if (staged is not null)
        {
            await File.WriteAllBytesAsync(
                Path.Combine(factory.DataPath, "staging", stagedName), staged, TestContext.Current.CancellationToken);
        }

        await factory.AddRowsAsync(job, entry);
        return new Submitted(job, entry, stagedName);
    }

    private static AuditEntry Outcome(Submitted upload, string action, string subjectType, Guid subjectId, string details) =>
        new()
        {
            ActorName = "x",
            ActorEmail = "x@example.test",
            Action = action,
            SubjectType = subjectType,
            SubjectId = subjectId,
            SubjectName = "x",
            Details = details,
            SubmissionId = upload.Entry.Id,
        };

    private async Task DeleteJobAsync(Guid id)
    {
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteJobAsync(id)).StatusCode);
    }
}
