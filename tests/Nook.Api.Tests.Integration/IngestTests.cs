using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Features;
using Nook.Api.Features.Jobs;
using Nook.Api.Features.Works;
using Nook.Api.Ingest;

namespace Nook.Api.Tests.Integration;

public class IngestTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Uploaded_epub_becomes_a_work_with_one_item_and_one_part()
    {
        var library = await factory.AddLibraryAsync();
        var epub = new EpubBuilder()
            .WithTitle("Ancillary Justice")
            .WithCreator("Ann Leckie")
            .WithCreator("Second Writer")
            .WithCalibreSeries("Imperial Radch", "1")
            .WithSubject("Space Opera")
            .WithSubject("Fiction")
            .WithDescription("A ship in a body.")
            .WithPublisher("Orbit")
            .WithLanguage("en")
            .WithDate("2013-10-01")
            .Build();
        using var client = await factory.CreateSignedInClientAsync(UserRole.Admin);

        var job = await client.IngestAsync(epub, "ancillary.epub", library.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(
            ["identify-format", "check-duplicate", "extract-metadata", "extract-cover", "match-work", "file-item"],
            job.Steps.Select(s => s.Name));
        Assert.All(job.Steps, s => Assert.Equal(JobStepStatus.Succeeded, s.Status));
        var result = ReadResult(job);
        Assert.Equal(IngestOutcome.Created, result.Outcome);

        var works = await client.GetAsync<Page<WorkSummaryResponse>>($"/api/works?libraryId={library.Id}");
        var summary = Assert.Single(works.Items);
        var work = await client.GetAsync<WorkDetailResponse>($"/api/works/{summary.Id}");
        Assert.Equal("Ancillary Justice", work.Title);
        Assert.Equal("Ancillary Justice", work.SortTitle);
        Assert.Equal("A ship in a body.", work.Description);
        Assert.Equal("Orbit", work.Publisher);
        Assert.Equal("en", work.Language);
        Assert.Equal(new DateOnly(2013, 10, 1), work.PublicationDate);
        Assert.Equal(
            [("Ann Leckie", ContributorRole.Author), ("Second Writer", ContributorRole.Author)],
            work.Contributors.Select(c => (c.Name, c.Role)));
        Assert.Equal(("Imperial Radch", 1m), (work.Series?.Name, work.Series?.Position));
        Assert.Equal(["Fiction", "Space Opera"], work.Tags.Select(t => t.Name));
        var item = Assert.Single(work.Items);
        Assert.Equal(result.ItemId, item.Id);
        Assert.Equal(ItemFormat.Epub, item.Format);
        var part = Assert.Single(item.Parts);
        Assert.Equal("application/epub+zip", part.MediaType);
        Assert.Equal(epub.Length, part.Size);
    }

    [Fact]
    public async Task Uploaded_epub_is_filed_under_its_author_and_title_and_leaves_staging()
    {
        var library = await factory.AddLibraryAsync();
        var epub = new EpubBuilder().WithTitle("The Left Hand of Darkness").WithCreator("Ursula K. Le Guin").Build();
        using var client = await factory.CreateSignedInClientAsync(UserRole.Admin);
        var stagedBefore = factory.CountStagedFiles();

        var job = await client.IngestAsync(epub, "upload.epub", library.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var path = factory.LibraryPath(
            library, "Books", "Ursula K. Le Guin", "The Left Hand of Darkness",
            "The Left Hand of Darkness.epub");
        Assert.True(File.Exists(path), $"Expected the book at {path}.");
        Assert.Equal(epub, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(stagedBefore, factory.CountStagedFiles());
    }

    [Fact]
    public async Task Png_cover_is_written_beside_the_book_and_hashed_on_the_work()
    {
        var library = await factory.AddLibraryAsync();
        var epub = new EpubBuilder().WithTitle("Kindred").WithCreator("Octavia E. Butler").WithCover(TestData.PngCover).Build();
        using var client = await factory.CreateSignedInClientAsync(UserRole.Admin);

        var job = await client.IngestAsync(epub, "kindred.epub", library.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var cover = factory.LibraryPath(
            library, "Books", "Octavia E. Butler", "Kindred", "cover.png");
        Assert.True(File.Exists(cover), $"Expected the cover at {cover}.");
        Assert.Equal(TestData.PngCover, await File.ReadAllBytesAsync(cover, TestContext.Current.CancellationToken));
        var works = await client.GetAsync<Page<WorkSummaryResponse>>($"/api/works?libraryId={library.Id}");
        var work = await client.GetAsync<WorkDetailResponse>($"/api/works/{Assert.Single(works.Items).Id}");
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(TestData.PngCover)), work.CoverHash);
    }

    [Fact]
    public async Task Work_created_by_ingest_has_an_empty_search_vector()
    {
        var library = await factory.AddLibraryAsync();
        var epub = new EpubBuilder().WithTitle("Parable of the Sower").WithCreator("Octavia E. Butler").Build();
        using var client = await factory.CreateSignedInClientAsync(UserRole.Admin);

        var job = await client.IngestAsync(epub, "sower.epub", library.Id);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        var work = await db.Works.SingleAsync(w => w.LibraryId == library.Id, TestContext.Current.CancellationToken);
        Assert.Null(work.SearchVector);
    }

    [Fact]
    public async Task Same_file_uploaded_twice_to_one_library_is_a_duplicate_of_the_first_item()
    {
        var library = await factory.AddLibraryAsync();
        var epub = new EpubBuilder().WithTitle("The Dispossessed").WithCreator("Ursula K. Le Guin").Build();
        using var client = await factory.CreateSignedInClientAsync(UserRole.Admin);
        var first = await client.IngestAsync(epub, "dispossessed.epub", library.Id);
        Assert.Equal(JobStatus.Succeeded, first.Status);
        var stagedBefore = factory.CountStagedFiles();

        var second = await client.IngestAsync(epub, "dispossessed-again.epub", library.Id);

        Assert.Equal(JobStatus.Succeeded, second.Status);
        var result = ReadResult(second);
        Assert.Equal(IngestOutcome.Duplicate, result.Outcome);
        Assert.Equal(ReadResult(first).ItemId, result.ItemId);
        Assert.Equal(["identify-format", "check-duplicate"], second.Steps.Select(s => s.Name));
        Assert.Equal((1, 1, 1), await CountRowsAsync(library));
        Assert.Equal(stagedBefore, factory.CountStagedFiles());
    }

    [Fact]
    public async Task Same_file_uploaded_to_two_libraries_makes_a_work_in_each()
    {
        var one = await factory.AddLibraryAsync();
        var other = await factory.AddLibraryAsync();
        var epub = new EpubBuilder().WithTitle("The Lathe of Heaven").WithCreator("Ursula K. Le Guin").Build();
        using var client = await factory.CreateSignedInClientAsync(UserRole.Admin);

        var first = await client.IngestAsync(epub, "lathe.epub", one.Id);
        var second = await client.IngestAsync(epub, "lathe.epub", other.Id);

        Assert.Equal(IngestOutcome.Created, ReadResult(first).Outcome);
        Assert.Equal(IngestOutcome.Created, ReadResult(second).Outcome);
        Assert.NotEqual(ReadResult(first).ItemId, ReadResult(second).ItemId);
        Assert.Equal((1, 1, 1), await CountRowsAsync(one));
        Assert.Equal((1, 1, 1), await CountRowsAsync(other));
    }

    [Fact]
    public async Task Drm_encrypted_epub_fails_its_job_and_creates_nothing()
    {
        var epub = new EpubBuilder().WithTitle("Locked").WithCreator("Some Author")
            .WithEncryption(EpubBuilder.Aes128Cbc)
            .Build();

        await AssertFailsAtMetadataAndCreatesNothingAsync(epub, "DRM-encrypted");
    }

    [Fact]
    public async Task Unreadable_epub_fails_its_job_and_creates_nothing()
    {
        var epub = new EpubBuilder().WithTitle("Broken").WithCreator("Some Author")
            .WithFile("OEBPS/content.opf", "<package><metadata>")
            .Build();

        await AssertFailsAtMetadataAndCreatesNothingAsync(epub, "cannot be read");
    }

    [Fact]
    public async Task Failed_ingest_keeps_its_staged_file_and_runs_again_when_retried()
    {
        var library = await factory.AddLibraryAsync();
        using var client = await factory.CreateSignedInClientAsync(UserRole.Admin);
        var stagedBefore = factory.CountStagedFiles();

        var failed = await client.IngestAsync(TestData.NotABook, "notes.epub", library.Id);

        Assert.Equal(JobStatus.Failed, failed.Status);
        Assert.Equal(stagedBefore + 1, factory.CountStagedFiles());
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        var waiting = await client.GetAsync<JobDetailResponse>($"/api/jobs/{failed.Id}");
        Assert.Equal(JobStatus.Failed, waiting.Status);
        Assert.Equal(1, waiting.AttemptsMade);

        var retry = await client.RetryJobAsync(failed.Id);

        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
        var retried = await client.WaitForJobAsync(failed.Id);
        Assert.Equal(JobStatus.Failed, retried.Status);
        Assert.Contains("unsupported format", retried.Error);
        Assert.Equal(
            [("identify-format", 1), ("identify-format", 2)],
            retried.Steps.Select(s => (s.Name, s.Attempt)));
        Assert.Equal(stagedBefore + 1, factory.CountStagedFiles());
    }

    [Fact]
    public async Task Deleting_a_failed_ingest_removes_the_job_its_steps_and_its_staged_file()
    {
        var library = await factory.AddLibraryAsync();
        using var client = await factory.CreateSignedInClientAsync(UserRole.Admin);
        var stagedBefore = factory.CountStagedFiles();
        var failed = await client.IngestAsync(TestData.NotABook, "notes.epub", library.Id);
        Assert.Equal(JobStatus.Failed, failed.Status);
        Assert.Equal(stagedBefore + 1, factory.CountStagedFiles());

        var response = await client.DeleteJobAsync(failed.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var gone = await client.GetAsync($"/api/jobs/{failed.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        Assert.False(await db.JobSteps.AnyAsync(s => s.JobId == failed.Id, TestContext.Current.CancellationToken));
        Assert.Equal(stagedBefore, factory.CountStagedFiles());
    }

    [Fact]
    public async Task Deleting_a_succeeded_ingest_leaves_the_filed_book_in_place()
    {
        var library = await factory.AddLibraryAsync();
        var epub = new EpubBuilder().WithTitle("Fledgling").WithCreator("Octavia E. Butler").Build();
        using var client = await factory.CreateSignedInClientAsync(UserRole.Admin);
        var job = await client.IngestAsync(epub, "fledgling.epub", library.Id);
        Assert.Equal(JobStatus.Succeeded, job.Status);

        var response = await client.DeleteJobAsync(job.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var path = factory.LibraryPath(
            library, "Books", "Octavia E. Butler", "Fledgling", "Fledgling.epub");
        Assert.Equal(epub, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal((1, 1, 1), await CountRowsAsync(library));
    }

    /// <summary>Ingests the file into a library of its own and checks the job failed reading it, leaving the library empty in the database and on disk.</summary>
    private async Task AssertFailsAtMetadataAndCreatesNothingAsync(byte[] epub, string cause)
    {
        var library = await factory.AddLibraryAsync();
        using var client = await factory.CreateSignedInClientAsync(UserRole.Admin);

        var job = await client.IngestAsync(epub, "book.epub", library.Id);

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Contains(cause, job.Error);
        var failed = Assert.Single(job.Steps, s => s.Status == JobStepStatus.Failed);
        Assert.Equal("extract-metadata", failed.Name);
        Assert.Equal("extract-metadata", job.Steps[^1].Name);
        Assert.Equal((0, 0, 0), await CountRowsAsync(library));
        Assert.False(Directory.Exists(factory.LibraryPath(library, "Books")));
    }

    /// <summary>How many works, items and parts the library holds.</summary>
    private async Task<(int Works, int Items, int Parts)> CountRowsAsync(Library library)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        var cancellationToken = TestContext.Current.CancellationToken;
        var works = db.Works.Where(w => w.LibraryId == library.Id);
        var items = db.Items.Where(i => works.Any(w => w.Id == i.WorkId));
        var parts = db.Parts.Where(p => items.Any(i => i.Id == p.ItemId));
        return (
            await works.CountAsync(cancellationToken),
            await items.CountAsync(cancellationToken),
            await parts.CountAsync(cancellationToken));
    }

    private static IngestResult ReadResult(JobDetailResponse job)
    {
        Assert.NotNull(job.Result);
        var result = job.Result.Value.Deserialize<IngestResult>(ApiTestExtensions.Json);
        Assert.NotNull(result);
        return result;
    }
}
