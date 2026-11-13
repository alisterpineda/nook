using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;

namespace Nook.Api.Ingest;

/// <summary>
/// The one lock that ingests take turns under while they match a file to its rows and save them. It is global, not
/// per library, because people, series and tags are shared across libraries.
/// </summary>
public static class IngestLock
{
    /// <summary>The advisory lock's key, the same for every ingest.</summary>
    public const long Key = 0x4E6F6F6B_496E6773; // "NookIngs"

    /// <summary>
    /// Waits for the lock and holds it to the end of the context's current transaction, which releases it on commit,
    /// rollback or a dropped connection. Call it only inside a transaction. The wait has no time limit of its own,
    /// since the ingest ahead may hold the lock while it copies a large file; cancelling the token ends it.
    /// </summary>
    public static async Task TakeAsync(NookDbContext db, CancellationToken cancellationToken)
    {
        var commandTimeout = db.Database.GetCommandTimeout();
        db.Database.SetCommandTimeout(0);
        try
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({Key})", cancellationToken);
        }
        finally
        {
            db.Database.SetCommandTimeout(commandTimeout);
        }
    }
}
