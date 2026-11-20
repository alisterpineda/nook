namespace Nook.Api.Ingest;

/// <summary>Why an upload's ingest ended in failure, in terms the uploader can be told.</summary>
public enum UploadFailureKind
{
    /// <summary>The file is not a book format Nook knows.</summary>
    UnsupportedFormat,

    /// <summary>The file is a book format Nook knows, but it is DRM-encrypted or broken.</summary>
    Unreadable,

    /// <summary>The ingest failed for a reason of Nook's own.</summary>
    Internal,
}
