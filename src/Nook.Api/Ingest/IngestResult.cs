namespace Nook.Api.Ingest;

/// <summary>What an ingest job did with its file.</summary>
public enum IngestOutcome
{
    /// <summary>The file became a new item.</summary>
    Created,

    /// <summary>The library already held the file, so nothing was added.</summary>
    Duplicate,
}

/// <summary>What an ingest job leaves as its result.</summary>
/// <param name="Outcome">What the job did with its file.</param>
/// <param name="ItemId">The item the file became or, for a duplicate, the item that already held it.</param>
public sealed record IngestResult(IngestOutcome Outcome, Guid ItemId);
