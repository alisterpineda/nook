using System.ComponentModel.DataAnnotations;
using Nook.Api.Data;

namespace Nook.Api.Features.Works;

/// <summary>The order a list of works comes in.</summary>
public enum WorkSort
{
    /// <summary>By sort title, A to Z.</summary>
    Title,

    /// <summary>By when the work was added, newest first.</summary>
    Added,
}

public class WorkListQuery
{
    /// <summary>Only works in this library.</summary>
    public Guid? LibraryId { get; set; }

    public WorkSort Sort { get; set; } = WorkSort.Title;

    [Range(0, int.MaxValue)]
    public int Offset { get; set; }

    [Range(1, 200)]
    public int Limit { get; set; } = 50;
}

/// <summary>A work's series and its place in it.</summary>
public record WorkSeriesResponse(Guid Id, string Name, decimal? Position);

/// <summary>A work as a list shows it.</summary>
public record WorkSummaryResponse(
    Guid Id,
    Guid LibraryId,
    string Title,
    IReadOnlyList<string> Authors,
    WorkSeriesResponse? Series,
    string? CoverHash,
    IReadOnlyList<ItemFormat> Formats,
    DateTimeOffset AddedAt);

public record WorkContributorResponse(Guid PersonId, string Name, ContributorRole Role);

public record WorkTagResponse(Guid Id, string Name);

public record WorkExternalIdResponse(string Scheme, string Value);

public record WorkPartResponse(Guid Id, string MediaType, long Size);

public record WorkItemResponse(Guid Id, ItemFormat Format, IReadOnlyList<WorkPartResponse> Parts);

/// <summary>Everything Nook knows about one work. Contributors and parts come in display order.</summary>
public record WorkDetailResponse(
    Guid Id,
    Guid LibraryId,
    string Title,
    string SortTitle,
    string? Description,
    string? Language,
    DateOnly? PublicationDate,
    string? Publisher,
    WorkSeriesResponse? Series,
    string? CoverHash,
    IReadOnlyList<WorkContributorResponse> Contributors,
    IReadOnlyList<WorkTagResponse> Tags,
    IReadOnlyList<WorkExternalIdResponse> ExternalIds,
    IReadOnlyList<WorkItemResponse> Items,
    DateTimeOffset AddedAt,
    DateTimeOffset UpdatedAt);
