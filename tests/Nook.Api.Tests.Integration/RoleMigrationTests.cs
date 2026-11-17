using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nook.Api.Data;

namespace Nook.Api.Tests.Integration;

/// <summary>Runs the migration that moves roles into a table over a database that already holds users.</summary>
public class RoleMigrationTests(NookApiFactory factory) : IClassFixture<NookApiFactory>
{
    private const string MigrationBeforeRoles = "20261116015701_ReplaceLibrariesWithSections";

    [Fact]
    public async Task Upgrade_gives_each_user_the_role_their_old_role_names()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var admin = Guid.NewGuid();
        var member = Guid.NewGuid();
        var guest = Guid.NewGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();

        await db.Database.MigrateAsync(MigrationBeforeRoles, cancellationToken);
        await InsertUserAsync(db, admin, "Admin", cancellationToken);
        await InsertUserAsync(db, member, "Member", cancellationToken);
        await InsertUserAsync(db, guest, "Guest", cancellationToken);

        await db.Database.MigrateAsync(cancellationToken);

        var roles = await db.Database
            .SqlQuery<string>($"SELECT (id::text || ' ' || name || ' ' || built_in) AS \"Value\" FROM roles")
            .ToListAsync(cancellationToken);
        string[] expected =
        [
            $"{Role.AdminId} Admin Admin",
            $"{Role.ContributorId} Contributor Contributor",
            $"{Role.ViewerId} Viewer Viewer",
            $"{Role.AnonymousId} Anonymous Anonymous",
        ];
        Assert.Equal(expected.Order(), roles.Order());

        Assert.Equal(Role.AdminId, await ReadRoleIdAsync(db, admin, cancellationToken));
        Assert.Equal(Role.ContributorId, await ReadRoleIdAsync(db, member, cancellationToken));
        Assert.Equal(Role.ViewerId, await ReadRoleIdAsync(db, guest, cancellationToken));
    }

    /// <summary>Inserts a user with the role stored as text, as it was before roles were a table.</summary>
    private static async Task InsertUserAsync(NookDbContext db, Guid id, string role, CancellationToken cancellationToken)
    {
        var name = $"{id:N}@example.test";
        var now = DateTimeOffset.UtcNow;
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO asp_net_users (id, role, created_at, updated_at, user_name, email, email_confirmed,
                phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count)
            VALUES ({id}, {role}, {now}, {now}, {name}, {name}, true, false, false, true, 0)
            """,
            cancellationToken);
    }

    private static Task<Guid> ReadRoleIdAsync(NookDbContext db, Guid userId, CancellationToken cancellationToken) =>
        db.Database
            .SqlQuery<Guid>($"SELECT role_id AS \"Value\" FROM asp_net_users WHERE id = {userId}")
            .SingleAsync(cancellationToken);
}
