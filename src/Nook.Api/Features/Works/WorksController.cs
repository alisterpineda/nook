using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;
using Nook.Api.Features.Libraries;

namespace Nook.Api.Features.Works;

[ApiController]
[Route("api/works")]
public class WorksController(NookDbContext db) : ControllerBase
{
    /// <summary>One page of the works the signed-in user can see.</summary>
    [HttpGet]
    public async Task<ActionResult<Page<WorkSummaryResponse>>> List(
        [FromQuery] WorkListQuery query, CancellationToken cancellationToken)
    {
        var works = db.Works.VisibleTo(User);
        if (query.LibraryId is { } libraryId)
        {
            works = works.Where(w => w.LibraryId == libraryId);
        }

        var total = await works.CountAsync(cancellationToken);

        // The id breaks ties, so a work never shows on two pages or on none.
        works = query.Sort switch
        {
            WorkSort.Added => works.OrderByDescending(w => w.CreatedAt).ThenByDescending(w => w.Id),
            _ => works.OrderBy(w => w.SortTitle).ThenBy(w => w.Id),
        };

        var items = await works
            .Skip(query.Offset)
            .Take(query.Limit)
            .Select(w => new WorkSummaryResponse(
                w.Id,
                w.LibraryId,
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
                w.CreatedAt))
            .ToListAsync(cancellationToken);

        return new Page<WorkSummaryResponse>(items, total, query.Offset, query.Limit);
    }

    /// <summary>One work, with its contributors, tags, external ids and items.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkDetailResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        // A work the user cannot see is reported the same way as one that does not exist.
        var work = await db.Works
            .VisibleTo(User)
            .Where(w => w.Id == id)
            .Select(w => new WorkDetailResponse(
                w.Id,
                w.LibraryId,
                w.Title,
                w.SortTitle,
                w.Description,
                w.Language,
                w.PublicationDate,
                w.Publisher,
                db.Series
                    .Where(s => s.Id == w.SeriesId)
                    .Select(s => new WorkSeriesResponse(s.Id, s.Name, w.SeriesPosition))
                    .FirstOrDefault(),
                w.CoverHash,
                (from c in w.Contributors
                 join p in db.People on c.PersonId equals p.Id
                 orderby c.Order, c.Id
                 select new WorkContributorResponse(p.Id, p.Name, c.Role)).ToList(),
                w.Tags.OrderBy(t => t.Name).ThenBy(t => t.Id).Select(t => new WorkTagResponse(t.Id, t.Name)).ToList(),
                w.ExternalIds
                    .OrderBy(e => e.Scheme)
                    .ThenBy(e => e.Value)
                    .Select(e => new WorkExternalIdResponse(e.Scheme, e.Value))
                    .ToList(),
                w.Items
                    .OrderBy(i => i.CreatedAt)
                    .ThenBy(i => i.Id)
                    .Select(i => new WorkItemResponse(
                        i.Id,
                        i.Format,
                        db.Parts
                            .Where(p => p.ItemId == i.Id)
                            .OrderBy(p => p.Order)
                            .ThenBy(p => p.Id)
                            .Select(p => new WorkPartResponse(p.Id, p.MediaType, p.Size))
                            .ToList()))
                    .ToList(),
                w.CreatedAt,
                w.UpdatedAt))
            .AsSplitQuery()
            .SingleOrDefaultAsync(cancellationToken);

        return work is null ? NotFound() : work;
    }
}
