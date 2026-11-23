using System.ComponentModel.DataAnnotations;

namespace Nook.Api.Features.Search;

/// <summary>What a search looks through. Each source answers as one group.</summary>
public enum SearchSource
{
    Works,
}

public class SearchQuery : IValidatableObject
{
    /// <summary>The words to look for: at least 2 characters once trimmed.</summary>
    [StringLength(200)]
    public string? Q { get; set; }

    /// <summary>The most hits to return for each source.</summary>
    [Range(1, 20)]
    public int Limit { get; set; } = 6;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if ((Q?.Trim().Length ?? 0) < 2)
        {
            yield return new ValidationResult("The term must have at least 2 characters.", [nameof(Q)]);
        }
    }
}

public record SearchResponse(IReadOnlyList<SearchGroup> Groups);

/// <summary>One source's hits: the first <c>Limit</c> of them, and how many matched in all.</summary>
public record SearchGroup(SearchSource Source, int Total, IReadOnlyList<SearchHit> Results);

public record SearchHit(Guid Id, string Title, string? Subtitle);
