using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Nook.Api.Data;

namespace Nook.Api.Features.Works;

/// <summary>The order a list of works comes in.</summary>
public enum WorkSort
{
    /// <summary>By sort title, A to Z.</summary>
    Title,

    /// <summary>By when the work was added, newest first.</summary>
    Added,

    /// <summary>By how well the work matches the search, best first. Only a search can be sorted this way.</summary>
    Relevance,
}

public class WorkListQuery : IValidatableObject
{
    /// <summary>Only works in this section.</summary>
    public Guid? SectionId { get; set; }

    /// <summary>Only works in no section. It cannot be set together with <see cref="SectionId"/>.</summary>
    public bool NoSection { get; set; }

    /// <summary>Only works that match these words: every word, the last as the start of a word.</summary>
    [StringLength(200)]
    public string? Search { get; set; }

    /// <summary>The order. With none, a search is by relevance and a list with no search is by title.</summary>
    public WorkSort? Sort { get; set; }

    [Range(0, int.MaxValue)]
    public int Offset { get; set; }

    [Range(1, 200)]
    public int Limit { get; set; } = 50;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (NoSection && SectionId is not null)
        {
            yield return new ValidationResult(
                "A list is of one section or of the works in no section, not both.",
                [nameof(SectionId), nameof(NoSection)]);
        }

        if (Sort == WorkSort.Relevance && string.IsNullOrWhiteSpace(Search))
        {
            yield return new ValidationResult(
                "Only a search can be sorted by relevance.",
                [nameof(Sort), nameof(Search)]);
        }
    }
}

/// <summary>A work's series and its place in it.</summary>
public record WorkSeriesResponse(Guid Id, string Name, decimal? Position);

/// <summary>A work as a list shows it.</summary>
public record WorkSummaryResponse(
    Guid Id,
    Guid? SectionId,
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

/// <summary>
/// Everything Nook knows about one work. Contributors and parts come in display order. <c>Access</c> is the caller's
/// level for the work: whether they may only browse it, read it in the browser, or download its files. <c>CanEdit</c>
/// is whether the caller may change or delete the work: an admin may, and so may the work's owner while their role
/// may upload. <c>AddedBy</c> is the shown name of the work's owner. It is left out for a visitor with no account
/// and for a work with no owner.
/// </summary>
public record WorkDetailResponse(
    Guid Id,
    Guid? SectionId,
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
    DateTimeOffset UpdatedAt,
    ContentAccess Access,
    bool CanEdit,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AddedBy);
