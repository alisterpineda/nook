using System.Text;
using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;
using Nook.Api.Ingest.ExternalIds;

namespace Nook.Api.Ingest;

/// <summary>Finds the Work a book file belongs to in a library, or builds a new one from its draft.</summary>
public sealed class WorkMatcher(NookDbContext db)
{
    /// <summary>The escape character of the LIKE patterns built here.</summary>
    private const string LikeEscape = "\\";

    /// <summary>
    /// The oldest item in the library with a part holding these bytes, by their SHA-256, or null when there is none.
    /// </summary>
    public async Task<Guid?> FindItemWithHashAsync(Guid libraryId, string sha256, CancellationToken cancellationToken) =>
        await (
            from part in db.Parts
            join item in db.Items on part.ItemId equals item.Id
            join work in db.Works on item.WorkId equals work.Id
            where part.Sha256 == sha256 && work.LibraryId == libraryId
            orderby item.CreatedAt, item.Id
            select (Guid?)item.Id).FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// The oldest work in the library that holds one of the draft's ISBNs, tracked so filing can give it a cover it
    /// lacks, or null when none does or the draft has no ISBN.
    /// </summary>
    public async Task<Work?> FindByIsbnAsync(Library library, WorkDraft draft, CancellationToken cancellationToken)
    {
        var isbns = draft.Isbns;
        if (isbns.Count == 0)
        {
            return null;
        }

        return await db.Works
            .Where(w => w.LibraryId == library.Id
                && w.ExternalIds.Any(e => e.Scheme == ExternalIdRegistry.Isbn && isbns.Contains(e.Value)))
            .OrderBy(w => w.CreatedAt)
            .ThenBy(w => w.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Builds a new work in the library from the draft, with its people, series, tags and external ids, and adds it
    /// all to the context unsaved, returning it with the name of the author it is filed under. A person, series or
    /// tag whose name key equals the stored name key of one already here is the oldest such row, left as it is; any
    /// other is new, stored under its name tidied.
    /// </summary>
    public async Task<(Work Work, string? FilingAuthor)> BuildAsync(
        Library library, WorkDraft draft, CancellationToken cancellationToken)
    {
        var work = new Work
        {
            LibraryId = library.Id,
            Title = draft.Title,
            SortTitle = draft.SortTitle,
            Description = draft.Description,
            Publisher = draft.Publisher,
            Language = draft.Language,
            PublicationDate = draft.PublicationDate,
        };

        // Contributors are numbered in draft order without gaps. A new person takes the sort name the draft gives.
        var people = await NamedRows.FindOrAddAsync(
            db.People,
            draft.Contributors.Select(c => c.Name),
            name => new Person
            {
                Name = NameKey.Tidy(name),
                SortName = draft.Contributors.First(c => c.Name == name).SortName ?? NameKey.Tidy(name),
            },
            cancellationToken);
        string? filingAuthor = null;
        var order = 0;
        foreach (var contributor in draft.Contributors)
        {
            var person = people[NameKey.Of(contributor.Name)];

            // The folder takes the first author's name as the person row has it, so one person has one folder.
            if (contributor.Role == ContributorRole.Author)
            {
                filingAuthor ??= person.Name;
            }

            work.Contributors.Add(new WorkContributor
            {
                WorkId = work.Id,
                PersonId = person.Id,
                Role = contributor.Role,
                Order = order++,
            });
        }

        if (draft.Series is { } bookSeries)
        {
            var series = await NamedRows.FindOrAddAsync(
                db.Series, [bookSeries.Name], name => new Series { Name = NameKey.Tidy(name) },
                cancellationToken);
            work.SeriesId = series[NameKey.Of(bookSeries.Name)].Id;
            work.SeriesPosition = bookSeries.Position;
        }

        var tags = await NamedRows.FindOrAddAsync(
            db.Tags, draft.Subjects, name => new Tag { Name = NameKey.Tidy(name) }, cancellationToken);
        foreach (var subject in draft.Subjects)
        {
            work.Tags.Add(tags[NameKey.Of(subject)]);
        }

        foreach (var externalId in draft.ExternalIds)
        {
            work.ExternalIds.Add(new WorkExternalId
            {
                WorkId = work.Id,
                Scheme = externalId.Scheme,
                Value = externalId.Value,
            });
        }

        db.Works.Add(work);
        return (work, filingAuthor);
    }

    /// <summary>
    /// The paths of the library's parts inside the <c>/</c>-separated folder, at any depth, as
    /// <see cref="Filing.IsInside"/> judges it; an empty folder stands for the whole library. Filing takes these as
    /// the paths that are claimed.
    /// </summary>
    public async Task<List<string>> ClaimedPathsAsync(
        Library library, string folder, CancellationToken cancellationToken)
    {
        // The database only narrows the rows, by a case-insensitive prefix in each Unicode form a stored path is
        // likely to have, since paths saved before filing wrote one form keep the form their book gave them. Filing's
        // own rule then decides. A path in a mix of forms, or with letters the database lowercases otherwise than
        // .NET, could still be missed.
        var prefix = folder.Length == 0 ? "" : folder + "/";
        var composed = EscapeLike(Filing.InForm(prefix, NormalizationForm.FormC)) + "%";
        var decomposed = EscapeLike(Filing.InForm(prefix, NormalizationForm.FormD)) + "%";
        var paths = await (
            from part in db.Parts
            join item in db.Items on part.ItemId equals item.Id
            join work in db.Works on item.WorkId equals work.Id
            where work.LibraryId == library.Id
                && (EF.Functions.ILike(part.Path, composed, LikeEscape)
                    || EF.Functions.ILike(part.Path, decomposed, LikeEscape))
            select part.Path).ToListAsync(cancellationToken);
        return paths.Where(path => Filing.IsInside(path, folder)).ToList();
    }

    /// <summary>The text as a LIKE pattern that matches only itself, with <see cref="LikeEscape"/> as its escape.</summary>
    private static string EscapeLike(string text) =>
        text.Replace(LikeEscape, LikeEscape + LikeEscape)
            .Replace("%", LikeEscape + "%")
            .Replace("_", LikeEscape + "_");

    /// <summary>
    /// The <c>/</c>-separated folder, relative to the library's folder, that holds the work's earliest part. A work
    /// with no part yet gets the folder its own title and first author make.
    /// </summary>
    public async Task<string> FindFolderAsync(Work work, CancellationToken cancellationToken)
    {
        var earliest = await (
            from part in db.Parts
            join item in db.Items on part.ItemId equals item.Id
            where item.WorkId == work.Id
            orderby part.CreatedAt, part.Id
            select part.Path).FirstOrDefaultAsync(cancellationToken);
        if (earliest is not null)
        {
            return Filing.FolderOf(earliest);
        }

        var author = await (
            from contributor in db.WorkContributors
            join person in db.People on contributor.PersonId equals person.Id
            where contributor.WorkId == work.Id && contributor.Role == ContributorRole.Author
            orderby contributor.Order, contributor.Id
            select person.Name).FirstOrDefaultAsync(cancellationToken);
        return Filing.WorkFolder(author, work.Title);
    }
}
