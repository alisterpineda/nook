namespace Nook.Api.Data;

/// <summary>
/// How far someone may go with a work, each level including the ones before it. Stored as the member name, so new
/// members need no migration.
/// </summary>
public enum ContentAccess
{
    /// <summary>Nothing: the work is not there for them.</summary>
    None,

    /// <summary>Its metadata and cover.</summary>
    Browse,

    /// <summary>The in-browser reader as well.</summary>
    Read,

    /// <summary>The original files as well.</summary>
    Download,
}
