using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;
using Nook.Api.Features.Sections;
using Nook.Api.Features.Works;

namespace Nook.Api.Features.People;

[ApiController]
[Route("api/people")]
public class PeopleController(NookDbContext db) : ControllerBase
{
    /// <summary>
    /// One page of the people credited on a work the signed-in user can see, or holding the given role on one, by sort
    /// name, each with how many such works they are credited on. A work counts once however many roles they hold on it.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<Page<PersonSummaryResponse>>> List(
        [FromQuery] PersonListQuery query, CancellationToken cancellationToken)
    {
        var works = db.Works.VisibleTo(User);
        var credits = db.WorkContributors.AsQueryable();
        if (query.Role is { } role)
        {
            credits = credits.Where(c => c.Role == role);
        }

        var people = db.People
            .Select(p => new
            {
                Person = p,
                WorkCount = works.Count(w => credits.Any(c => c.WorkId == w.Id && c.PersonId == p.Id)),
            })
            .Where(p => p.WorkCount > 0);

        var total = await people.CountAsync(cancellationToken);

        // The id breaks ties, so a person never shows on two pages or on none.
        var items = await people
            .OrderBy(p => p.Person.SortName)
            .ThenBy(p => p.Person.Id)
            .Skip(query.Offset)
            .Take(query.Limit)
            .Select(p => new PersonSummaryResponse(p.Person.Id, p.Person.Name, p.Person.SortName, p.WorkCount))
            .ToListAsync(cancellationToken);

        return new Page<PersonSummaryResponse>(items, total, query.Offset, query.Limit);
    }

    /// <summary>
    /// One person with every work the signed-in user can see that they are credited on, grouped by role in the order
    /// the roles are declared. Within a role the works in a series come first, by series name then place in the
    /// series, and the works in no series after them, by sort title.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<PersonDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PersonDetailResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var person = await db.People.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (person is null)
        {
            return NotFound();
        }

        // The id breaks ties, so works with the same sort title always come in the same order.
        var works = await db.Works
            .VisibleTo(User)
            .Where(w => db.WorkContributors.Any(c => c.WorkId == w.Id && c.PersonId == id))
            .OrderBy(w => w.SortTitle)
            .ThenBy(w => w.Id)
            .ToSummaries(db)
            .ToListAsync(cancellationToken);

        // A person credited only on works the user cannot see is reported the same way as one that does not exist.
        if (works.Count == 0)
        {
            return NotFound();
        }

        // Credits on works the user cannot see are dropped below, since only the works read above are walked.
        var credits = (await db.WorkContributors
                .Where(c => c.PersonId == id)
                .Select(c => new { c.WorkId, c.Role })
                .ToListAsync(cancellationToken))
            .Select(c => (c.WorkId, c.Role))
            .ToHashSet();

        var roles = Enum.GetValues<ContributorRole>()
            .Select(role => new PersonRoleResponse(
                role,
                ArrangeBySeries(works.Where(w => credits.Contains((w.Id, role)))).ToList()))
            .Where(r => r.Works.Count > 0)
            .ToList();

        return new PersonDetailResponse(person.Id, person.Name, person.SortName, roles);
    }

    /// <summary>
    /// The works in a series first, by series name, then series id so two series of one name stay apart, then place
    /// with no place last; then the works in no series. The sorts are stable, so works that tie keep the order they
    /// came in.
    /// </summary>
    private static IEnumerable<WorkSummaryResponse> ArrangeBySeries(IEnumerable<WorkSummaryResponse> works) =>
        works
            .OrderBy(w => w.Series is null)
            .ThenBy(w => w.Series?.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(w => w.Series?.Id)
            .ThenBy(w => w.Series?.Position is null)
            .ThenBy(w => w.Series?.Position);
}
