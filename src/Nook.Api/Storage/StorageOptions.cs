using System.ComponentModel.DataAnnotations;

namespace Nook.Api.Storage;

/// <summary>Where Nook keeps its files, bound from <c>Nook</c>.</summary>
public sealed class StorageOptions
{
    public const string Section = "Nook";

    /// <summary>The folder that holds everything Nook keeps on disk: the library and the files waiting to be ingested.</summary>
    [Required(ErrorMessage = "Nook:DataPath is not set. Set it to the folder where Nook keeps its files.")]
    public string DataPath { get; set; } = "";
}
