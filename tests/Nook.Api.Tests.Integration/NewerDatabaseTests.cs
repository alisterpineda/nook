using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;

namespace Nook.Api.Tests.Integration;

/// <summary>Starts the API over a database that a newer build has migrated.</summary>
public class NewerDatabaseTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    private const string NewerMigration = "29990101000000_AddSomethingNewer";

    [Fact]
    public async Task Starting_over_a_database_with_a_migration_this_build_does_not_define_fails_and_leaves_the_database_as_it_was()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // The first start migrates the database; then a newer build's migration is added to its history.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            await db.Database.ExecuteSqlAsync(
                $"INSERT INTO \"__EFMigrationsHistory\" (migration_id, product_version) VALUES ({NewerMigration}, '99.0.0')",
                cancellationToken);
        }

        var before = await ReadHistoryAsync(cancellationToken);

        var error = Assert.Throws<InvalidOperationException>(() => factory.WithWebHostBuilder(_ => { }).CreateClient());

        Assert.Contains(NewerMigration, error.Message);
        Assert.Contains("This database was migrated by a newer Nook", error.Message);
        Assert.Equal(before, await ReadHistoryAsync(cancellationToken));
    }

    private async Task<List<string>> ReadHistoryAsync(CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        return await db.Database
            .SqlQuery<string>($"SELECT migration_id AS \"Value\" FROM \"__EFMigrationsHistory\" ORDER BY migration_id")
            .ToListAsync(cancellationToken);
    }
}
