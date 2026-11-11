namespace Nook.Api.Data;

/// <summary>Where an ingest run stands. Stored as the member name, so new members need no migration.</summary>
public enum IngestRunStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,

    /// <summary>The source matched an item the library already held; the run points at that item.</summary>
    Duplicate,
}
