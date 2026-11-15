using System.Collections.Concurrent;
using Nook.Api.Ingest;
using Nook.Api.Ingest.Epub;

namespace Nook.Api.Features.Publications;

/// <summary>
/// Keeps the description of the books read most recently, so a book is not parsed again for each of its requests. An
/// entry is kept by the file's path, size and modified time, so a file that changes is read again. Only the parsed
/// description is kept; no book stays open.
/// </summary>
public sealed class PublicationCache
{
    /// <summary>How many books are kept at once. Past it, the others are let go to make room.</summary>
    public const int Capacity = 16;

    private readonly ConcurrentDictionary<string, Entry> entries = new(StringComparer.Ordinal);

    private sealed record Entry(long Size, DateTime LastWriteUtc, Lazy<Task<EpubPublication>> Publication);

    /// <summary>The publication in the EPUB at <paramref name="path"/>, read when it is not kept or has changed.</summary>
    /// <exception cref="FileNotFoundException">There is no file at the path.</exception>
    /// <exception cref="UnreadableBookException">The book is one ingest would refuse.</exception>
    public async Task<EpubPublication> GetAsync(string path, CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException("The book's file is missing.", path);
        }

        var (size, lastWriteUtc) = (file.Length, file.LastWriteTimeUtc);
        var entry = entries.AddOrUpdate(
            path,
            _ => NewEntry(path, size, lastWriteUtc),
            (_, kept) => kept.Size == size && kept.LastWriteUtc == lastWriteUtc
                ? kept
                : NewEntry(path, size, lastWriteUtc));

        if (entries.Count > Capacity)
        {
            foreach (var key in entries.Keys.Where(key => key != path).Take(entries.Count - Capacity))
            {
                entries.TryRemove(key, out _);
            }
        }

        try
        {
            return await entry.Publication.Value.WaitAsync(cancellationToken);
        }
        catch (Exception) when (entry.Publication.Value.IsFaulted)
        {
            // A failure is not kept, so the next request tries again.
            entries.TryRemove(new KeyValuePair<string, Entry>(path, entry));
            throw;
        }
    }

    /// <summary>
    /// The read runs apart from the request that started it, since other requests may wait on it too; one request
    /// giving up does not cancel it for the rest.
    /// </summary>
    private static Entry NewEntry(string path, long size, DateTime lastWriteUtc) =>
        new(size, lastWriteUtc, new Lazy<Task<EpubPublication>>(
            () => Task.Run(() => EpubPublicationReader.ReadAsync(path, CancellationToken.None)),
            LazyThreadSafetyMode.ExecutionAndPublication));
}
