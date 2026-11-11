namespace Nook.Api.Data;

/// <summary>One file of an item.</summary>
public class Part : Entity
{
    public Guid ItemId { get; set; }

    /// <summary>Where the part comes among the item's parts, lowest first.</summary>
    public int Order { get; set; }

    /// <summary>The file's path, relative to the library folder.</summary>
    public required string Path { get; set; }

    /// <summary>The file's media type, such as "application/epub+zip".</summary>
    public required string MediaType { get; set; }

    /// <summary>The file's size in bytes.</summary>
    public long Size { get; set; }

    /// <summary>The file's SHA-256 as 64 lowercase hex characters. Two parts can hold the same one.</summary>
    public required string Sha256 { get; set; }
}
