using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;
using Nook.Api.Features.Works;

namespace Nook.Api.Tests.Integration;

/// <summary>Rows and catalog queries that several test classes share.</summary>
internal static class TestData
{
    /// <summary>A work with only its required values set, in no section and sorted by its title unless told otherwise.</summary>
    public static Work NewWork(string title, Guid? sectionId = null, string? sortTitle = null) =>
        new() { SectionId = sectionId, Title = title, SortTitle = sortTitle ?? title };

    /// <summary>A section with only its name set, not yet saved.</summary>
    public static LibrarySection NewSection(string name) => new() { Name = name };

    /// <summary>A file that is no book format at all.</summary>
    public static readonly byte[] NotABook = Encoding.UTF8.GetBytes("This is a plain text file, not a book.");

    /// <summary>A stand-in for a PNG cover: the PNG signature and a few bytes. Nothing decodes it.</summary>
    public static readonly byte[] PngCover = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    /// <summary>Adds a section of its own for one test, so rows from other tests stay out of it.</summary>
    public static async Task<LibrarySection> AddSectionAsync(
        this WebApplicationFactory<Program> factory, string name = "Test")
    {
        var section = new LibrarySection { Name = name };
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        db.Sections.Add(section);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return section;
    }

    /// <summary>Saves rows as <c>AddRowsAsync</c> does, then builds the search vector of every work among them.</summary>
    public static async Task AddSearchableRowsAsync(this WebApplicationFactory<Program> factory, params object[] rows)
    {
        await factory.AddRowsAsync(rows);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        await new WorkSearchIndex(db).RebuildAsync(
            [.. rows.OfType<Work>().Select(w => w.Id)], TestContext.Current.CancellationToken);
    }

    /// <summary>The definition of every index on a table, as PostgreSQL reports it.</summary>
    public static Task<List<string>> ReadIndexDefinitionsAsync(
        this NookDbContext db, string table, CancellationToken cancellationToken) =>
        db.Database
            .SqlQuery<string>($"SELECT indexdef AS \"Value\" FROM pg_indexes WHERE schemaname = 'public' AND tablename = {table}")
            .ToListAsync(cancellationToken);

    /// <summary>How many tags a work is linked to, read from the link table itself.</summary>
    public static Task<int> CountTagLinksAsync(this NookDbContext db, Guid workId, CancellationToken cancellationToken) =>
        db.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM work_tags WHERE work_id = {workId}")
            .SingleAsync(cancellationToken);
}
