using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Nook.Api.Data;

namespace Nook.Api.Features.Jobs;

public class JobListQuery
{
    [Range(0, int.MaxValue)]
    public int Offset { get; set; }

    [Range(1, 200)]
    public int Limit { get; set; } = 50;

    /// <summary>The statuses to list, any number of them. None means every status.</summary>
    public List<JobStatus> Status { get; set; } = [];
}

/// <summary>
/// A job as a list shows it. The payload is left out: it can hold paths on the server's disk. SubmittedBy is the name
/// of the user who submitted the upload this job ingests, or null when it is not an upload or none is on record.
/// </summary>
public record JobSummaryResponse(
    Guid Id,
    string Type,
    JobStatus Status,
    string? Error,
    Guid? ParentJobId,
    DateTimeOffset? NotBefore,
    int AttemptsMade,
    int AttemptsAllowed,
    int ReclaimsMade,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? SubmittedBy);

/// <summary>One step of one run of a job.</summary>
public record JobStepResponse(
    string Name,
    int Order,
    int Attempt,
    JobStepStatus Status,
    string? Error,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt);

/// <summary>
/// Everything shown about one job: its summary, the result its type wrote, the steps of every run by run
/// and then order, and the jobs enqueued under it, oldest first.
/// </summary>
public record JobDetailResponse(
    Guid Id,
    string Type,
    JobStatus Status,
    string? Error,
    Guid? ParentJobId,
    DateTimeOffset? NotBefore,
    int AttemptsMade,
    int AttemptsAllowed,
    int ReclaimsMade,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    JsonElement? Result,
    IReadOnlyList<JobStepResponse> Steps,
    IReadOnlyList<JobSummaryResponse> Children,
    string? SubmittedBy);
