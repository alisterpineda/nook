using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;

namespace Nook.Api.Ingest;

/// <summary>Finds people, series and tags by name key, adding the ones not stored yet.</summary>
public static class NamedRows
{
    /// <summary>
    /// The row for each of the names, by name key: the oldest row whose stored name key is that key, or a new one,
    /// made from the first of the names with the key and added unsaved.
    /// </summary>
    public static async Task<Dictionary<string, T>> FindOrAddAsync<T>(
        DbSet<T> rows, IEnumerable<string> names, Func<string, T> create, CancellationToken cancellationToken)
        where T : Entity, INamed
    {
        var firstNames = new Dictionary<string, string>();
        foreach (var name in names)
        {
            firstNames.TryAdd(NameKey.Of(name), name);
        }

        var keys = firstNames.Keys.ToList();
        var candidates = keys.Count == 0
            ? []
            : await rows
                .Where(row => keys.Contains(row.NameKey))
                .ToListAsync(cancellationToken);
        var byKey = candidates
            .GroupBy(row => row.NameKey)
            .ToDictionary(group => group.Key, group => group.MinBy(row => (row.CreatedAt, row.Id))!);
        foreach (var (key, name) in firstNames)
        {
            if (!byKey.ContainsKey(key))
            {
                var row = create(name);
                rows.Add(row);
                byKey[key] = row;
            }
        }

        return byKey;
    }
}
