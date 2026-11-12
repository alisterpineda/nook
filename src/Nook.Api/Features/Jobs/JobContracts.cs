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
}

/// <summary>A job as a list shows it. The payload is left out: it can hold paths on the server's disk.</summary>
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
    DateTimeOffset UpdatedAt);

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
    IReadOnlyList<JobSummaryResponse> Children);
