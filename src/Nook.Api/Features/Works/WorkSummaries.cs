using Nook.Api.Data;

namespace Nook.Api.Features.Works;

public static class WorkSummaries
{
    /// <summary>
    /// Each work as it shows in a list, in the order the works come in. Every answer that lists works goes through
    /// here, so a work looks the same in all of them.
    /// </summary>
    public static IQueryable<WorkSummaryResponse> ToSummaries(this IQueryable<Work> works, NookDbContext db) =>
        works.Select(w => new WorkSummaryResponse(
            w.Id,
            w.SectionId,
            w.Title,
            (from c in w.Contributors
             join p in db.People on c.PersonId equals p.Id
             where c.Role == ContributorRole.Author
             orderby c.Order, c.Id
             select p.Name).ToList(),
            db.Series
                .Where(s => s.Id == w.SeriesId)
                .Select(s => new WorkSeriesResponse(s.Id, s.Name, w.SeriesPosition))
                .FirstOrDefault(),
            w.CoverHash,
            w.Items.Select(i => i.Format).Distinct().OrderBy(f => f).ToList(),
            w.CreatedAt));
}
