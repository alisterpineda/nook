using Microsoft.EntityFrameworkCore;

namespace Nook.Api.Data;

/// <summary>Refuses to start Nook over a database that a newer build has migrated.</summary>
public static class NewerDatabase
{
    /// <summary>
    /// Throws when the database's migration history holds a migration this build does not define. Nothing is
    /// written to the database.
    /// </summary>
    public static async Task EnsureNoneAsync(NookDbContext db, CancellationToken cancellationToken)
    {
        var unknown = (await db.Database.GetAppliedMigrationsAsync(cancellationToken))
            .Except(db.Database.GetMigrations())
            .ToList();

        if (unknown.Count > 0)
        {
            throw new InvalidOperationException(
                $"This database was migrated by a newer Nook: it holds migrations this build does not define ({string.Join(", ", unknown)}). "
                + "Start the newer build, or restore the backup made before the upgrade.");
        }
    }
}
