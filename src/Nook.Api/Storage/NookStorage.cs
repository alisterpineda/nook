using Microsoft.Extensions.Options;

namespace Nook.Api.Storage;

/// <summary>The folders under the data root: <c>library/</c>, which holds every book file and cover, <c>staging/</c>, which holds uploads waiting to be ingested, <c>cache/</c>, which holds files that can be made again from the library, and <c>keys/</c>, which holds the app's secrets, with the keys that protect sign-in cookies in <c>keys/data-protection/</c>.</summary>
public sealed class NookStorage(IOptions<StorageOptions> options)
{
    /// <summary>The buffer files are copied through, the size <see cref="Stream.CopyToAsync(Stream)"/> uses.</summary>
    private const int CopyBufferSize = 81920;

    private string Root => Path.GetFullPath(options.Value.DataPath);

    /// <summary>Where uploaded files wait until their ingest finishes.</summary>
    public string StagingFolder => Path.Combine(Root, "staging");

    /// <summary>The path of one staged file, from its name inside the staging folder.</summary>
    public string StagedFile(string name) => Path.Combine(StagingFolder, name);

    /// <summary>The one folder that holds every book file and cover, whatever section its work is in.</summary>
    public string LibraryFolder => Path.Combine(Root, "library");

    /// <summary>Where resized covers are kept. Everything in it can be made again from the covers in the library.</summary>
    public string CoversCacheFolder => Path.Combine(Root, "cache", "covers");

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
        if (!path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal))
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

    /// <summary>Creates the staging, library, cover cache and keys folders when they are missing. Reading the data root throws when it is not set.</summary>
    public void CreateFolders()
    {
        Directory.CreateDirectory(StagingFolder);
        Directory.CreateDirectory(LibraryFolder);
        Directory.CreateDirectory(CoversCacheFolder);
        Directory.CreateDirectory(KeysFolder);
    }
}
