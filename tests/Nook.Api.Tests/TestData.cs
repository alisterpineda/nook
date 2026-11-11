using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;

namespace Nook.Api.Tests;

/// <summary>Rows and catalog queries that several test classes share.</summary>
internal static class TestData
{
    /// <summary>A work in the seeded library with only its required values set.</summary>
    public static Work NewWork(string title) => new() { LibraryId = Library.DefaultId, Title = title, SortTitle = title };

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
