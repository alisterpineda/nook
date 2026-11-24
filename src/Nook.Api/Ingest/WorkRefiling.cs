using Nook.Api.Storage;

namespace Nook.Api.Ingest;

/// <summary>
/// Where an existing work's files go when its title or first author changes, and the moving itself. Paths are
/// <c>/</c>-separated and relative to the library folder, as <see cref="Filing"/> and a part store them. Files are
/// moved, never rewritten, so a book's bytes and hash stay as they were. A file at a destination that no part points at
/// is overwritten, as ingest overwrites one.
/// </summary>
public static class WorkRefiling
{
    private const string BooksFolder = "Books";

    /// <summary>One file's move, from its path to its new path, both relative to the library folder.</summary>
    public sealed record FileMove(string FromRelative, string ToRelative);

    /// <summary>
    /// The moves that bring every part and the cover into one folder. The folder is the target, or, when another work's
    /// path is inside it or a plain file is there, the same name with <c> (2)</c>, then <c> (3)</c> and so on. The
    /// first part with an extension is named for the title, and a later one with the same extension takes
    /// <c> (2)</c>, <c> (3)</c> before the extension. A name where another of the work's own files is now is passed
    /// over for the next number, so no move lands on a file that has yet to move. The cover is <c>cover</c> with its
    /// extension. A file already at its destination is left out.
    /// </summary>
    /// <param name="targetFolder">The folder the title and first author make, relative to the library folder.</param>
    /// <param name="title">The work's new title.</param>
    /// <param name="partPaths">The paths of the work's parts, in the order they are named.</param>
    /// <param name="coverPath">The path of the work's cover, or null when it has none.</param>
    /// <param name="claimedByOthers">The paths other works' parts and covers point at, at least those near the target.</param>
    /// <param name="storage">Where the library folder is.</param>
    public static IReadOnlyList<FileMove> Plan(
        string targetFolder,
        string title,
        IReadOnlyList<string> partPaths,
        string? coverPath,
        IReadOnlyCollection<string> claimedByOthers,
        NookStorage storage)
    {
        var folder = FirstFreeFolder(targetFolder, claimedByOthers, storage);
        var stem = Filing.Segment(title);
        var given = new List<string>();
        var moves = new List<FileMove>();

        for (var index = 0; index < partPaths.Count; index++)
        {
            var path = partPaths[index];
            var extension = Path.GetExtension(path);
            var others = partPaths.Where((_, other) => other != index).ToList();
            for (var number = 1; ; number++)
            {
                var destination = Filing.Join(folder, Filing.Numbered(stem, number) + extension);
                if (Filing.IsClaimed(given, destination) || Filing.IsClaimed(others, destination))
                {
                    continue;
                }

                given.Add(destination);
                Add(moves, path, destination);
                break;
            }
        }

        if (coverPath is not null)
        {
            Add(moves, coverPath, Filing.Join(folder, "cover" + Path.GetExtension(coverPath)));
        }

        return moves;
    }

    private static void Add(List<FileMove> moves, string from, string to)
    {
        if (!string.Equals(from, to, StringComparison.Ordinal))
        {
            moves.Add(new FileMove(from, to));
        }
    }

    private static string FirstFreeFolder(string targetFolder, IReadOnlyCollection<string> claimedByOthers, NookStorage storage)
    {
        for (var number = 1; ; number++)
        {
            var candidate = Filing.Numbered(targetFolder, number);
            if (!Filing.IsFolderTaken(candidate, claimedByOthers, storage))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Creates the destination folders and moves each file. When one cannot be moved, the ones already moved go back,
    /// a folder this call made is removed if it is empty, and the error is rethrown.
    /// </summary>
    public static void Apply(IReadOnlyList<FileMove> moves, NookStorage storage)
    {
        var done = new List<FileMove>();
        var created = new List<string>();
        try
        {
            foreach (var move in moves)
            {
                var folder = storage.LibrarySubfolder(Filing.FolderOf(move.ToRelative));
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                    created.Add(folder);
                }

                File.Move(storage.LibraryFile(move.FromRelative), storage.LibraryFile(move.ToRelative), overwrite: true);
                done.Add(move);
            }
        }
        catch
        {
            try
            {
                Undo(done, storage);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or AggregateException)
            {
                // The move's own error is the one worth reporting.
            }

            foreach (var folder in created)
            {
                try
                {
                    Filing.RemoveIfEmpty(folder);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Nothing more to do for a folder that cannot be removed.
                }
            }

            throw;
        }
    }

    /// <summary>
    /// The moves whose destination no part points at, compared as <see cref="Filing.IsClaimed"/> compares. They are
    /// the ones safe to undo once other writers have had a turn: a destination a part claims holds that part's file.
    /// </summary>
    public static IReadOnlyList<FileMove> NotClaimed(IReadOnlyList<FileMove> moves, IReadOnlyCollection<string> claimedPaths) =>
        [.. moves.Where(move => !Filing.IsClaimed(claimedPaths, move.ToRelative))];

    /// <summary>
    /// Moves every file back to where it came from, recreating its folder when that is gone. Each file is tried
    /// whatever happens to the others.
    /// </summary>
    /// <exception cref="AggregateException">One or more files could not be moved back.</exception>
    public static void Undo(IReadOnlyList<FileMove> moves, NookStorage storage)
    {
        var failures = new List<Exception>();
        foreach (var move in moves.Reverse())
        {
            try
            {
                Directory.CreateDirectory(storage.LibrarySubfolder(Filing.FolderOf(move.FromRelative)));
                File.Move(storage.LibraryFile(move.ToRelative), storage.LibraryFile(move.FromRelative), overwrite: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failures.Add(exception);
            }
        }

        if (failures.Count > 0)
        {
            throw new AggregateException(failures);
        }
    }

    /// <summary>
    /// Removes each folder a file left when it is empty, then that folder's parent when it is empty too. The library
    /// folder and <c>Books</c> stay.
    /// </summary>
    public static void RemoveEmptied(IReadOnlyList<FileMove> moves, NookStorage storage)
    {
        var folders = moves.Select(move => Filing.FolderOf(move.FromRelative)).Distinct(StringComparer.Ordinal);
        foreach (var folder in folders)
        {
            RemoveIfEmptyBelowBooks(folder, storage);
            RemoveIfEmptyBelowBooks(Filing.FolderOf(folder), storage);
        }
    }

    private static void RemoveIfEmptyBelowBooks(string folder, NookStorage storage)
    {
        if (folder.Length == 0 || string.Equals(folder, BooksFolder, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Filing.RemoveIfEmpty(storage.LibrarySubfolder(folder));
    }
}
