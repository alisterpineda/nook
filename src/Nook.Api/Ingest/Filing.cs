using System.Buffers;
using System.Globalization;
using System.Text;
using Nook.Api.Data;
using Nook.Api.Storage;

namespace Nook.Api.Ingest;

/// <summary>
/// Where ingest files a book inside a library's folder. Paths here are <c>/</c>-separated and relative to the
/// library's folder, as a part stores them. Filing copies the staged file, which stays in staging. A path is taken
/// only when a part claims it, compared ignoring case and Unicode form, as a file system that folds either does. A
/// book file no part points at, such as one a run that died left behind, is overwritten, and a folder no part points
/// into is used as it is, with anything else in it left alone.
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
    /// A name made safe as one folder or file name on Windows, macOS and Linux: it is put in Unicode form C, illegal and control characters, and code points that are not characters, become
    /// <c>_</c>, spaces at either end and dots at the end go, the name is cut to 100 characters and 200 UTF-8 bytes
    /// without splitting a character, a Windows device name gets a leading <c>_</c>, and an empty result is
    /// <c>Untitled</c>.
    /// </summary>
    public static string Segment(string name)
    {
        // Half a surrogate pair and the code points Unicode sets aside as not characters go first, since no file
        // system needs them and normalising refuses them.
        var replaced = string.Create(name.Length, name, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                var paired = char.IsHighSurrogate(c)
                    ? i + 1 < source.Length && char.IsLowSurrogate(source[i + 1])
                    : !char.IsLowSurrogate(c) || (i > 0 && char.IsHighSurrogate(source[i - 1]));
                var notACharacter = c is >= '\uFDD0' and <= '\uFDEF' or '\uFFFE' or '\uFFFF';
                span[i] = !paired || notACharacter || char.IsControl(c) || IllegalChars.Contains(c) ? '_' : c;
            }
        });

        // One Unicode form for every name, so two spellings a file system treats as one name are one path here too.
        replaced = FormC(replaced);

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
    /// Copies the staged file into a folder no other work has, as <c>&lt;title&gt;&lt;extension&gt;</c> with the title
    /// made safe as a segment. The folder is the one given, or, when a part already points into it, the same name with
    /// <c> (2)</c>, then <c> (3)</c> and so on. A folder no part points into is used as it is.
    /// </summary>
    public static FiledBook CopyIntoNewFolder(StagedBook book, string folder)
    {
        var (stagedFile, _, _, library, storage, claimedPaths) = book;
        var fileName = Segment(book.Title) + book.Extension;
        return FirstFree(number =>
        {
            var candidate = Numbered(folder, number);
            var folderPath = storage.LibraryFile(library, candidate);

            // A plain file where the folder would go is not ingest's to replace.
            if (File.Exists(folderPath) || HoldsClaimedPath(claimedPaths, candidate))
            {
                return null;
            }

            var created = !Directory.Exists(folderPath);
            Directory.CreateDirectory(folderPath);
            var relativePath = $"{candidate}/{fileName}";
            var path = Path.Combine(folderPath, fileName);
            try
            {
                return TryCopy(stagedFile, path, IsClaimed(claimedPaths, relativePath))
                    ? new FiledBook(relativePath, path, created ? folderPath : null)
                    : null;
            }
            catch
            {
                // The folder this run made is not left behind empty, or a retry would land in the next number. A
                // folder that was already there is not this run's to remove.
                if (created)
                {
                    try
                    {
                        RemoveIfEmpty(folderPath);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        // The copy's own error is the one worth reporting.
                    }
                }

                throw;
            }
        });
    }

    /// <summary>
    /// Copies the staged file into the folder as <c>&lt;title&gt;&lt;extension&gt;</c> with the title made safe as a
    /// segment, or, when a part already points at that name, <c>&lt;title&gt; (2)&lt;extension&gt;</c>, then (3) and so
    /// on, beside what the folder already holds. A file at the name that no part points at is overwritten.
    /// </summary>
    public static FiledBook CopyBesideOthers(StagedBook book, string folder)
    {
        var (stagedFile, _, extension, library, storage, claimedPaths) = book;
        var title = Segment(book.Title);
        var folderPath = storage.LibrarySubfolder(library, folder);
        var created = folder.Length > 0 && !Directory.Exists(folderPath);
        Directory.CreateDirectory(folderPath);
        return FirstFree(number =>
        {
            var name = Numbered(title, number) + extension;
            var relativePath = folder.Length == 0 ? name : $"{folder}/{name}";
            var path = storage.LibraryFile(library, relativePath);
            return TryCopy(stagedFile, path, IsClaimed(claimedPaths, relativePath))
                ? new FiledBook(relativePath, path, created ? folderPath : null)
                : null;
        });
    }

    /// <summary>Whether a part points at the path, compared as a file system that folds names may: ignoring case and Unicode form.</summary>
    public static bool IsClaimed(IReadOnlyCollection<string> claimedPaths, string relativePath)
    {
        var path = FormC(relativePath);
        return claimedPaths.Any(claimed => string.Equals(FormC(claimed), path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whether the path lies inside the <c>/</c>-separated folder, at any depth, compared as <see cref="IsClaimed"/>
    /// does. Every path is inside the empty folder, which stands for the whole library.
    /// </summary>
    public static bool IsInside(string relativePath, string relativeFolder) =>
        relativeFolder.Length == 0
        || FormC(relativePath).StartsWith(FormC(relativeFolder) + "/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a part points at a path inside the folder, at any depth, compared as <see cref="IsClaimed"/> does.</summary>
    private static bool HoldsClaimedPath(IReadOnlyCollection<string> claimedPaths, string relativeFolder) =>
        claimedPaths.Any(claimed => IsInside(claimed, relativeFolder));

    /// <summary>
    /// The text in the Unicode form, or as it is when it holds something that has no such form, as a path stored
    /// before names were made safe for it can.
    /// </summary>
    public static string InForm(string text, NormalizationForm form)
    {
        try
        {
            return text.Normalize(form);
        }
        catch (ArgumentException)
        {
            return text;
        }
    }

    private static string FormC(string text) => InForm(text, NormalizationForm.FormC);

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

    /// <summary>
    /// Copies the staged file to the path unless a part claims it or a folder is there. A file no part points at is
    /// overwritten.
    /// </summary>
    /// <returns>False when the path is taken; a copy that fails throws its error.</returns>
    private static bool TryCopy(string stagedFile, string path, bool claimed)
    {
        if (claimed || Directory.Exists(path))
        {
            return false;
        }

        try
        {
            File.Copy(stagedFile, path, overwrite: true);
            return true;
        }
        catch (IOException)
        {
            // A failed copy can leave part of the file behind. No part points at the path and ingests file one at a
            // time, so a file there now is this copy's own or one nothing needs.
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The copy's own error is the one worth reporting.
            }

            throw;
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
    /// <param name="ClaimedPaths">
    /// The paths the library's parts point at, at least those inside the folder the book is filed into and its
    /// siblings. A path that exists on disk is taken only when one of these is it or lies inside it.
    /// </param>
    public sealed record StagedBook(
        string StagedFile,
        string Title,
        string Extension,
        Library Library,
        NookStorage Storage,
        IReadOnlyCollection<string> ClaimedPaths);

    /// <summary>A book copied into a library.</summary>
    /// <param name="RelativePath">Its path relative to the library's folder, as a part stores it.</param>
    /// <param name="FullPath">Its full path on disk.</param>
    /// <param name="CreatedFolder">The full path of the folder the copy created for it, or null when the folder was already there.</param>
    public sealed record FiledBook(string RelativePath, string FullPath, string? CreatedFolder);
}
