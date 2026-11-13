using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Features.Jobs;
using Nook.Api.Features.Uploads;
using Nook.Api.Ingest;

namespace Nook.Api.Tests.Integration;

/// <summary>
/// Ingests that run side by side must leave the same rows and files as the same ingests run one after another.
/// These are races: each test uploads all its files before waiting for any job, so several workers take them at once.
/// </summary>
public sealed class ConcurrentIngestTests : IClassFixture<NookApiFactory>, IDisposable
{
    private const int WorkerCount = 4;
    private const int FileCount = 8;

    private readonly NookApiFactory _factory;

    // The class runs jobs only on this app, so no second app's workers share its database.
    private readonly WebApplicationFactory<Program> _app;

    public ConcurrentIngestTests(NookApiFactory factory)
    {
        _factory = factory;
        _app = factory.WithWebHostBuilder(builder => builder
            .UseSetting("Nook:Jobs:WorkerCount", WorkerCount.ToString()));
    }

    [Fact]
    public async Task Files_sharing_an_isbn_uploaded_together_become_one_work()
    {
        var library = await _app.AddLibraryAsync();
        var suffix = IngestTestExtensions.UniqueSuffix();
        var epubs = Enumerable.Range(1, FileCount)
            .Select(n => new EpubBuilder().WithTitle("Burning Chrome").WithCreator($"William Gibson {suffix}")
                .WithIdentifier("9780060539825", scheme: "isbn")
                .WithDescription($"Edition {n}.")
                .Build())
            .ToList();
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);

        var jobs = await IngestTogetherAsync(client, epubs, library);

        Assert.All(jobs, job => Assert.Equal(JobStatus.Succeeded, job.Status));
        Assert.All(jobs, job => Assert.Equal(IngestOutcome.Created, job.ReadIngestResult().Outcome));
        Assert.Equal((1, FileCount, FileCount), await _app.CountRowsAsync(library));
        var paths = await _app.ReadPartPathsAsync(library);
        Assert.Equal(FileCount, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var filed = new List<string>();
        foreach (var path in paths)
        {
            var bytes = await File.ReadAllBytesAsync(
                _factory.LibraryPath(library, path.Split('/')), TestContext.Current.CancellationToken);
            filed.Add(Convert.ToHexStringLower(bytes));
        }

        Assert.Equal(epubs.Select(e => Convert.ToHexStringLower(e)).Order(), filed.Order());
    }

    [Fact]
    public async Task Files_naming_the_same_new_author_series_and_tag_share_one_row_of_each()
    {
        var library = await _app.AddLibraryAsync();
        var suffix = IngestTestExtensions.UniqueSuffix();
        var epubs = Enumerable.Range(1, FileCount)
            .Select(n => new EpubBuilder().WithTitle($"Sprawl Story {n}").WithCreator($"William Gibson {suffix}")
                .WithCalibreSeries($"Sprawl {suffix}", n.ToString())
                .WithSubject($"Cyberpunk {suffix}")
                .Build())
            .ToList();
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);

        var jobs = await IngestTogetherAsync(client, epubs, library);

        Assert.All(jobs, job => Assert.Equal(JobStatus.Succeeded, job.Status));
        Assert.Equal((FileCount, FileCount, FileCount), await _app.CountRowsAsync(library));
        Assert.Equal(1, await _app.CountNamedAsync<Person>(suffix));
        Assert.Equal(1, await _app.CountNamedAsync<Series>(suffix));
        Assert.Equal(1, await _app.CountNamedAsync<Tag>(suffix));
    }

    [Fact]
    public async Task Files_joining_a_work_without_a_cover_all_succeed_and_leave_one_cover()
    {
        var library = await _app.AddLibraryAsync();
        var suffix = IngestTestExtensions.UniqueSuffix();
        EpubBuilder Edition(int n) => new EpubBuilder().WithTitle("Spook Country").WithCreator($"William Gibson {suffix}")
            .WithIdentifier("9780399154300", scheme: "isbn")
            .WithDescription($"Edition {n}.");
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);
        var first = await client.IngestAsync(Edition(0).Build(), "spook-country.epub", library.Id);
        Assert.Equal(JobStatus.Succeeded, first.Status);

        // Each file's cover differs, so the work's hash says which one it took.
        var epubs = Enumerable.Range(1, FileCount)
            .Select(n => Edition(n).WithCover([.. TestData.PngCover, (byte)n]).Build())
            .ToList();

        var jobs = await IngestTogetherAsync(client, epubs, library);

        Assert.All(jobs, job => Assert.Equal(JobStatus.Succeeded, job.Status));
        Assert.Equal((1, FileCount + 1, FileCount + 1), await _app.CountRowsAsync(library));
        var folder = _factory.LibraryPath(library, "Books", $"William Gibson {suffix}", "Spook Country");
        var cover = Assert.Single(Directory.GetFiles(folder, "cover.*"));
        Assert.Equal("cover.png", Path.GetFileName(cover));
        var coverBytes = await File.ReadAllBytesAsync(cover, TestContext.Current.CancellationToken);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        var work = await db.Works.SingleAsync(w => w.LibraryId == library.Id, TestContext.Current.CancellationToken);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(coverBytes)), work.CoverHash);
    }

    [Fact]
    public async Task Copies_of_one_file_uploaded_together_become_one_item()
    {
        var library = await _app.AddLibraryAsync();
        var epub = new EpubBuilder().WithTitle("Zero History").WithCreator($"William Gibson {IngestTestExtensions.UniqueSuffix()}").Build();
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);
        var stagedBefore = _factory.CountStagedFiles();

        var jobs = await IngestTogetherAsync(client, Enumerable.Repeat(epub, FileCount).ToList(), library);

        Assert.All(jobs, job => Assert.Equal(JobStatus.Succeeded, job.Status));
        var results = jobs.Select(job => job.ReadIngestResult()).ToList();
        var created = Assert.Single(results, r => r.Outcome == IngestOutcome.Created);
        var duplicates = results.Where(r => r.Outcome == IngestOutcome.Duplicate).ToList();
        Assert.Equal(FileCount - 1, duplicates.Count);
        Assert.All(duplicates, d => Assert.Equal(created.ItemId, d.ItemId));
        Assert.Equal((1, 1, 1), await _app.CountRowsAsync(library));
        await _factory.WaitForStagedFilesAsync(stagedBefore);
    }

    public void Dispose() => _app.Dispose();

    /// <summary>Uploads every file before waiting for any of their jobs, then returns each job once it has ended.</summary>
    private static async Task<List<JobDetailResponse>> IngestTogetherAsync(
        HttpClient client, IReadOnlyList<byte[]> epubs, Library library)
    {
        var jobIds = new List<Guid>();
        for (var i = 0; i < epubs.Count; i++)
        {
            var response = await client.UploadAsync(epubs[i], $"book-{i}.epub", library.Id);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            jobIds.Add((await response.ReadAsync<UploadResponse>()).JobId);
        }

        var jobs = new List<JobDetailResponse>();
        foreach (var id in jobIds)
        {
            jobs.Add(await client.WaitForJobAsync(id));
        }

        return jobs;
    }
}
