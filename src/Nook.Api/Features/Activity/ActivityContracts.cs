using Nook.Api.Ingest;

namespace Nook.Api.Features.Activity;

/// <summary>Where an upload stands.</summary>
public enum UploadActivityStatus
{
    /// <summary>Its job is waiting to run.</summary>
    Queued,

    /// <summary>Its job is running.</summary>
    Running,

    /// <summary>It became a new item of a work.</summary>
    Added,

    /// <summary>The section already held the file.</summary>
    Duplicate,

    /// <summary>Its ingest failed.</summary>
    Failed,
}

/// <summary>
/// One of the caller's uploads. The job's own error is never part of it: <paramref name="Failure"/> is all a
/// non-admin is told about why an upload failed.
/// </summary>
/// <param name="JobId">The id of the job that ingests the file.</param>
/// <param name="FileName">The name the file was uploaded under.</param>
/// <param name="SectionId">The section it went into, or null for the Default area.</param>
/// <param name="SectionName">The section's name when it was uploaded, or null for the Default area.</param>
/// <param name="SubmittedAt">When the upload was accepted.</param>
/// <param name="WorkId">The work it became or matched, once added or duplicate.</param>
/// <param name="Failure">Why it failed, once failed.</param>
public record UploadActivityResponse(
    Guid JobId,
    string FileName,
    Guid? SectionId,
    string? SectionName,
    DateTimeOffset SubmittedAt,
    UploadActivityStatus Status,
    Guid? WorkId,
    UploadFailureKind? Failure);
