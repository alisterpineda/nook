using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Features.Jobs;
using Nook.Api.Features.Works;

namespace Nook.Api.Tests.Integration;

/// <summary>An edit that changes the title or the first author moves the work's files with it.</summary>
public sealed class WorkEditFilingTests : IClassFixture<NookApiFactory>, IDisposable
{
    private const string FailSave = "[fail save]";

    private readonly NookApiFactory _factory;

    // The failing save runs on an app of its own, so no other test's edit meets the interceptor.
    private readonly WebApplicationFactory<Program> _failingApp;

    public WorkEditFilingTests(NookApiFactory factory)
    {
        _factory = factory;
        _failingApp = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<NookDbContext>(options => options.AddInterceptors(new FailingSaveInterceptor()))));
    }

    public void Dispose() => _failingApp.Dispose();

    [Fact]
    public async Task Edit_of_the_title_moves_the_book_and_cover_to_the_folder_the_new_title_makes()
    {
        var (section, work, author, title) = await IngestAsync(_factory);
        var before = await ReadAsync(_factory, section);
        using var admin = await _factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var newTitle = $"Dawn {IngestTestExtensions.UniqueSuffix()}";

        var response = await admin.PutWorkAsync(work.Id, Request(section.Id, newTitle, [author]));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var after = await ReadAsync(_factory, section);
        var folder = $"Books/{author}/{newTitle}";
        Assert.Equal($"{folder}/{newTitle}.epub", after.Part.Path);
        Assert.Equal($"{folder}/cover.png", after.Work.CoverPath);
        Assert.True(File.Exists(LibraryFile(after.Part.Path)));
        Assert.True(File.Exists(LibraryFile(after.Work.CoverPath!)));
        Assert.False(File.Exists(LibraryFile(before.Part.Path)));
        Assert.False(File.Exists(LibraryFile(before.Work.CoverPath!)));
        Assert.False(Directory.Exists(LibraryFile($"Books/{author}/{title}")));
        await AssertServedAsync(admin, after);
    }

    [Fact]
    public async Task Edit_of_the_first_author_moves_the_work_under_the_new_author_and_removes_the_emptied_folders()
    {
        var (section, work, author, title) = await IngestAsync(_factory);
        using var admin = await _factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var newAuthor = $"Lilith {IngestTestExtensions.UniqueSuffix()}";

        var response = await admin.PutWorkAsync(work.Id, Request(section.Id, title, [newAuthor]));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var after = await ReadAsync(_factory, section);
        var folder = $"Books/{newAuthor}/{title}";
        Assert.Equal($"{folder}/{title}.epub", after.Part.Path);
        Assert.Equal($"{folder}/cover.png", after.Work.CoverPath);
        Assert.True(File.Exists(LibraryFile(after.Part.Path)));
        Assert.False(Directory.Exists(LibraryFile($"Books/{author}")));
        await AssertServedAsync(admin, after);
    }

    [Fact]
    public async Task Edit_that_changes_only_the_description_moves_nothing()
    {
        var (section, work, author, title) = await IngestAsync(_factory);
        var before = await ReadAsync(_factory, section);
        using var admin = await _factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await admin.PutWorkAsync(work.Id, Request(section.Id, title, [author], description: "A new blurb."));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var after = await ReadAsync(_factory, section);
        Assert.Equal(before.Part.Path, after.Part.Path);
        Assert.Equal(before.Work.CoverPath, after.Work.CoverPath);
        Assert.True(File.Exists(LibraryFile(after.Part.Path)));
        Assert.True(File.Exists(LibraryFile(after.Work.CoverPath!)));
    }

    [Fact]
    public async Task Edit_that_changes_only_the_letter_case_of_the_title_moves_nothing()
    {
        var (section, work, author, title) = await IngestAsync(_factory);
        var before = await ReadAsync(_factory, section);
        using var admin = await _factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await admin.PutWorkAsync(work.Id, Request(section.Id, title.ToUpperInvariant(), [author]));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var after = await ReadAsync(_factory, section);
        Assert.Equal(title.ToUpperInvariant(), after.Work.Title);
        Assert.Equal(before.Part.Path, after.Part.Path);
        Assert.Equal(before.Work.CoverPath, after.Work.CoverPath);
        Assert.True(File.Exists(LibraryFile(after.Part.Path)));
    }

    [Fact]
    public async Task Edit_into_a_folder_another_work_holds_takes_a_numbered_folder_and_leaves_the_other_work_alone()
    {
        var other = await IngestAsync(_factory);
        var (section, work, _, _) = await IngestAsync(_factory, other.Author);
        var otherBefore = await ReadAsync(_factory, other.Section);
        using var admin = await _factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await admin.PutWorkAsync(work.Id, Request(section.Id, other.Title, [other.Author]));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var after = await ReadAsync(_factory, section);
        var folder = $"Books/{other.Author}/{other.Title} (2)";
        Assert.Equal($"{folder}/{other.Title}.epub", after.Part.Path);
        Assert.Equal($"{folder}/cover.png", after.Work.CoverPath);
        Assert.True(File.Exists(LibraryFile(after.Part.Path)));
        var otherAfter = await ReadAsync(_factory, other.Section);
        Assert.Equal(otherBefore.Part.Path, otherAfter.Part.Path);
        Assert.Equal(otherBefore.Work.CoverPath, otherAfter.Work.CoverPath);
        Assert.True(File.Exists(LibraryFile(otherAfter.Part.Path)));
        Assert.True(File.Exists(LibraryFile(otherAfter.Work.CoverPath!)));
        await AssertServedAsync(admin, otherAfter);
    }

    [Fact]
    public async Task Edit_leaves_the_books_bytes_size_and_hash_as_they_were()
    {
        var (section, work, author, _) = await IngestAsync(_factory);
        var before = await ReadAsync(_factory, section);
        var bytes = await File.ReadAllBytesAsync(LibraryFile(before.Part.Path), TestContext.Current.CancellationToken);
        using var admin = await _factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await admin.PutWorkAsync(work.Id, Request(section.Id, $"Kindred {IngestTestExtensions.UniqueSuffix()}", [author]));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var after = await ReadAsync(_factory, section);
        Assert.NotEqual(before.Part.Path, after.Part.Path);
        Assert.Equal(before.Part.Size, after.Part.Size);
        Assert.Equal(before.Part.Sha256, after.Part.Sha256);
        var moved = await File.ReadAllBytesAsync(LibraryFile(after.Part.Path), TestContext.Current.CancellationToken);
        Assert.Equal(bytes, moved);
        Assert.Equal(after.Part.Sha256, Convert.ToHexStringLower(SHA256.HashData(moved)));
        Assert.Equal(after.Part.Size, moved.Length);
    }

    [Fact]
    public async Task Edit_that_cannot_move_a_file_saves_nothing_and_leaves_the_files_where_they_were()
    {
        var (section, work, author, title) = await IngestAsync(_factory);
        var before = await ReadAsync(_factory, section);
        var newTitle = $"Fledgling {IngestTestExtensions.UniqueSuffix()}";
        Directory.CreateDirectory(LibraryFile($"Books/{author}/{newTitle}/{newTitle}.epub"));
        using var admin = await _factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await SendAsync(admin, work.Id, Request(section.Id, newTitle, [author]));

        Assert.True(response is null || (int)response.StatusCode >= 500, $"Answered {response?.StatusCode}.");
        Assert.Equal(title, (await admin.GetAsync<WorkDetailResponse>($"/api/works/{work.Id}")).Title);
        var after = await ReadAsync(_factory, section);
        Assert.Equal(before.Part.Path, after.Part.Path);
        Assert.Equal(before.Work.CoverPath, after.Work.CoverPath);
        Assert.True(File.Exists(LibraryFile(before.Part.Path)));
        Assert.True(File.Exists(LibraryFile(before.Work.CoverPath!)));
    }

    [Fact]
    public async Task Edit_whose_save_fails_puts_the_files_back()
    {
        var (section, work, author, title) = await IngestAsync(_failingApp);
        var before = await ReadAsync(_failingApp, section);
        using var admin = await _failingApp.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await SendAsync(admin, work.Id, Request(section.Id, $"Wild Seed {FailSave}", [author]));

        Assert.True(response is null || (int)response.StatusCode >= 500, $"Answered {response?.StatusCode}.");
        Assert.Equal(title, (await admin.GetAsync<WorkDetailResponse>($"/api/works/{work.Id}")).Title);
        var after = await ReadAsync(_failingApp, section);
        Assert.Equal(before.Part.Path, after.Part.Path);
        Assert.Equal(before.Work.CoverPath, after.Work.CoverPath);
        Assert.True(File.Exists(LibraryFile(before.Part.Path)));
        Assert.True(File.Exists(LibraryFile(before.Work.CoverPath!)));
        Assert.False(Directory.Exists(LibraryFile($"Books/{author}/Wild Seed {FailSave}")));
    }

    private string LibraryFile(string relativePath) => _factory.LibraryPath(relativePath.Split('/'));

    /// <summary>The response to the edit, or null when the app let the error through to the caller.</summary>
    private static async Task<HttpResponseMessage?> SendAsync(HttpClient client, Guid id, WorkEditRequest request)
    {
        try
        {
            return await client.PutWorkAsync(id, request);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task AssertServedAsync(HttpClient client, (Work Work, Part Part) stored)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/works/{stored.Work.Id}/cover", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/parts/{stored.Part.Id}/file", cancellationToken)).StatusCode);
    }

    /// <summary>The section's one work and its one part, as the database holds them.</summary>
    private static async Task<(Work Work, Part Part)> ReadAsync(WebApplicationFactory<Program> app, LibrarySection section)
    {
        var part = Assert.Single(await app.ReadPartsAsync(section));
        var work = await app.ReadWorkAsync(await app.ReadWorkIdOfItemAsync(part.ItemId));
        return (work, part);
    }

    /// <summary>Ingests an EPUB with a cover into a section of its own, under a title and author no other test shares.</summary>
    private static async Task<(LibrarySection Section, Work Work, string Author, string Title)> IngestAsync(
        WebApplicationFactory<Program> app, string? author = null)
    {
        var section = await app.AddSectionAsync();
        author ??= $"Octavia {IngestTestExtensions.UniqueSuffix()}";
        var title = $"Wild Seed {IngestTestExtensions.UniqueSuffix()}";
        var epub = new EpubBuilder().WithTitle(title).WithCreator(author).WithCover(TestData.PngCover).Build();
        using var admin = await app.CreateSignedInClientAsync(BuiltInRole.Admin);
        var job = await admin.IngestAsync(epub, "book.epub", section.Id);
        Assert.Equal(JobStatus.Succeeded, job.Status);
        var part = Assert.Single(await app.ReadPartsAsync(section));
        var work = await app.ReadWorkAsync(await app.ReadWorkIdOfItemAsync(part.ItemId));
        return (section, work, author, title);
    }

    private static WorkEditRequest Request(Guid sectionId, string title, List<string> authors, string? description = null) =>
        new(title, title, authors, null, [], description, sectionId);

    /// <summary>Throws once the rows of a work whose title holds the mark are written, before the transaction commits.</summary>
    private sealed class FailingSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<Work>()
                .Any(e => e.Entity.Title.Contains(FailSave, StringComparison.Ordinal)) == true)
            {
                throw new InvalidOperationException("The save failed, as the test asked.");
            }

            return base.SavedChangesAsync(eventData, result, cancellationToken);
        }
    }
}
