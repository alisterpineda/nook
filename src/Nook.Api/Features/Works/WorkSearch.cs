using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;

namespace Nook.Api.Features.Works;

/// <summary>How a typed term finds works and how the matches are ordered, shared by every place that searches works.</summary>
public static class WorkSearch
{
    /// <summary>
    /// The term as a query where every word must match and the last matches as the start of a word, or null when the
    /// term has no words.
    /// </summary>
    public static string? QueryFor(string term)
    {
        var words = SearchWords.Of(term);
        return words.Count == 0 ? null : string.Join(" & ", words) + ":*";
    }

    /// <summary>The works the query matches.</summary>
    public static IQueryable<Work> Matching(this IQueryable<Work> works, string tsQuery) =>
        works.Where(w => w.SearchVector!.Matches(EF.Functions.ToTsQuery("nook", tsQuery)));

    /// <summary>The works the best match first, then by sort title; the id breaks ties, so a work never shows on two pages or on none.</summary>
    public static IOrderedQueryable<Work> ByRelevance(this IQueryable<Work> works, string tsQuery) =>
        works
            .OrderByDescending(w => w.SearchVector!.Rank(EF.Functions.ToTsQuery("nook", tsQuery)))
            .ThenBy(w => w.SortTitle)
            .ThenBy(w => w.Id);
}
