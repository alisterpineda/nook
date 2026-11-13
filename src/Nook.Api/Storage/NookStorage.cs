using Microsoft.Extensions.Options;
using Nook.Api.Data;

namespace Nook.Api.Storage;

/// <summary>The folders under the data root: <c>library/</c>, which holds one folder per library, and <c>staging/</c>, which holds uploads waiting to be ingested.</summary>
public sealed class NookStorage(IOptions<StorageOptions> options)
{
    /// <summary>The buffer files are copied through, the size <see cref="Stream.CopyToAsync(Stream)"/> uses.</summary>
    private const int CopyBufferSize = 81920;

    private string Root => Path.GetFullPath(options.Value.DataPath);

    /// <summary>Where uploaded files wait until their ingest finishes.</summary>
    public string StagingFolder => Path.Combine(Root, "staging");

    /// <summary>The path of one staged file, from its name inside the staging folder.</summary>
    public string StagedFile(string name) => Path.Combine(StagingFolder, name);

    /// <summary>The folder that holds every library's folder.</summary>
    public string LibrariesFolder => Path.Combine(Root, "library");

    /// <summary>The folder that holds one library's files.</summary>
    public string LibraryFolder(Library library) => Path.Combine(LibrariesFolder, library.FolderName);

    /// <summary>The full path of a file in a library, from its <c>/</c>-separated path relative to the library's folder, as a part stores it.</summary>
    /// <exception cref="InvalidOperationException">The path leads outside the library's folder.</exception>
    public string LibraryFile(Library library, string relativePath)
    {
        var folder = Path.GetFullPath(LibraryFolder(library));
        var path = Path.GetFullPath(Path.Combine([folder, .. relativePath.Split('/')]));

        // A path built from a book's own words can hold ".." or a rooted segment, and must not reach other folders.
        if (!path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"The path '{relativePath}' leads outside the library's folder.");
        }

        return path;
    }

    /// <summary>The full path of a folder in a library, from its <c>/</c>-separated path relative to the library's folder. An empty path is the library's folder itself.</summary>
    /// <exception cref="InvalidOperationException">The path leads outside the library's folder.</exception>
    public string LibrarySubfolder(Library library, string relativeFolder) =>
        relativeFolder.Length == 0 ? Path.GetFullPath(LibraryFolder(library)) : LibraryFile(library, relativeFolder);

    /// <summary>Opens a new file for writing. It fails when the file is already there, so nothing is overwritten.</summary>
    public static FileStream CreateFile(string path) =>
        new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferSize, useAsync: true);

    /// <summary>Opens a file for writing, emptying any file already there.</summary>
    public static FileStream ReplaceFile(string path) =>
        new(path, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize, useAsync: true);

    /// <summary>Opens a file for reading.</summary>
    public static FileStream OpenFile(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, useAsync: true);

    /// <summary>Creates the staging and library folders when they are missing. Reading the data root throws when it is not set.</summary>
    public void CreateFolders()
    {
        Directory.CreateDirectory(StagingFolder);
        Directory.CreateDirectory(LibrariesFolder);
    }
}
