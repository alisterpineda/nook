using System.Data.Common;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Features.Jobs;
using Nook.Api.Ingest;

namespace Nook.Api.Tests.Integration;

/// <summary>
/// An ingest whose save or commit throws removes the copy it filed only when its item is known not to be saved, so a
/// file a saved part points at is never removed. The failures are made by interceptors on the app's database context,
/// for works whose title holds a mark.
/// </summary>
public sealed class IngestSaveFailureTests : IClassFixture<NookApiFactory>, IDisposable
{
    private const string FailSave = "[fail save]";
    private const string FailSaveClaimed = "[fail save after the path is claimed]";
    private const string FailCommit = "[fail commit]";
    private const string FailCommitOnce = "[fail commit once]";

    private readonly NookApiFactory _factory;

    // The class runs jobs only on this app, so no worker without the interceptors shares its database.
    private readonly WebApplicationFactory<Program> _app;

    public IngestSaveFailureTests(NookApiFactory factory)
    {
        _factory = factory;
        _app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<NookDbContext>(options =>
                options.AddInterceptors(new FailingSaveInterceptor(() => _app!.Services),
                    new FailingCommitInterceptor(),
                    new LostCommitAnswerInterceptor(context => MarkedWork(context, FailCommitOnce)?.Id)))));
    }

    [Fact]
    public async Task Failed_save_removes_the_copy_and_keeps_the_staged_file()
    {
        var section = await _app.AddSectionAsync();
        var author = $"Octavia E. Butler {IngestTestExtensions.UniqueSuffix()}";
        var epub = new EpubBuilder().WithTitle($"Wild Seed {FailSave}").WithCreator(author)
            .WithCover(TestData.PngCover)
            .Build();
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);
        var stagedBefore = _factory.CountStagedFiles();

        var job = await client.IngestAsync(epub, "wild-seed.epub", section.Id);

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Contains("The save failed", job.Error);
        Assert.Equal((0, 0, 0), await _app.CountRowsAsync(section));
        Assert.Empty(LibraryFiles(author));
        Assert.Equal(stagedBefore + 1, _factory.CountStagedFiles());
    }

    [Fact]
    public async Task Failed_save_keeps_a_file_that_another_ingest_has_claimed_since()
    {
        var section = await _app.AddSectionAsync();
        var author = $"Octavia E. Butler {IngestTestExtensions.UniqueSuffix()}";
        var epub = new EpubBuilder().WithTitle($"Adulthood Rites {FailSaveClaimed}").WithCreator(author)
            .Build();
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);

        var job = await client.IngestAsync(epub, "adulthood-rites.epub", section.Id);

        // The failed run's own rows are gone, and the part the other ingest saved still has its file.
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Contains("The save failed", job.Error);
        Assert.Equal((1, 1, 1), await _app.CountRowsAsync(section));
        await AssertBookIsAtItsPartPathAsync(section, author, epub);
    }

    [Fact]
    public async Task Commit_that_lands_but_reports_failure_keeps_the_book()
    {
        var section = await _app.AddSectionAsync();
        var author = $"Octavia E. Butler {IngestTestExtensions.UniqueSuffix()}";
        var epub = new EpubBuilder().WithTitle($"Mind of My Mind {FailCommit}").WithCreator(author)
            .WithCover(TestData.PngCover)
            .Build();
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);

        var job = await client.IngestAsync(epub, "mind.epub", section.Id);

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Contains("The commit reported a failure", job.Error);
        Assert.Equal((1, 1, 1), await _app.CountRowsAsync(section));
        await AssertBookIsAtItsPartPathAsync(section, author, epub);
    }

    [Fact]
    public async Task Commit_that_lands_but_reports_a_transient_failure_completes_as_created()
    {
        var section = await _app.AddSectionAsync();
        var author = $"Octavia E. Butler {IngestTestExtensions.UniqueSuffix()}";
        var epub = new EpubBuilder().WithTitle($"Clay's Ark {FailCommitOnce}").WithCreator(author)
            .Build();
        using var client = await _app.CreateSignedInClientAsync(UserRole.Admin);
        var stagedBefore = _factory.CountStagedFiles();

        var job = await client.IngestAsync(epub, "clays-ark.epub", section.Id);

        // The retry finds the item the first pass saved, so it neither files the book again nor calls it a duplicate.
        Assert.Equal(JobStatus.Succeeded, job.Status);
        var result = job.ReadIngestResult();
        Assert.Equal(IngestOutcome.Created, result.Outcome);
        Assert.Equal((1, 1, 1), await _app.CountRowsAsync(section));
        var itemId = await AssertBookIsAtItsPartPathAsync(section, author, epub);
        Assert.Equal(itemId, result.ItemId);
        Assert.Equal(await _app.ReadWorkIdOfItemAsync(itemId), result.WorkId);
        await _factory.WaitForStagedFilesAsync(stagedBefore);
    }

    public void Dispose() => _app.Dispose();

    /// <summary>
    /// Checks the section's one part points at a file holding the bytes, and that it is the only book file in the
    /// author's folder, and returns the part's item id.
    /// </summary>
    private async Task<Guid> AssertBookIsAtItsPartPathAsync(LibrarySection section, string author, byte[] bytes)
    {
        var part = Assert.Single(await _app.ReadPartsAsync(section));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(
            _factory.LibraryPath(part.Path.Split('/')), TestContext.Current.CancellationToken));
        Assert.Equal([part.Path], LibraryFiles(author).Where(path => path.EndsWith(".epub", StringComparison.Ordinal)));
        return part.ItemId;
    }

    /// <summary>
    /// Every file under the author's folder, <c>Books/&lt;author&gt;</c>, as a <c>/</c>-separated path relative to the
    /// library folder.
    /// </summary>
    private string[] LibraryFiles(string author)
    {
        var libraryFolder = _factory.LibraryPath();
        var folder = _factory.LibraryPath("Books", author);
        return Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(libraryFolder, file).Replace(Path.DirectorySeparatorChar, '/'))
                .ToArray()
            : [];
    }

    /// <summary>The work the context tracks whose title holds the mark, or null when it tracks none.</summary>
    private static Work? MarkedWork(DbContext? context, string mark) =>
        context?.ChangeTracker.Entries<Work>().Select(e => e.Entity)
            .FirstOrDefault(work => work.Title.Contains(mark, StringComparison.Ordinal));

    /// <summary>
    /// Throws once the rows of a marked work are written, inside the transaction and before it commits. For the
    /// second mark it first saves, on a connection of its own, a part of another work at the path the failing run
    /// filed its copy at, as an ingest that filed over the copy after the run's connection broke would have.
    /// </summary>
    private sealed class FailingSaveInterceptor(Func<IServiceProvider> services) : SaveChangesInterceptor
    {
        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (MarkedWork(eventData.Context, FailSaveClaimed) is { } claimed)
            {
                var path = eventData.Context!.ChangeTracker.Entries<Part>().Single().Entity.Path;
                await using var scope = services().CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
                var work = TestData.NewWork("Another ingest's work", claimed.SectionId);
                var item = new Item { WorkId = work.Id, Format = ItemFormat.Epub };
                db.AddRange(work, item, new Part
                {
                    ItemId = item.Id,
                    Order = 0,
                    Path = path,
                    MediaType = "application/epub+zip",
                    Size = 1,
                    Sha256 = "another ingest's bytes",
                });
                await db.SaveChangesAsync(cancellationToken);
                throw new InvalidOperationException("The save failed, as the test asked.");
            }

            if (MarkedWork(eventData.Context, FailSave) is not null)
            {
                throw new InvalidOperationException("The save failed, as the test asked.");
            }

            return await base.SavedChangesAsync(eventData, result, cancellationToken);
        }
    }

    /// <summary>
    /// Throws once a transaction holding a marked work has committed, with an error the database's retry strategy
    /// does not count as transient, so the commit has landed and the job still fails.
    /// </summary>
    private sealed class FailingCommitInterceptor : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (MarkedWork(eventData.Context, FailCommit) is not null)
            {
                throw new InvalidOperationException("The commit reported a failure, as the test asked.");
            }

            return base.TransactionCommittedAsync(transaction, eventData, cancellationToken);
        }
    }
}
