using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Features;
using Nook.Api.Features.Audit;
using Nook.Api.Features.Jobs;
using Nook.Api.Features.Works;

namespace Nook.Api.Tests.Integration;

public class WorkDeleteTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    [Fact]
    public async Task Delete_removes_the_work_its_rows_and_its_files()
    {
        var book = await IngestAsync(withExtras: true);
        var before = await ReadRowsAsync(book);
        Assert.Equal(1, before.Items);
        Assert.Equal(1, before.Parts);
        Assert.True(before.Contributors > 0);
        Assert.True(before.TagLinks > 0);
        Assert.True(before.ExternalIds > 0);
        Assert.True(File.Exists(LibraryFile(book.PartPath)));
        Assert.True(File.Exists(LibraryFile(book.CoverPath)));
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await admin.DeleteAsync($"/api/works/{book.Work.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/works/{book.Work.Id}", TestContext.Current.CancellationToken)).StatusCode);
        var after = await ReadRowsAsync(book);
        Assert.Equal(new Rows(0, 0, 0, 0, 0, 0), after);
        Assert.False(File.Exists(LibraryFile(book.PartPath)));
        Assert.False(File.Exists(LibraryFile(book.CoverPath)));
    }

    [Fact]
    public async Task Delete_removes_the_folders_it_emptied()
    {
        var book = await IngestAsync();
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await admin.DeleteAsync($"/api/works/{book.Work.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(Directory.Exists(LibraryFile($"Books/{book.Author}/{book.Title}")));
        Assert.False(Directory.Exists(LibraryFile($"Books/{book.Author}")));
        Assert.True(Directory.Exists(LibraryFile("Books")));
    }

    [Fact]
    public async Task Delete_removes_a_person_series_and_tag_left_with_no_work_and_keeps_one_another_work_names()
    {
        var book = await IngestAsync(withExtras: true);
        var other = await IngestAsync(author: book.Author, withExtras: true, seriesName: book.SeriesName, tag: book.Tag);
        var third = await IngestAsync(withExtras: true);
        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/works/{book.Work.Id}", TestContext.Current.CancellationToken)).StatusCode);

        // The other work names the first one's person, series and tag, so they stay.
        var kept = await ReadRowsAsync(book);
        Assert.Equal((1, 1, 1), (kept.People, kept.Series, kept.Tags));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/works/{other.Work.Id}", TestContext.Current.CancellationToken)).StatusCode);
        var gone = await ReadRowsAsync(book);
        Assert.Equal((0, 0, 0), (gone.People, gone.Series, gone.Tags));

        // Another work's names are not touched by either delete.
        var untouched = await ReadRowsAsync(third);
        Assert.Equal((1, 1, 1), (untouched.People, untouched.Series, untouched.Tags));
    }

    [Fact]
    public async Task Delete_by_the_owner_removes_the_work()
    {
        var book = await IngestAsync();
        var (owner, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Contributor);
        using var session = client;
        await OwnAsync(book, owner.Id);

        var response = await client.DeleteAsync($"/api/works/{book.Work.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(File.Exists(LibraryFile(book.PartPath)));
    }

    [Fact]
    public async Task Delete_by_a_contributor_who_does_not_own_the_work_is_refused()
    {
        var book = await IngestAsync();
        using var contributor = await factory.CreateSignedInClientAsync(BuiltInRole.Contributor);

        var response = await contributor.DeleteAsync($"/api/works/{book.Work.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertKeptAsync(contributor, book);
    }

    [Fact]
    public async Task Delete_by_a_viewer_is_refused()
    {
        var book = await IngestAsync();
        var (viewer, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Viewer);
        using var session = client;
        await OwnAsync(book, viewer.Id);

        var response = await client.DeleteAsync($"/api/works/{book.Work.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertKeptAsync(client, book);
    }

    [Fact]
    public async Task Delete_with_no_session_is_refused()
    {
        var book = await IngestAsync();
        await factory.SetPublicAccessAsync(true, ContentAccess.Read, ContentAccess.None, (book.Section.Id, ContentAccess.Download));
        using var visitor = factory.CreateClient();

        var response = await visitor.DeleteAsync($"/api/works/{book.Work.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertKeptAsync(visitor, book);
    }

    [Fact]
    public async Task Delete_of_a_work_the_caller_cannot_see_answers_not_found()
    {
        var book = await IngestAsync();
        var visible = TestData.NewSection("Visible to the caller");
        var role = ApiTestExtensions.NewRole("Sees one section", canUpload: true, allSections: false, defaultArea: ContentAccess.None);
        await factory.AddRowsAsync(visible, role);
        await factory.AddRowsAsync(new RoleSection { RoleId = role.Id, SectionId = visible.Id, Access = ContentAccess.Browse });
        var (user, client) = await factory.CreateSignedInUserAsync(role.Id);
        using var session = client;
        await OwnAsync(book, user.Id);

        var response = await client.DeleteAsync($"/api/works/{book.Work.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, (await ReadRowsAsync(book)).Works);
        Assert.True(File.Exists(LibraryFile(book.PartPath)));
    }

    [Fact]
    public async Task Delete_records_the_title_authors_and_files_of_the_work()
    {
        var book = await IngestAsync();
        var (admin, client) = await factory.CreateSignedInUserAsync(BuiltInRole.Admin);
        using var session = client;

        var response = await client.DeleteAsync($"/api/works/{book.Work.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        var entry = Assert.Single(await db.AuditEntries
            .Where(e => e.Action == AuditActions.WorkDeleted && e.SubjectId == book.Work.Id)
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(admin.Id, entry.ActorId);
        Assert.Equal(AuditSubjectTypes.Work, entry.SubjectType);
        using var details = JsonDocument.Parse(entry.Details);
        Assert.Equal(book.Title, details.RootElement.GetProperty("title").GetString());
        Assert.Equal([book.Author], details.RootElement.GetProperty("authors").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(
            new[] { book.PartPath, book.CoverPath }.Order(StringComparer.Ordinal),
            details.RootElement.GetProperty("files").EnumerateArray().Select(e => e.GetString()!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Delete_leaves_a_file_another_work_points_at()
    {
        var book = await IngestAsync();
        var other = await IngestAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            // As when another book is filed over the deleted work's paths before its files go.
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            await db.Parts.Where(p => p.Id == other.PartId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Path, book.PartPath), TestContext.Current.CancellationToken);
            await db.Works.Where(w => w.Id == other.Work.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.CoverPath, book.CoverPath), TestContext.Current.CancellationToken);
        }

        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await admin.DeleteAsync($"/api/works/{book.Work.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(File.Exists(LibraryFile(book.PartPath)));
        Assert.True(File.Exists(LibraryFile(book.CoverPath)));
    }

    [Fact]
    public async Task Delete_that_runs_again_after_a_lost_commit_answer_still_removes_the_files()
    {
        var book = await IngestAsync();
        await using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<NookDbContext>(options =>
                options.AddInterceptors(new LostCommitAnswerInterceptor(context =>
                    context.ChangeTracker.Entries<AuditEntry>().Select(e => e.Entity).FirstOrDefault(e =>
                        e.Action == AuditActions.WorkDeleted && e.SubjectId == book.Work.Id)?.Id)))));
        using var admin = await app.CreateSignedInClientAsync(BuiltInRole.Admin);

        var response = await admin.DeleteAsync($"/api/works/{book.Work.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, (await ReadRowsAsync(book)).Works);
        Assert.False(File.Exists(LibraryFile(book.PartPath)));
        Assert.False(File.Exists(LibraryFile(book.CoverPath)));
    }

    private string LibraryFile(string relativePath) => factory.LibraryPath(relativePath.Split('/'));

    private async Task AssertKeptAsync(HttpClient client, Book book)
    {
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/works/{book.Work.Id}", TestContext.Current.CancellationToken)).StatusCode);
        Assert.True(File.Exists(LibraryFile(book.PartPath)));
        Assert.True(File.Exists(LibraryFile(book.CoverPath)));
    }

    private async Task OwnAsync(Book book, Guid ownerId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        await db.Works.Where(w => w.Id == book.Work.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.OwnerId, ownerId), TestContext.Current.CancellationToken);
    }

    private sealed record Rows(int Works, int Items, int Parts, int Contributors, int TagLinks, int ExternalIds)
    {
        public int People { get; init; }

        public int Series { get; init; }

        public int Tags { get; init; }
    }

    /// <summary>The rows the book's work and its names have now, counted by the ids the test read before the delete.</summary>
    private async Task<Rows> ReadRowsAsync(Book book)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        var works = await db.Works.CountAsync(w => w.Id == book.Work.Id, cancellationToken);
        var items = await db.Items.CountAsync(i => i.WorkId == book.Work.Id, cancellationToken);
        var parts = await db.Parts.CountAsync(p => p.Id == book.PartId, cancellationToken);
        var contributors = await db.WorkContributors.CountAsync(c => c.WorkId == book.Work.Id, cancellationToken);
        var tagLinks = await db.Works.Where(w => w.Id == book.Work.Id).SelectMany(w => w.Tags).CountAsync(cancellationToken);
        var externalIds = await db.Set<WorkExternalId>().CountAsync(e => e.WorkId == book.Work.Id, cancellationToken);
        return new Rows(works, items, parts, contributors, tagLinks, externalIds)
        {
            People = await db.People.CountAsync(p => p.Name == book.Author, cancellationToken),
            Series = book.SeriesName is { } series ? await db.Series.CountAsync(s => s.Name == series, cancellationToken) : 0,
            Tags = book.Tag is { } tag ? await db.Tags.CountAsync(t => t.Name == tag, cancellationToken) : 0,
        };
    }

    private sealed record Book(
        LibrarySection Section, Work Work, Guid PartId, string PartPath, string CoverPath, string Author, string Title, string? SeriesName, string? Tag);

    /// <summary>Ingests an EPUB with a cover into a section of its own, under a title no other test shares.</summary>
    private async Task<Book> IngestAsync(string? author = null, bool withExtras = false, string? seriesName = null, string? tag = null)
    {
        var section = await factory.AddSectionAsync();
        var suffix = IngestTestExtensions.UniqueSuffix();
        author ??= $"Octavia {suffix}";
        var title = $"Wild Seed {suffix}";
        var builder = new EpubBuilder().WithTitle(title).WithCreator(author).WithCover(TestData.PngCover);
        if (withExtras)
        {
            seriesName ??= $"Patternist {suffix}";
            tag ??= $"Fantasy {suffix}";
            builder = builder
                .WithIdentifier(IngestTestExtensions.UniqueIsbn(), scheme: "ISBN")
                .WithSubject(tag)
                .WithCalibreSeries(seriesName, "1");
        }

        using var admin = await factory.CreateSignedInClientAsync(BuiltInRole.Admin);
        var job = await admin.IngestAsync(builder.Build(), "book.epub", section.Id);
        Assert.Equal(JobStatus.Succeeded, job.Status);
        var part = Assert.Single(await factory.ReadPartsAsync(section));
        var work = await factory.ReadWorkAsync(await factory.ReadWorkIdOfItemAsync(part.ItemId));
        return new Book(section, work, part.Id, part.Path, work.CoverPath!, author, title, seriesName, tag);
    }
}
