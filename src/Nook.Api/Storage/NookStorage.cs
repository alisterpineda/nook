using Microsoft.Extensions.Options;

namespace Nook.Api.Storage;

/// <summary>Nook's four folders: the library, which holds every book file and cover, the cache, which holds files that can be made again from the library, <c>staging/</c> in the data root, which holds uploads waiting to be ingested, and <c>keys/</c> in the data root, which holds the app's secrets, with the keys that protect sign-in cookies in <c>keys/data-protection/</c>. The library and the cache are <c>library/</c> and <c>cache/</c> in the data root unless their own settings place them elsewhere.</summary>
public sealed partial class NookStorage(IOptions<StorageOptions> options)
{
    /// <summary>The buffer files are copied through, the size <see cref="Stream.CopyToAsync(Stream)"/> uses.</summary>
    private const int CopyBufferSize = 81920;

    private const string DataPathSetting = $"{StorageOptions.Section}:{nameof(StorageOptions.DataPath)}";
    private const string LibraryPathSetting = $"{StorageOptions.Section}:{nameof(StorageOptions.LibraryPath)}";
    private const string CachePathSetting = $"{StorageOptions.Section}:{nameof(StorageOptions.CachePath)}";

    private string Root => FullPathOf(options.Value.DataPath);

    /// <summary>A setting's folder as a full path with no separator at its end, so <c>/books/</c> and <c>/books</c> are one folder.</summary>
    private static string FullPathOf(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private bool HasOwnLibraryPath => !string.IsNullOrWhiteSpace(options.Value.LibraryPath);

    private bool HasOwnCachePath => !string.IsNullOrWhiteSpace(options.Value.CachePath);

    /// <summary>Where uploaded files wait until their ingest finishes.</summary>
    public string StagingFolder => Path.Combine(Root, "staging");

    /// <summary>The path of one staged file, from its name inside the staging folder.</summary>
    public string StagedFile(string name) => Path.Combine(StagingFolder, name);

    /// <summary>The one folder that holds every book file and cover, whatever section its work is in.</summary>
    public string LibraryFolder =>
        HasOwnLibraryPath ? FullPathOf(options.Value.LibraryPath!) : Path.Combine(Root, "library");

    /// <summary>The folder that holds files that can be made again from the library.</summary>
    public string CacheFolder =>
        HasOwnCachePath ? FullPathOf(options.Value.CachePath!) : Path.Combine(Root, "cache");

    /// <summary>Where resized covers are kept. Everything in it can be made again from the covers in the library.</summary>
    public string CoversCacheFolder => Path.Combine(CacheFolder, "covers");

    /// <summary>Where the app's own secrets are kept, such as the key that signs publication tokens.</summary>
    public string KeysFolder => Path.Combine(Root, "keys");

    /// <summary>Where the keys that protect sign-in cookies are kept, so a sign-in outlasts a restart or an upgrade.</summary>
    public string DataProtectionKeysFolder => Path.Combine(KeysFolder, "data-protection");

    /// <summary>The full path of a file in the library folder, from its <c>/</c>-separated path relative to that folder, as a part stores it.</summary>
    /// <exception cref="InvalidOperationException">The path leads outside the library folder.</exception>
    public string LibraryFile(string relativePath)
    {
        var folder = Path.GetFullPath(LibraryFolder);
        var path = Path.GetFullPath(Path.Combine([folder, .. relativePath.Split('/')]));

        // A path built from a book's own words can hold ".." or a rooted segment, and must not reach other folders.
        if (!IsInside(path, folder))
        {
            throw new InvalidOperationException($"The path '{relativePath}' leads outside the library folder.");
        }

        return path;
    }

    /// <summary>The full path of a folder in the library folder, from its <c>/</c>-separated path relative to that folder. An empty path is the library folder itself.</summary>
    /// <exception cref="InvalidOperationException">The path leads outside the library folder.</exception>
    public string LibrarySubfolder(string relativeFolder) =>
        relativeFolder.Length == 0 ? Path.GetFullPath(LibraryFolder) : LibraryFile(relativeFolder);

    /// <summary>Opens a new file for writing. It fails when the file is already there, so nothing is overwritten.</summary>
    public static FileStream CreateFile(string path) =>
        new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferSize, useAsync: true);

    /// <summary>Opens a file for writing, emptying any file already there.</summary>
    public static FileStream ReplaceFile(string path) =>
        new(path, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize, useAsync: true);

    /// <summary>Opens a file for reading.</summary>
    public static FileStream OpenFile(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, useAsync: true);

    /// <summary>The library, cache, staging and keys folders, each with the setting that places it.</summary>
    public IReadOnlyList<StorageFolder> Folders =>
    [
        new("library", LibraryFolder, HasOwnLibraryPath ? LibraryPathSetting : DataPathSetting),
        new("cache", CacheFolder, HasOwnCachePath ? CachePathSetting : DataPathSetting),
        new("staging", StagingFolder, DataPathSetting),
        new("keys", KeysFolder, DataPathSetting),
    ];

    /// <summary>A message for every two folders that are the same folder or where one is inside the other. It is empty when every folder is apart from the others.</summary>
    public IReadOnlyList<string> FindOverlappingFolders()
    {
        var folders = Folders;
        var problems = new List<string>();
        for (var i = 0; i < folders.Count; i++)
        {
            for (var j = i + 1; j < folders.Count; j++)
            {
                var (first, second) = (folders[i], folders[j]);
                if (IsSameFolder(first.Path, second.Path))
                {
                    problems.Add($"The {first.Name} folder, placed by {first.Setting}, and the {second.Name} folder, placed by {second.Setting}, are the same folder, '{first.Path}'. Give each of them a folder of its own.");
                }
                else if (IsInside(first.Path, second.Path) || IsInside(second.Path, first.Path))
                {
                    var (inner, outer) = IsInside(first.Path, second.Path) ? (first, second) : (second, first);
                    problems.Add($"The {inner.Name} folder at '{inner.Path}', placed by {inner.Setting}, is inside the {outer.Name} folder at '{outer.Path}', placed by {outer.Setting}. Give each of them a folder of its own, with neither inside the other.");
                }
            }
        }

        return problems;
    }

    /// <summary>Creates the staging, library, cache, cover cache and keys folders when they are missing. Reading the data root throws when it is not set.</summary>
    /// <exception cref="InvalidOperationException">A folder cannot be created. The message names the folder and the setting that places it.</exception>
    public void CreateFolders()
    {
        foreach (var folder in Folders)
        {
            try
            {
                Directory.CreateDirectory(folder.Path);

                // The covers folder is made here so that a cache Nook cannot write to is reported as the cache.
                if (folder.Path == CacheFolder)
                {
                    Directory.CreateDirectory(CoversCacheFolder);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw FolderError(folder, "create", "create it and write to it", exception);
            }
        }
    }

    /// <summary>Creates the folders, checks that Nook can write to each of them, and logs where each one is, so a wrong mount shows in the log.</summary>
    /// <exception cref="InvalidOperationException">A folder cannot be created or written to. The message names the folder and the setting that places it.</exception>
    public void PrepareFolders(ILogger logger)
    {
        CreateFolders();

        foreach (var folder in Folders)
        {
            CheckWritable(folder);
        }

        foreach (var folder in Folders)
        {
            LogFolder(logger, folder.Name, folder.Path, folder.Setting);
        }
    }

    /// <summary>Writes and deletes a small file in a folder, and fails with a message that names the folder when it cannot.</summary>
    private static void CheckWritable(StorageFolder folder)
    {
        var probe = Path.Combine(folder.Path, $".nook-write-check-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllBytes(probe, [0]);
            File.Delete(probe);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw FolderError(folder, "write to", "write to it", exception);
        }
    }

    /// <summary>The error for a folder Nook cannot use. Its message names the folder and the setting that places it.</summary>
    private static InvalidOperationException FolderError(StorageFolder folder, string action, string need, Exception inner) =>
        new($"Nook cannot {action} the {folder.Name} folder at '{folder.Path}', which {folder.Setting} places. The user Nook runs as must be able to {need}.", inner);

    /// <summary>How folder paths compare: by case on Linux, and ignoring case on Windows and macOS, whose disks usually do.</summary>
    private static StringComparison PathComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private static bool IsSameFolder(string first, string second) =>
        string.Equals(Path.TrimEndingDirectorySeparator(first), Path.TrimEndingDirectorySeparator(second), PathComparison);

    /// <summary>Whether a folder is inside another, by whole path segments, so <c>/a/library</c> is not inside <c>/a/lib</c>.</summary>
    private static bool IsInside(string folder, string outer)
    {
        var trimmedOuter = Path.TrimEndingDirectorySeparator(outer);
        var prefix = Path.EndsInDirectorySeparator(trimmedOuter) ? trimmedOuter : trimmedOuter + Path.DirectorySeparatorChar;
        return !IsSameFolder(folder, outer) && folder.StartsWith(prefix, PathComparison);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The {FolderName} folder is {FolderPath}, placed by {Setting}.")]
    private static partial void LogFolder(ILogger logger, string folderName, string folderPath, string setting);
}

/// <summary>One of Nook's folders, with the setting that places it.</summary>
/// <param name="Name">What the folder is, such as <c>library</c>, as messages name it.</param>
/// <param name="Path">The folder's full path.</param>
/// <param name="Setting">The setting that places the folder, such as <c>Nook:LibraryPath</c>.</param>
public sealed record StorageFolder(string Name, string Path, string Setting);
