using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;
using Nook.Api.Features.Auth;
using Nook.Api.Features.Sections;
using Nook.Api.Features.Works;

namespace Nook.Api.Features.Search;

[ApiController]
[Route("api/search")]
[Authorize(Policy = Policies.Catalog)]
public class SearchController(NookDbContext db, SectionAccess access) : ControllerBase
{
    /// <summary>The hits for a term from each source, a source with no match left out.</summary>
    [HttpGet]
    [ProducesResponseType<SearchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SearchResponse>> Search(
        [FromQuery] SearchQuery query, CancellationToken cancellationToken)
    {
        var groups = new List<SearchGroup>();

        var tsQuery = WorkSearch.QueryFor(query.Q ?? "");
        if (tsQuery is not null)
        {
            var works = db.Works.VisibleTo(await access.ForAsync(User, cancellationToken)).Matching(tsQuery);
            var total = await works.CountAsync(cancellationToken);
            if (total > 0)
            {
                var summaries = await works
                    .ByRelevance(tsQuery)
                    .Take(query.Limit)
                    .ToSummaries(db)
                    .ToListAsync(cancellationToken);
                var hits = summaries
                    .Select(w => new SearchHit(
                        w.Id, w.Title, w.Authors.Count == 0 ? null : string.Join(", ", w.Authors)))
                    .ToList();
                groups.Add(new SearchGroup(SearchSource.Works, total, hits));
            }
        }

        return new SearchResponse(groups);
    }
}
