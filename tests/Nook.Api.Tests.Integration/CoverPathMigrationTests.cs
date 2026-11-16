using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;

namespace Nook.Api.Tests.Integration;

/// <summary>Runs the migration that adds cover paths over a database that already holds works.</summary>
public class CoverPathMigrationTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    private const string MigrationBeforeCoverPaths = "20261113144148_AddNameKeys";

    /// <summary>The library row the first migration seeded, which every work belonged to before sections.</summary>
    private static readonly Guid SeededLibraryId = new("01a1c7e7-8000-7000-8000-000000000001");

    [Fact]
    public async Task Work_that_existed_before_cover_paths_loses_its_cover_hash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();

        await db.Database.MigrateAsync(MigrationBeforeCoverPaths, cancellationToken);
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO works (id, library_id, title, sort_title, cover_hash, created_at, updated_at)
            VALUES ({workId}, {SeededLibraryId}, 'The Dispossessed', 'Dispossessed, The', 'a1b2c3', {now}, {now})
            """,
            cancellationToken);

        await db.Database.MigrateAsync(cancellationToken);

        var coverHash = await db.Database
            .SqlQuery<string?>($"SELECT cover_hash AS \"Value\" FROM works WHERE id = {workId}")
            .SingleAsync(cancellationToken);
        var coverPath = await db.Database
            .SqlQuery<string?>($"SELECT cover_path AS \"Value\" FROM works WHERE id = {workId}")
            .SingleAsync(cancellationToken);

        Assert.Null(coverHash);
        Assert.Null(coverPath);
    }
}
