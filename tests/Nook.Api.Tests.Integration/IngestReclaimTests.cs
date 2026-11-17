using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Features.Jobs;
using Nook.Api.Ingest;
using Nook.Api.Jobs;

namespace Nook.Api.Tests.Integration;

/// <summary>
/// An ingest whose worker died is claimed again once its lease runs out, and runs from its first step with the staged
/// file it was given, which stays in staging until the job has succeeded.
/// </summary>
public sealed class IngestReclaimTests : IClassFixture<NookApiFactory>, IDisposable
{
    private readonly NookApiFactory _factory;

    // The class runs jobs only on this app, so no second app's workers share its database.
    private readonly WebApplicationFactory<Program> _app;

    public IngestReclaimTests(NookApiFactory factory)
    {
        _factory = factory;
        _app = factory.WithWebHostBuilder(builder => builder.UseSetting("Nook:Jobs:LeaseMinutes", JobTestExtensions.ShortLeaseMinutes));
    }

    [Fact]
    public async Task Ingest_whose_worker_died_before_filing_runs_again_and_creates_the_work()
    {
        var section = await _app.AddSectionAsync();
        var author = $"Ursula K. Le Guin {IngestTestExtensions.UniqueSuffix()}";
        var epub = new EpubBuilder().WithTitle("The Word for World Is Forest").WithCreator(author).Build();
        using var client = await _app.CreateSignedInClientAsync(BuiltInRole.Admin);
        var stagedBefore = _factory.CountStagedFiles();

        var id = await InsertIngestOfADeadWorkerAsync(epub, section, diedIn: "extract-metadata");
        var job = await client.WaitForJobAsync(id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(1, job.ReclaimsMade);
        var result = job.ReadIngestResult();
        Assert.Equal(IngestOutcome.Created, result.Outcome);
        Assert.Equal((1, 1, 1), await _app.CountRowsAsync(section));
        var path = _factory.LibraryPath(
            "Books", author, "The Word for World Is Forest", "The Word for World Is Forest.epub");
        Assert.Equal(epub, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        await _factory.WaitForStagedFilesAsync(stagedBefore);
    }

    [Fact]
    public async Task Ingest_whose_worker_died_after_saving_runs_again_as_a_duplicate_of_its_own_item()
    {
        var section = await _app.AddSectionAsync();
        var epub = new EpubBuilder().WithTitle("The Telling").WithCreator($"Ursula K. Le Guin {IngestTestExtensions.UniqueSuffix()}").Build();
        using var client = await _app.CreateSignedInClientAsync(BuiltInRole.Admin);
        var stagedBefore = _factory.CountStagedFiles();

        // The same bytes ingested in full stand for what the dead run saved before it died.
        var saved = await client.IngestAsync(epub, "telling.epub", section.Id);
        Assert.Equal(JobStatus.Succeeded, saved.Status);
        var savedItemId = saved.ReadIngestResult().ItemId;

        var id = await InsertIngestOfADeadWorkerAsync(epub, section, diedIn: "file-item");
        var job = await client.WaitForJobAsync(id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(1, job.ReclaimsMade);
        var result = job.ReadIngestResult();
        Assert.Equal(IngestOutcome.Duplicate, result.Outcome);
        Assert.Equal(savedItemId, result.ItemId);
        Assert.Equal((1, 1, 1), await _app.CountRowsAsync(section));
        await _factory.WaitForStagedFilesAsync(stagedBefore);
    }

    public void Dispose() => _app.Dispose();

    /// <summary>
    /// Stages the file and writes its ingest job as a worker that died in the named step leaves it: running, its
    /// lease run out, and that step still running after the steps before it succeeded.
    /// </summary>
    private async Task<Guid> InsertIngestOfADeadWorkerAsync(byte[] bytes, LibrarySection section, string diedIn)
    {
        var stagedName = Guid.NewGuid().ToString();
        await File.WriteAllBytesAsync(
            Path.Combine(_factory.DataPath, "staging", stagedName), bytes, TestContext.Current.CancellationToken);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        var died = DateTimeOffset.UtcNow.AddMinutes(-1);
        var job = new Job
        {
            Type = IngestJob.TypeName,
            Status = JobStatus.Running,
            Payload = JobJson.Serialize(new IngestPayload(section.Id, stagedName, "book.epub")),
            AttemptsAllowed = 1,
            LeaseExpiresAt = died,
        };
        db.Jobs.Add(job);
        var steps = IngestTestExtensions.StepNames;
        for (var order = 0; order <= Array.IndexOf(steps, diedIn); order++)
        {
            var running = steps[order] == diedIn;
            db.JobSteps.Add(new JobStep
            {
                JobId = job.Id,
                Name = steps[order],
                Order = order,
                Attempt = 1,
                Status = running ? JobStepStatus.Running : JobStepStatus.Succeeded,
                StartedAt = died.AddSeconds(-30 + order),
                FinishedAt = running ? null : died.AddSeconds(-30 + order),
            });
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return job.Id;
    }
}
