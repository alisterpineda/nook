using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;

namespace Nook.Api.Tests.Integration;

/// <summary>Runs the migration that adds work owners over a database that already holds works.</summary>
public class WorkOwnerMigrationTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    private const string MigrationBeforeWorkOwners = "20261120201031_LetContributorsUpload";

    [Fact]
    public async Task Work_that_existed_before_owners_is_owned_by_whoever_first_added_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var gone = Guid.NewGuid();
        var addedTwice = Guid.NewGuid();
        var addedByRemovedUser = Guid.NewGuid();
        var neverAdded = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var earlier = now.AddHours(-2);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();

        await db.Database.MigrateAsync(MigrationBeforeWorkOwners, cancellationToken);
        foreach (var (id, name) in new[] { (first, "first"), (second, "second") })
        {
            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO asp_net_users (id, user_name, email, role_id, access_failed_count, email_confirmed, lockout_enabled, phone_number_confirmed, two_factor_enabled, created_at, updated_at)
                VALUES ({id}, {name}, {name + "@example.test"}, '01a1c7e7-8000-7000-8000-0000000000a3', 0, false, false, false, false, {now}, {now})
                """,
                cancellationToken);
        }

        foreach (var (id, title) in new[] { (addedTwice, "A"), (addedByRemovedUser, "B"), (neverAdded, "C") })
        {
            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO works (id, title, sort_title, created_at, updated_at)
                VALUES ({id}, {title}, {title}, {now}, {now})
                """,
                cancellationToken);
        }

        // The later entry is written first so that only the entry's time, not insertion order, puts the first user first.
        foreach (var (actor, workId, at) in new[] { (second, addedTwice, now), (first, addedTwice, earlier), (gone, addedByRemovedUser, earlier) })
        {
            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO audit_entries (id, actor_id, actor_name, actor_email, action, subject_type, subject_id, subject_name, details, created_at, updated_at)
                VALUES ({Guid.CreateVersion7()}, {actor}, 'x', 'x@example.test', 'upload.added', 'work', {workId}, 'x', '[]', {at}, {at})
                """,
                cancellationToken);
        }

        await db.Database.MigrateAsync(cancellationToken);

        async Task<Guid?> OwnerOf(Guid workId) => await db.Database
            .SqlQuery<Guid?>($"SELECT owner_id AS \"Value\" FROM works WHERE id = {workId}")
            .SingleAsync(cancellationToken);

        Assert.Equal(first, await OwnerOf(addedTwice));
        Assert.Null(await OwnerOf(addedByRemovedUser));
        Assert.Null(await OwnerOf(neverAdded));
    }
}
