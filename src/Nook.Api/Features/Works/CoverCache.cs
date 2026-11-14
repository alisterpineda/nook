using Nook.Api.Storage;

namespace Nook.Api.Features.Works;

/// <summary>
/// The resized covers kept in the cache folder, one JPEG for each cover hash and size, so works that share a cover
/// share its files. Every file can be made again from the cover in the library, so the folder can be emptied at any
/// time.
/// </summary>
public class CoverCache(NookStorage storage)
{
    /// <summary>The path a cover resized to a size is kept at, whether or not it is there yet.</summary>
    public string PathOf(string coverHash, CoverSize size) =>
        Path.Combine(storage.CoversCacheFolder, $"{coverHash}-{CoverSizes.NameOf(size)}.jpg");

    /// <summary>
    /// Keeps a resized cover at its path. The bytes are written under a temporary name in the same folder and moved
    /// into place, so a request never reads a file half written. The folder is created when it is missing. When
    /// another request put the file there first, that file is kept.
    /// </summary>
    public async Task WriteAsync(string path, byte[] jpeg, CancellationToken cancellationToken)
    {
        var folder = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(folder);

        var temporary = Path.Combine(folder, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var file = NookStorage.CreateFile(temporary))
            {
                await file.WriteAsync(jpeg, cancellationToken);
            }

            try
            {
                File.Move(temporary, path);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Another request made the same file at the same time.
            }
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
