using System.ComponentModel.DataAnnotations;

namespace Nook.Api.Storage;

/// <summary>Where Nook keeps its files, bound from <c>Nook</c>. Only the data root is required: the library and the cache sit under it unless they are given folders of their own.</summary>
public sealed class StorageOptions
{
    public const string Section = "Nook";

    /// <summary>The folder that holds everything Nook keeps on disk: the files waiting to be ingested, the app's secrets, and the library and the cache when they have no folders of their own.</summary>
    [Required(ErrorMessage = "Nook:DataPath is not set. Set it to the folder where Nook keeps its files.")]
    public string DataPath { get; set; } = "";

    /// <summary>The folder that holds every book file and cover. When it is not set, or blank, the library is the <c>library</c> folder in the data root.</summary>
    public string? LibraryPath { get; set; }

    /// <summary>The folder that holds files that can be made again from the library, with resized covers in its <c>covers</c> folder. When it is not set, or blank, the cache is the <c>cache</c> folder in the data root.</summary>
    public string? CachePath { get; set; }
}
