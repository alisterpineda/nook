using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nook.Api.Data;

namespace Nook.Api.Ingest;

/// <summary>
/// The one lock that ingests take turns under while they match a file to its rows and save them. It is global, not
/// per section, because people, series and tags are shared across sections.
/// </summary>
public static class IngestLock
{
    /// <summary>The advisory lock's key, the same for every ingest.</summary>
    public const long Key = 0x4E6F6F6B_496E6773; // "NookIngs"

    /// <summary>How long a writer whose save failed waits for the lock before it leaves its files in place.</summary>
    private static readonly TimeSpan WaitAfterFailedSave = TimeSpan.FromSeconds(30);

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

    /// <summary>
    /// For a writer whose save or commit threw after it put files in the library folder. The save may still have
    /// landed, and a connection that broke has already let go of the lock, so the lock is given up for good by ending
    /// the transaction, and taken again in a transaction on a context of its own, under which
    /// <paramref name="underLock"/> runs with that context. The writer is already failing, so it does not wait for the
    /// lock without end as one that is filing does: when the wait runs out this throws, as it does when the database
    /// cannot be asked.
    /// </summary>
    public static async Task RetakeAfterFailedSaveAsync(
        IDbContextTransaction transaction,
        IServiceScopeFactory scopes,
        ILogger logger,
        Func<NookDbContext, Task> underLock)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            // A transaction that has ended, by a commit that landed or a lost connection, holds no lock.
            logger.LogDebug(exception, "The transaction of the failed save had already ended.");
        }

        await using var scope = scopes.CreateAsyncScope();
        var fresh = scope.ServiceProvider.GetRequiredService<NookDbContext>();
        await fresh.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var check = await fresh.Database.BeginTransactionAsync(CancellationToken.None);
            using var waited = new CancellationTokenSource(WaitAfterFailedSave);
            await TakeAsync(fresh, waited.Token);
            await underLock(fresh);
        });
    }
}
