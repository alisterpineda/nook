namespace Nook.Api.Data;

/// <summary>One attempt to bring a source file into a library.</summary>
public class IngestRun : Entity
{
    /// <summary>The library the source is being brought into. Duplicates are judged within it.</summary>
    public Guid LibraryId { get; set; }

    /// <summary>The path of the file being ingested.</summary>
    public required string SourcePath { get; set; }

    public IngestRunStatus Status { get; set; }

    /// <summary>The item the run produced, or the item it duplicated. Cleared when that item is deleted.</summary>
    public Guid? ItemId { get; set; }
}
