using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;

namespace Nook.Api.Features.Works;

/// <summary>Builds the search vector of works from the rows in the database.</summary>
public class WorkSearchIndex(NookDbContext db)
{
    /// <summary>
    /// Writes the vector of each work: its title at weight A, its authors' names at B, and its series' name and
    /// tags' names at C. Other contributors, the description, the publisher and the external ids are left out.
    /// Writing a vector is not an edit, so the work's updated time stays as it is.
    /// </summary>
    public async Task RebuildAsync(IReadOnlyCollection<Guid> workIds, CancellationToken cancellationToken)
    {
        foreach (var id in workIds)
        {
            // The authors and the series are read as every list of works reads them, so a work is found by the
            // names it shows.
            var work = await db.Works.AsNoTracking()
                .Where(w => w.Id == id)
                .ToSummaries(db)
                .SingleOrDefaultAsync(cancellationToken);
            if (work is null)
            {
                continue;
            }

            var tags = await db.Works.AsNoTracking()
                .Where(w => w.Id == id)
                .SelectMany(w => w.Tags.Select(t => t.Name))
                .ToListAsync(cancellationToken);

            var title = Join(work.Title);
            var authors = Join([.. work.Authors]);
            var rest = Join([work.Series?.Name, .. tags]);
            await db.Database.ExecuteSqlAsync(
                $"""
                UPDATE works SET search_vector =
                    setweight(to_tsvector('nook', {title}), 'A')
                    || setweight(to_tsvector('nook', {authors}), 'B')
                    || setweight(to_tsvector('nook', {rest}), 'C')
                WHERE id = {id}
                """,
                cancellationToken);
        }
    }

    /// <summary>Builds the vector of every work that has none, such as one saved before search was built.</summary>
    public async Task RebuildMissingAsync(CancellationToken cancellationToken)
    {
        var workIds = await db.Works.Where(w => w.SearchVector == null).Select(w => w.Id).ToListAsync(cancellationToken);
        await RebuildAsync(workIds, cancellationToken);
    }

    private static string Join(params string?[] texts) =>
        string.Join(' ', texts.SelectMany(SearchWords.Of));
}
