using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;

namespace Nook.Api.Tests.Integration;

/// <summary>Runs the migration that replaces libraries with sections over a database that already holds works.</summary>
public class SectionMigrationTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    private const string MigrationBeforeSections = "20261113224207_AddCoverPath";

    /// <summary>The library row the first migration seeded.</summary>
    private static readonly Guid SeededLibraryId = new("01a1c7e7-8000-7000-8000-000000000001");

    [Fact]
    public async Task Upgrade_turns_the_seeded_library_into_no_section_and_other_libraries_into_sections()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var comicsId = Guid.NewGuid();
        var seededWork = (Work: Guid.NewGuid(), Item: Guid.NewGuid(), Part: Guid.NewGuid(), Job: Guid.NewGuid());
        var comicsWork = (Work: Guid.NewGuid(), Item: Guid.NewGuid(), Part: Guid.NewGuid(), Job: Guid.NewGuid());
        var now = DateTimeOffset.UtcNow;

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();

        await db.Database.MigrateAsync(MigrationBeforeSections, cancellationToken);
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO libraries (id, name, folder_name, created_at, updated_at)
            VALUES ({comicsId}, 'Comics', 'Comics', {now}, {now})
            """,
            cancellationToken);
        await InsertWorkAsync(db, SeededLibraryId, seededWork, "The Dispossessed", now, cancellationToken);
        await InsertWorkAsync(db, comicsId, comicsWork, "Saga", now, cancellationToken);

        await db.Database.MigrateAsync(cancellationToken);

        Assert.Null(await ReadSectionIdAsync(db, seededWork.Work, cancellationToken));
        Assert.Equal(comicsId, await ReadSectionIdAsync(db, comicsWork.Work, cancellationToken));
        foreach (var ids in new[] { seededWork, comicsWork })
        {
            Assert.Equal(ids.Work, await ReadGuidAsync(
                db, $"SELECT work_id AS \"Value\" FROM items WHERE id = {ids.Item}", cancellationToken));
            Assert.Equal(ids.Item, await ReadGuidAsync(
                db, $"SELECT item_id AS \"Value\" FROM parts WHERE id = {ids.Part}", cancellationToken));
        }

        var sectionIds = await db.Database
            .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM sections")
            .ToListAsync(cancellationToken);
        Assert.Equal([comicsId], sectionIds);
        Assert.Equal("Comics", await db.Database
            .SqlQuery<string>($"SELECT name AS \"Value\" FROM sections WHERE id = {comicsId}")
            .SingleAsync(cancellationToken));

        using var seededPayload = await ReadPayloadAsync(db, seededWork.Job, cancellationToken);
        using var comicsPayload = await ReadPayloadAsync(db, comicsWork.Job, cancellationToken);
        Assert.Equal(JsonValueKind.Null, seededPayload.RootElement.GetProperty("sectionId").ValueKind);
        Assert.Equal(comicsId, comicsPayload.RootElement.GetProperty("sectionId").GetGuid());
        Assert.False(seededPayload.RootElement.TryGetProperty("libraryId", out _));
        Assert.False(comicsPayload.RootElement.TryGetProperty("libraryId", out _));
    }

    /// <summary>Inserts a work in the library, with one item, one part and a succeeded ingest job that named it.</summary>
    private static async Task InsertWorkAsync(
        NookDbContext db,
        Guid libraryId,
        (Guid Work, Guid Item, Guid Part, Guid Job) ids,
        string title,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var path = $"Books/Someone/{title}/{title}.epub";
        var sha256 = new string('a', 64);
        var payload = JsonSerializer.Serialize(new { libraryId, stagedName = Guid.NewGuid().ToString(), fileName = "book.epub" });
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO works (id, library_id, title, sort_title, created_at, updated_at)
            VALUES ({ids.Work}, {libraryId}, {title}, {title}, {now}, {now});
            INSERT INTO items (id, work_id, format, created_at, updated_at)
            VALUES ({ids.Item}, {ids.Work}, 'Epub', {now}, {now});
            INSERT INTO parts (id, item_id, "order", path, media_type, size, sha256, created_at, updated_at)
            VALUES ({ids.Part}, {ids.Item}, 0, {path}, 'application/epub+zip', 1, {sha256}, {now}, {now});
            INSERT INTO jobs (id, type, status, payload, attempts_made, attempts_allowed, reclaims_made, created_at, updated_at)
            VALUES ({ids.Job}, 'ingest', 'Succeeded', CAST({payload} AS jsonb), 1, 3, 0, {now}, {now});
            """,
            cancellationToken);
    }

    private static Task<Guid?> ReadSectionIdAsync(NookDbContext db, Guid workId, CancellationToken cancellationToken) =>
        db.Database
            .SqlQuery<Guid?>($"SELECT section_id AS \"Value\" FROM works WHERE id = {workId}")
            .SingleAsync(cancellationToken);

    private static Task<Guid> ReadGuidAsync(
        NookDbContext db, FormattableString sql, CancellationToken cancellationToken) =>
        db.Database.SqlQuery<Guid>(sql).SingleAsync(cancellationToken);

    private static async Task<JsonDocument> ReadPayloadAsync(
        NookDbContext db, Guid jobId, CancellationToken cancellationToken) =>
        JsonDocument.Parse(await db.Database
            .SqlQuery<string>($"SELECT payload::text AS \"Value\" FROM jobs WHERE id = {jobId}")
            .SingleAsync(cancellationToken));
}
