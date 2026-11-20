using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Features.Jobs;
using Nook.Api.Features.Uploads;
using Nook.Api.Ingest;
using Nook.Api.Jobs;

namespace Nook.Api.Tests.Integration;

public class AuditLogTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task An_audit_entry_outlives_the_user_and_the_work_it_names()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await factory.CreateUserAsync();
        var work = TestData.NewWork("The Dispossessed");
        var entry = new AuditEntry
        {
            ActorId = user.Id,
            ActorName = "Shevek",
            ActorEmail = user.Email!,
            Action = "work.deleted",
            SubjectType = "work",
            SubjectId = work.Id,
            SubjectName = work.Title,
            SubmissionId = Guid.CreateVersion7(),
            Details = """{"reason":"test"}""",
        };
        await factory.AddRowsAsync(work, entry);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            await db.Works.Where(w => w.Id == work.Id).ExecuteDeleteAsync(cancellationToken);
            await db.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync(cancellationToken);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            Assert.False(await db.Works.AnyAsync(w => w.Id == work.Id, cancellationToken));
            Assert.False(await db.Users.AnyAsync(u => u.Id == user.Id, cancellationToken));

            var kept = await db.AuditEntries.SingleAsync(e => e.Id == entry.Id, cancellationToken);
            Assert.Equal(user.Id, kept.ActorId);
            Assert.Equal("Shevek", kept.ActorName);
            Assert.Equal(user.Email, kept.ActorEmail);
            Assert.Equal("work.deleted", kept.Action);
            Assert.Equal("work", kept.SubjectType);
            Assert.Equal(work.Id, kept.SubjectId);
            Assert.Equal("The Dispossessed", kept.SubjectName);
            Assert.Equal(entry.SubmissionId, kept.SubmissionId);
            Assert.Contains("test", kept.Details);
        }
    }

    [Fact]
    public async Task An_upload_is_logged_with_who_sent_it_its_job_and_its_section()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var section = await factory.AddSectionAsync();
        var (user, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Contributor, "Ursula");
        using var clientSession = client;

        var inSection = await client.UploadAsync(TestData.NotABook, "left-hand.epub", section.Id);
        var inDefault = await client.UploadAsync(TestData.NotABook, "dispossessed.epub", sectionId: null);

        Assert.Equal(HttpStatusCode.Accepted, inSection.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, inDefault.StatusCode);
        var sectionJob = (await inSection.ReadAsync<UploadResponse>()).JobId;
        var defaultJob = (await inDefault.ReadAsync<UploadResponse>()).JobId;

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        var entries = await db.AuditEntries
            .Where(e => e.ActorId == user.Id && e.Action == "upload.submitted")
            .ToListAsync(cancellationToken);
        Assert.Equal(2, entries.Count);

        var entry = Assert.Single(entries, e => e.SubjectId == sectionJob);
        Assert.Equal("Ursula", entry.ActorName);
        Assert.Equal(user.Email, entry.ActorEmail);
        Assert.Equal("job", entry.SubjectType);
        Assert.Equal(entry.Id, entry.SubmissionId);
        using (var details = JsonDocument.Parse(entry.Details))
        {
            Assert.Equal("left-hand.epub", details.RootElement.GetProperty("fileName").GetString());
            Assert.Equal(section.Id, details.RootElement.GetProperty("sectionId").GetGuid());
            Assert.Equal(section.Name, details.RootElement.GetProperty("sectionName").GetString());
        }

        var inDefaultEntry = Assert.Single(entries, e => e.SubjectId == defaultJob);
        using var defaultDetails = JsonDocument.Parse(inDefaultEntry.Details);
        Assert.Equal("dispossessed.epub", defaultDetails.RootElement.GetProperty("fileName").GetString());
        Assert.Equal(JsonValueKind.Null, defaultDetails.RootElement.GetProperty("sectionId").ValueKind);
        Assert.Equal(JsonValueKind.Null, defaultDetails.RootElement.GetProperty("sectionName").ValueKind);
    }

    [Fact]
    public async Task An_ingested_upload_is_logged_against_the_work_it_made()
    {
        var section = await factory.AddSectionAsync();
        var (user, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Contributor, "Octavia");
        using var clientSession = client;
        var epub = new EpubBuilder().WithTitle("Kindred").WithCreator($"Octavia Butler {IngestTestExtensions.UniqueSuffix()}").Build();

        var job = await IngestAsync(client, epub, "kindred.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var result = job.ReadIngestResult();
        var entry = await ReadOutcomeAsync(job.Id, "upload.added");
        Assert.Equal(user.Id, entry.ActorId);
        Assert.Equal("Octavia", entry.ActorName);
        Assert.Equal(user.Email, entry.ActorEmail);
        Assert.Equal("work", entry.SubjectType);
        Assert.Equal(result.WorkId, entry.SubjectId);
        Assert.Equal("Kindred", entry.SubjectName);
        using var details = JsonDocument.Parse(entry.Details);
        Assert.Equal(job.Id, details.RootElement.GetProperty("jobId").GetGuid());
        Assert.Equal(result.ItemId, details.RootElement.GetProperty("itemId").GetGuid());
    }

    [Fact]
    public async Task A_duplicate_upload_is_logged_against_the_work_already_held()
    {
        var section = await factory.AddSectionAsync();
        var (first, firstClient) = await factory.CreateSignedInUserAsync(BuiltInRole.Contributor, "Ursula");
        using var firstClientSession = firstClient;
        var (second, secondClient) = await factory.CreateSignedInUserAsync(BuiltInRole.Contributor, "Samuel");
        using var secondClientSession = secondClient;
        var epub = new EpubBuilder().WithTitle("Dhalgren").WithCreator($"Samuel Delany {IngestTestExtensions.UniqueSuffix()}").Build();

        var held = await IngestAsync(firstClient, epub, "dhalgren.epub", section.Id);
        var again = await IngestAsync(secondClient, epub, "dhalgren-again.epub", section.Id);

        Assert.Equal(JobStatus.Succeeded, again.Status);
        var result = again.ReadIngestResult();
        Assert.Equal(IngestOutcome.Duplicate, result.Outcome);
        var entry = await ReadOutcomeAsync(again.Id, "upload.duplicate");
        Assert.Equal(second.Id, entry.ActorId);
        Assert.Equal("Samuel", entry.ActorName);
        Assert.Equal("work", entry.SubjectType);
        Assert.Equal(held.ReadIngestResult().WorkId, entry.SubjectId);
        Assert.Equal("Dhalgren", entry.SubjectName);
        using var details = JsonDocument.Parse(entry.Details);
        Assert.Equal(again.Id, details.RootElement.GetProperty("jobId").GetGuid());
        Assert.Equal(result.ItemId, details.RootElement.GetProperty("itemId").GetGuid());
    }

    [Theory]
    [InlineData(UploadFailureKind.UnsupportedFormat, "unsupportedFormat")]
    [InlineData(UploadFailureKind.Unreadable, "unreadable")]
    [InlineData(UploadFailureKind.Internal, "internal")]
    public async Task A_failed_upload_is_logged_with_the_kind_of_failure(UploadFailureKind kind, string written)
    {
        var (user, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Contributor, "Nnedi");
        using var clientSession = client;
        var fileName = $"{kind}.epub";

        Guid jobId;
        if (kind == UploadFailureKind.Internal)
        {
            // A job whose staged file is gone fails in a way no kind names.
            var submission = new AuditEntry
            {
                ActorId = user.Id,
                ActorName = "Nnedi",
                ActorEmail = user.Email!,
                Action = "upload.submitted",
                SubjectType = "job",
                SubjectId = Guid.CreateVersion7(),
                SubjectName = fileName,
                Details = "{}",
            };
            submission.SubmissionId = submission.Id;
            await factory.AddRowsAsync(submission);
            jobId = submission.SubjectId!.Value;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<JobQueue>().EnqueueAsync(
                    IngestJob.TypeName,
                    new IngestPayload(null, $"{Guid.NewGuid()}.epub", fileName, submission.Id),
                    id: jobId,
                    cancellationToken: TestContext.Current.CancellationToken);
            }

            using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
            Assert.Equal(JobStatus.Failed, (await admin.WaitForJobAsync(jobId)).Status);
        }
        else
        {
            var bytes = kind == UploadFailureKind.Unreadable
                ? new EpubBuilder().WithEncryption(EpubBuilder.Aes128Cbc).Build()
                : TestData.NotABook;
            var job = await IngestAsync(client, bytes, fileName, (await factory.AddSectionAsync()).Id);
            Assert.Equal(JobStatus.Failed, job.Status);
            jobId = job.Id;
        }

        var entry = await ReadOutcomeAsync(jobId, "upload.failed");
        Assert.Equal(user.Id, entry.ActorId);
        Assert.Equal("Nnedi", entry.ActorName);
        Assert.Equal(user.Email, entry.ActorEmail);
        Assert.Equal("job", entry.SubjectType);
        Assert.Equal(jobId, entry.SubjectId);
        Assert.Equal(fileName, entry.SubjectName);
        using var details = JsonDocument.Parse(entry.Details);
        Assert.Equal(written, details.RootElement.GetProperty("kind").GetString());
    }

    /// <summary>Uploads as the client's user, who may not read jobs, and waits for the job as an admin.</summary>
    private async Task<JobDetailResponse> IngestAsync(HttpClient uploader, byte[] bytes, string fileName, Guid sectionId)
    {
        var response = await uploader.UploadAsync(bytes, fileName, sectionId);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var upload = await response.ReadAsync<UploadResponse>();
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        return await admin.WaitForJobAsync(upload.JobId);
    }

    /// <summary>The one outcome entry of the upload whose job this is, found through its submitted entry.</summary>
    private async Task<AuditEntry> ReadOutcomeAsync(Guid jobId, string action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        var submission = await db.AuditEntries.SingleAsync(
            e => e.Action == "upload.submitted" && e.SubjectId == jobId, TestContext.Current.CancellationToken);
        var outcomes = await db.AuditEntries
            .Where(e => e.SubmissionId == submission.Id && e.Id != submission.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
        var outcome = Assert.Single(outcomes);
        Assert.Equal(action, outcome.Action);
        return outcome;
    }
}
