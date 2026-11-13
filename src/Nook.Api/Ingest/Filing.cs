using System.Buffers;
using System.Globalization;
using System.Text;
using Nook.Api.Data;
using Nook.Api.Storage;

namespace Nook.Api.Ingest;

/// <summary>
/// Where ingest files a book inside a library's folder. Paths here are <c>/</c>-separated and relative to the
/// library's folder, as a part stores them, and filing never replaces or moves anything already on disk.
/// </summary>
public static class Filing
{
    /// <summary>The author folder of a book whose file names no author.</summary>
    public const string UnknownAuthor = "Unknown Author";

    /// <summary>The name a segment takes when nothing usable is left of it.</summary>
    public const string Untitled = "Untitled";

    private const int MaxSegmentChars = 100;
    private const int MaxSegmentBytes = 200;

    // Characters Windows forbids in a name; macOS and Linux forbid a subset of these. Control characters are
    // forbidden too and are checked apart.
    private static readonly SearchValues<char> IllegalChars = SearchValues.Create("<>:\"/\\|?*");

    // Device names Windows reserves whatever extension follows them.
    private static readonly HashSet<string> ReservedNames = new(
        [
            "CON", "PRN", "AUX", "NUL",
            "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
            "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
        ],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The folder a work's files go in: <c>Books/&lt;author&gt;/&lt;title&gt;</c>, with the author and title made safe
    /// as segments.
    /// </summary>
    public static string WorkFolder(string? author, string title) =>
        $"Books/{Segment(author ?? UnknownAuthor)}/{Segment(title)}";

    /// <summary>
    /// A name made safe as one folder or file name on Windows, macOS and Linux: illegal and control characters become
    /// <c>_</c>, spaces at either end and dots at the end go, the name is cut to 100 characters and 200 UTF-8 bytes
    /// without splitting a character, a Windows device name gets a leading <c>_</c>, and an empty result is
    /// <c>Untitled</c>.
    /// </summary>
    public static string Segment(string name)
    {
        var replaced = string.Create(name.Length, name, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                span[i] = char.IsControl(c) || IllegalChars.Contains(c) ? '_' : c;
            }
        });

        var segment = Trim(Cut(Trim(replaced), MaxSegmentChars, MaxSegmentBytes));
        var dot = segment.IndexOf('.');
        var stem = (dot < 0 ? segment : segment[..dot]).TrimEnd(' ');
        if (ReservedNames.Contains(stem))
        {
            // The prefix can push the name one over the cap, so it is cut and trimmed once more.
            segment = Trim(Cut("_" + segment, MaxSegmentChars, MaxSegmentBytes));
        }

        return segment.Length == 0 ? Untitled : segment;
    }

    private static string Trim(string name) => name.TrimStart(' ').TrimEnd(' ', '.');

    /// <summary>The longest run of whole characters from the start of the name that fits both limits.</summary>
    private static string Cut(string name, int maxChars, int maxBytes)
    {
        if (name.Length <= maxChars && Encoding.UTF8.GetByteCount(name) <= maxBytes)
        {
            return name;
        }

        var chars = 0;
        var bytes = 0;
        var elements = StringInfo.GetTextElementEnumerator(name);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            var elementBytes = Encoding.UTF8.GetByteCount(element);
            if (chars + element.Length > maxChars || bytes + elementBytes > maxBytes)
            {
                break;
            }

            chars += element.Length;
            bytes += elementBytes;
        }

        return name[..chars];
    }

    /// <summary>The folder that holds a filed path, or an empty string for a path at the top of the library.</summary>
    public static string FolderOf(string relativePath)
    {
        var slash = relativePath.LastIndexOf('/');
        return slash < 0 ? "" : relativePath[..slash];
    }

    /// <summary>
    /// Moves the staged file into a folder no other work has, as <c>&lt;title&gt;&lt;extension&gt;</c> with the title
    /// made safe as a segment. The folder is the one given, or, when that is already there, the same name with
    /// <c> (2)</c>, then <c> (3)</c> and so on.
    /// </summary>
    public static FiledBook MoveIntoNewFolder(StagedBook book, string folder)
    {
        var (stagedFile, _, _, library, storage) = book;
        var fileName = Segment(book.Title) + book.Extension;
        return FirstFree(number =>
        {
            var candidate = Numbered(folder, number);
            var folderPath = storage.LibraryFile(library, candidate);
            if (Directory.Exists(folderPath) || File.Exists(folderPath))
            {
                return null;
            }

            Directory.CreateDirectory(folderPath);
            var path = Path.Combine(folderPath, fileName);
            try
            {
                // A failed move here means another run took this folder between the check and the move.
                return TryMove(stagedFile, path) ? new FiledBook($"{candidate}/{fileName}", path, folderPath) : null;
            }
            catch
            {
                // The folder this run made is not left behind empty, or a retry would land in the next number.
                try
                {
                    RemoveIfEmpty(folderPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // The move's own error is the one worth reporting.
                }

                throw;
            }
        });
    }

    /// <summary>
    /// Moves the staged file into the folder as <c>&lt;title&gt;&lt;extension&gt;</c> with the title made safe as a
    /// segment, or, when that name is taken, <c>&lt;title&gt; (2)&lt;extension&gt;</c>, then (3) and so on, beside what
    /// the folder already holds.
    /// </summary>
    public static FiledBook MoveBesideOthers(StagedBook book, string folder)
    {
        var (stagedFile, _, extension, library, storage) = book;
        var title = Segment(book.Title);
        var folderPath = storage.LibrarySubfolder(library, folder);
        var created = folder.Length > 0 && !Directory.Exists(folderPath);
        Directory.CreateDirectory(folderPath);
        return FirstFree(number =>
        {
            var name = Numbered(title, number) + extension;
            var relativePath = folder.Length == 0 ? name : $"{folder}/{name}";
            var path = storage.LibraryFile(library, relativePath);
            return TryMove(stagedFile, path) ? new FiledBook(relativePath, path, created ? folderPath : null) : null;
        });
    }

    /// <summary>The first result an attempt gives, trying the numbers from 1 up until one is free.</summary>
    private static FiledBook FirstFree(Func<int, FiledBook?> attempt)
    {
        for (var number = 1; ; number++)
        {
            if (attempt(number) is { } filed)
            {
                return filed;
            }
        }
    }

    /// <summary>The name itself for number 1, and after that the name with <c> (2)</c>, <c> (3)</c> and so on.</summary>
    private static string Numbered(string name, int number) => number == 1 ? name : $"{name} ({number})";

    /// <summary>Moves the staged file to the path unless something is already there, which is never replaced.</summary>
    /// <returns>False when the path is taken; any other failure throws.</returns>
    private static bool TryMove(string stagedFile, string path)
    {
        try
        {
            File.Move(stagedFile, path);
            return true;
        }
        catch (IOException) when (File.Exists(path) || Directory.Exists(path))
        {
            return false;
        }
    }

    /// <summary>Deletes the folder when it is there and holds nothing; a folder with anything in it is left alone.</summary>
    public static void RemoveIfEmpty(string folderPath)
    {
        if (Directory.Exists(folderPath) && !Directory.EnumerateFileSystemEntries(folderPath).Any())
        {
            Directory.Delete(folderPath);
        }
    }

    /// <summary>A staged file on its way into a library.</summary>
    /// <param name="StagedFile">The full path of the file in staging.</param>
    /// <param name="Title">The title the file is named after, made safe as a segment when it is filed.</param>
    /// <param name="Extension">The file's extension, with its dot.</param>
    /// <param name="Library">The library the file goes into.</param>
    /// <param name="Storage">Where the library's folder is.</param>
    public sealed record StagedBook(string StagedFile, string Title, string Extension, Library Library, NookStorage Storage);

    /// <summary>A book moved into a library.</summary>
    /// <param name="RelativePath">Its path relative to the library's folder, as a part stores it.</param>
    /// <param name="FullPath">Its full path on disk.</param>
    /// <param name="CreatedFolder">The full path of the folder the move created for it, or null when the folder was already there.</param>
    public sealed record FiledBook(string RelativePath, string FullPath, string? CreatedFolder);
}
