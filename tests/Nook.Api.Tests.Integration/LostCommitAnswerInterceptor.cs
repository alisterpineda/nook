using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Nook.Api.Tests.Integration;

/// <summary>
/// Throws an error the database's retry strategy counts as transient once a transaction has committed, as a commit
/// whose answer is lost does. It does so for the transactions whose context holds a row the test marked, picked by
/// the function given, and once only for each such row's id.
/// </summary>
internal sealed class LostCommitAnswerInterceptor(Func<DbContext, Guid?> markedRowId) : DbTransactionInterceptor
{
    private readonly ConcurrentDictionary<Guid, bool> _lostOnce = new();

    public override Task TransactionCommittedAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context && markedRowId(context) is { } id && _lostOnce.TryAdd(id, true))
        {
            throw new TimeoutException("The commit timed out, as the test asked.");
        }

        return base.TransactionCommittedAsync(transaction, eventData, cancellationToken);
    }
}
