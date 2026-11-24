using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Nook.Api.Data;
using Nook.Api.Features.Audit;
using Nook.Api.Features.Auth;
using Nook.Api.Features.Sections;
using Nook.Api.Ingest;
using Nook.Api.Storage;

namespace Nook.Api.Features.Works;

/// <summary>Changes the works of the library, and says who may.</summary>
public class WorkEditor(
    NookDbContext db,
    NookStorage storage,
    SectionAccess access,
    AuditLog audit,
    IServiceScopeFactory scopes,
    IOptions<IdentityOptions> identityOptions,
    ILogger<WorkEditor> logger)
{
    private readonly WorkMatcher _matcher = new(db);
    private readonly WorkSearchIndex _searchIndex = new(db);

    /// <summary>
    /// Whether the user may change a work owned by <paramref name="ownerId"/>: an admin may change any work, and
    /// anyone else only a work they own while their role may upload. A work with no owner is for admins alone.
    /// </summary>
    public bool MayChange(ClaimsPrincipal user, Guid? ownerId)
    {
        if (user.IsInRole(nameof(BuiltInRole.Admin)))
        {
            return true;
        }

        return ownerId is { } owner
            && user.HasClaim(Policies.CanUploadClaim, "true")
            && user.FindFirstValue(identityOptions.Value.ClaimsIdentity.UserIdClaimType) == owner.ToString();
    }

    private const string NotYoursMessage =
        "Only an admin, or the user who added this book while their role may upload, can change it.";

    /// <summary>The longest title or sort title an edit may set.</summary>
    private const int MaxTitleLength = 500;

    /// <summary>The longest name of a person, series or tag an edit may set.</summary>
    private const int MaxNameLength = 200;

    /// <summary>The longest description an edit may set.</summary>
    private const int MaxDescriptionLength = 20_000;

    /// <summary>The most authors an edit may set.</summary>
    private const int MaxAuthors = 50;

    /// <summary>The most tags an edit may set.</summary>
    private const int MaxTags = 100;

    /// <summary>The highest place in a series an edit may set.</summary>
    private const decimal MaxSeriesPosition = 99_999;

    /// <summary>
    /// Replaces the editable state of a work with the request's, when the user may change it. Takes the ingest lock,
    /// as an ingest does, so an edit and an ingest never change the same rows at once. A limit is held only for a
    /// value the edit changes, so a work stored with a value over a limit can still be saved with that value as it is.
    /// </summary>
    public async Task<WorkEditOutcome> EditAsync(
        ClaimsPrincipal user, Guid id, WorkEditRequest request, CancellationToken cancellationToken)
    {
        var grants = await access.ForAsync(user, cancellationToken);
        var title = request.Title.Trim();
        var sortTitle = request.SortTitle.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        // The database is set to retry, so the transaction runs inside its retry strategy.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // A retry starts from what the database holds, not from what a failed attempt loaded.
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await IngestLock.TakeAsync(db, cancellationToken);

            var (work, refusal) = await LoadToChangeAsync(user, grants, id, cancellationToken);
            if (work is null)
            {
                return refusal == Refusal.NotFound ? WorkEditOutcome.NotFound : WorkEditOutcome.Forbidden(NotYoursMessage);
            }

            if (title != work.Title && title.Length > MaxTitleLength)
            {
                return WorkEditOutcome.Invalid(nameof(request.Title), $"The title is over {MaxTitleLength} characters.");
            }

            if (sortTitle != work.SortTitle && sortTitle.Length > MaxTitleLength)
            {
                return WorkEditOutcome.Invalid(nameof(request.SortTitle), $"The sort title is over {MaxTitleLength} characters.");
            }

            if (description != work.Description && description?.Length > MaxDescriptionLength)
            {
                return WorkEditOutcome.Invalid(
                    nameof(request.Description), $"The description is over {MaxDescriptionLength} characters.");
            }

            // What the work is linked to now, read before anything changes, so what the edit leaves unused can be found.
            var people = await db.People
                .Where(p => work.Contributors.Select(c => c.PersonId).Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, cancellationToken);
            var authorLinks = AuthorLinks(work);
            var currentAuthors = authorLinks.Select(c => people[c.PersonId].Name).ToList();
            var currentTags = work.Tags.Select(t => t.Name).ToList();
            var currentSeries = work.SeriesId is { } currentSeriesId
                ? await db.Series.SingleAsync(s => s.Id == currentSeriesId, cancellationToken)
                : null;
            var currentSortTitle = work.SortTitle;
            var currentDescription = work.Description;
            var currentSectionId = work.SectionId;
            var currentPosition = work.SeriesPosition;
            var linkedPeople = people.Keys.ToList();
            var linkedTags = work.Tags.Select(t => t.Id).ToList();

            var authors = request.Authors.Select(NameKey.Tidy).ToList();
            var tags = request.Tags.Select(NameKey.Tidy).ToList();
            var series = request.Series is { } wanted ? new WorkEditSeriesRequest(NameKey.Tidy(wanted.Name), wanted.Position) : null;

            if (!authors.SequenceEqual(currentAuthors) && TooLong(authors, MaxAuthors, "author") is { } authorsMessage)
            {
                return WorkEditOutcome.Invalid(nameof(request.Authors), authorsMessage);
            }

            if (!tags.Order(StringComparer.Ordinal).SequenceEqual(currentTags.Order(StringComparer.Ordinal))
                && TooLong(tags, MaxTags, "tag") is { } tagsMessage)
            {
                return WorkEditOutcome.Invalid(nameof(request.Tags), tagsMessage);
            }

            if (series is not null)
            {
                if (series.Name != currentSeries?.Name && series.Name.Length > MaxNameLength)
                {
                    return WorkEditOutcome.Invalid(
                        nameof(request.Series), $"The series' name is over {MaxNameLength} characters.");
                }

                if (series.Position != work.SeriesPosition && series.Position is < 0 or > MaxSeriesPosition)
                {
                    return WorkEditOutcome.Invalid(
                        nameof(request.Series), $"The place in the series is not from 0 to {MaxSeriesPosition:N0}.");
                }
            }

            // Moving a work is for a role that may see the section it goes to, as an upload is.
            if (request.SectionId != work.SectionId)
            {
                if (request.SectionId is { } sectionId
                    && !await db.Sections.AnyAsync(s => s.Id == sectionId, cancellationToken))
                {
                    return WorkEditOutcome.Invalid(nameof(request.SectionId), "There is no section with this id.");
                }

                if (grants.In(request.SectionId) < ContentAccess.Browse)
                {
                    return WorkEditOutcome.Forbidden("Your role cannot see this section, so it cannot move books to it.");
                }

                work.SectionId = request.SectionId;
            }

            var currentTitle = work.Title;
            work.Title = title;
            work.SortTitle = sortTitle;
            work.Description = description;

            // An author's row is the one with that name key, or a new one, as an ingest finds it.
            var personRows = await NamedRows.FindOrAddAsync(
                db.People,
                authors,
                name => new Person { Name = NameKey.Tidy(name), SortName = NameKey.Tidy(name) },
                cancellationToken);
            var tagRows = await NamedRows.FindOrAddAsync(
                db.Tags, tags, name => new Tag { Name = NameKey.Tidy(name) }, cancellationToken);

            // The authors come first in the order sent, then everyone else in the order they had.
            var others = work.Contributors
                .Where(c => c.Role != ContributorRole.Author)
                .OrderBy(c => c.Order)
                .ThenBy(c => c.Id)
                .ToList();
            db.WorkContributors.RemoveRange(authorLinks);
            var order = 0;
            foreach (var name in authors)
            {
                db.WorkContributors.Add(new WorkContributor
                {
                    WorkId = work.Id,
                    PersonId = personRows[NameKey.Of(name)].Id,
                    Role = ContributorRole.Author,
                    Order = order++,
                });
            }

            foreach (var other in others)
            {
                other.Order = order++;
            }

            Series? seriesRow = null;
            if (series is null)
            {
                work.SeriesId = null;
                work.SeriesPosition = null;
            }
            else
            {
                var seriesRows = await NamedRows.FindOrAddAsync(
                    db.Series, [series.Name], name => new Series { Name = NameKey.Tidy(name) }, cancellationToken);
                seriesRow = seriesRows[NameKey.Of(series.Name)];
                work.SeriesId = seriesRow.Id;
                work.SeriesPosition = series.Position;
            }

            var wantedTags = tags.Select(name => tagRows[NameKey.Of(name)]).ToList();
            foreach (var gone in work.Tags.Where(t => !wantedTags.Contains(t)).ToList())
            {
                work.Tags.Remove(gone);
            }

            foreach (var added in wantedTags.Where(t => !work.Tags.Contains(t)))
            {
                work.Tags.Add(added);
            }

            // What the edit changed, by the names the rows hold: a name that an existing row matches is stored as that
            // row's name, so another letter case of it is no change.
            var newAuthors = authors.Select(name => personRows[NameKey.Of(name)].Name).ToList();
            var changes = new Dictionary<string, object?>();
            AddChange(changes, "title", currentTitle, title);
            AddChange(changes, "sortTitle", currentSortTitle, sortTitle);
            if (!newAuthors.SequenceEqual(currentAuthors))
            {
                changes["authors"] = new { from = currentAuthors, to = newAuthors };
            }

            var oldSeries = currentSeries is null ? null : new SeriesValue(currentSeries.Name, currentPosition);
            var newSeries = seriesRow is null ? null : new SeriesValue(seriesRow.Name, series!.Position);
            if (oldSeries != newSeries)
            {
                changes["series"] = new { from = oldSeries, to = newSeries };
            }

            var oldTags = currentTags.Order(StringComparer.Ordinal).ToList();
            var newTags = wantedTags.Select(t => t.Name).Order(StringComparer.Ordinal).ToList();
            if (!newTags.SequenceEqual(oldTags))
            {
                changes["tags"] = new { from = oldTags, to = newTags };
            }

            AddChange(changes, "description", currentDescription, description);
            if (work.SectionId != currentSectionId)
            {
                var sectionIds = new[] { currentSectionId, work.SectionId }.OfType<Guid>().ToList();
                var sections = await db.Sections
                    .Where(s => sectionIds.Contains(s.Id))
                    .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);
                changes["section"] = new
                {
                    from = currentSectionId is { } oldId ? new SectionValue(oldId, sections[oldId]) : null,
                    to = work.SectionId is { } newId ? new SectionValue(newId, sections[newId]) : null,
                };
            }

            // The files follow the edit when the folder its first author and title make is another one, and they move
            // before the save, so a file that cannot move saves nothing.
            var moves = await MoveFilesAsync(
                work,
                Filing.WorkFolder(currentAuthors.FirstOrDefault(), currentTitle),
                Filing.WorkFolder(newAuthors.FirstOrDefault(), title),
                title,
                cancellationToken);
            try
            {
                // The entry is stored by the same save as the edit. An edit that changes nothing leaves none.
                if (changes.Count > 0)
                {
                    await audit.AddAsync(
                        user,
                        AuditActions.WorkEdited,
                        new AuditSubject(AuditSubjectTypes.Work, work.Id, title),
                        new { changes },
                        cancellationToken: cancellationToken);
                }

                await db.SaveChangesAsync(cancellationToken);

                // What the edit left with no work goes, once the links to it have been saved away.
                await RemoveUnusedAsync(linkedPeople, currentSeries?.Id, linkedTags, cancellationToken);

                // The search vector is built from the saved rows, so it is rebuilt after the save.
                await _searchIndex.RebuildAsync([work.Id], cancellationToken);

                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await UndoUnlessSavedAsync(transaction, moves);
                throw;
            }

            try
            {
                WorkRefiling.RemoveEmptied(moves.Moves, storage);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "A folder the edit of work {WorkId} emptied could not be removed.", work.Id);
            }

            return WorkEditOutcome.Saved;
        });
    }

    /// <summary>
    /// Deletes a work, when the user may change it: its items, parts and links go with it, and so does each person,
    /// series and tag nothing else names. The rows go in one transaction, under the ingest lock, and the work's files
    /// go after it commits, under the lock again. A file that cannot be deleted is logged and left, since ingest
    /// treats a book file no part points at as free.
    /// </summary>
    public async Task<WorkDeleteOutcome> DeleteAsync(ClaimsPrincipal user, Guid id, CancellationToken cancellationToken)
    {
        var grants = await access.ForAsync(user, cancellationToken);

        // Kept across the strategy's passes: a commit that landed and then reported a failure is run again, and that
        // pass finds no work to read the paths from.
        List<string>? paths = null;
        var outcome = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await IngestLock.TakeAsync(db, cancellationToken);

            var (work, refusal) = await LoadToChangeAsync(user, grants, id, cancellationToken);
            if (work is null)
            {
                // A work that is gone after an earlier pass reached its commit was deleted by that pass.
                if (refusal == Refusal.Forbidden)
                {
                    return WorkDeleteOutcome.Forbidden(NotYoursMessage);
                }

                return paths is null ? WorkDeleteOutcome.NotFound : WorkDeleteOutcome.Deleted;
            }

            var linkedPeople = work.Contributors.Select(c => c.PersonId).Distinct().ToList();
            var linkedTags = work.Tags.Select(t => t.Id).ToList();
            var people = await db.People
                .Where(p => linkedPeople.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
            var authors = AuthorLinks(work).Select(c => people[c.PersonId]).ToList();
            var found = await PartsOf(work.Id).Select(part => part.Path).ToListAsync(cancellationToken);
            if (work.CoverPath is { } cover)
            {
                found.Add(cover);
            }

            found = [.. found.Distinct(StringComparer.Ordinal)];

            await audit.AddAsync(
                user,
                AuditActions.WorkDeleted,
                new AuditSubject(AuditSubjectTypes.Work, work.Id, work.Title),
                new { title = work.Title, authors, files = found },
                cancellationToken: cancellationToken);
            var seriesId = work.SeriesId;
            db.Works.Remove(work);
            await db.SaveChangesAsync(cancellationToken);

            // The people, series and tags are removed once the work's links to them are gone.
            await RemoveUnusedAsync(linkedPeople, seriesId, linkedTags, cancellationToken);
            paths = found;
            await transaction.CommitAsync(cancellationToken);
            return WorkDeleteOutcome.Deleted;
        });

        if (outcome.Status == WorkDeleteStatus.Deleted)
        {
            await DeleteFilesAsync(id, paths!);
        }

        return outcome;
    }

    /// <summary>
    /// Deletes the files a deleted work had, and the folders that empties. The commit that removed the rows let go of
    /// the ingest lock, so an ingest may since have filed a book over one of the paths: the lock is taken again, on a
    /// connection of its own, and a path a part or a cover points at by then is another work's and stays. The request
    /// being cancelled does not stop it, since the rows are already gone. A failure is logged and the files are left.
    /// </summary>
    private async Task DeleteFilesAsync(Guid workId, IReadOnlyList<string> paths)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var fresh = scope.ServiceProvider.GetRequiredService<NookDbContext>();
            await fresh.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var held = await fresh.Database.BeginTransactionAsync(CancellationToken.None);
                await IngestLock.TakeAsync(fresh, CancellationToken.None);

                var claimed = new List<string>();
                var matcher = new WorkMatcher(fresh);
                foreach (var folder in paths.Select(Filing.FolderOf).Distinct(StringComparer.Ordinal))
                {
                    claimed.AddRange(await matcher.ClaimedPathsAsync(folder, CancellationToken.None));
                }

                var lowered = paths.Select(path => path.ToLowerInvariant()).ToList();
                claimed.AddRange(await fresh.Works
                    .Where(w => w.CoverPath != null && lowered.Contains(w.CoverPath.ToLower()))
                    .Select(w => w.CoverPath!)
                    .ToListAsync(CancellationToken.None));

                var free = paths.Where(path => !Filing.IsClaimed(claimed, path)).ToList();
                foreach (var path in free)
                {
                    try
                    {
                        File.Delete(storage.LibraryFile(path));
                    }
                    catch (DirectoryNotFoundException)
                    {
                        // The file is gone already, with its folder.
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        logger.LogWarning(
                            exception, "The file {Path} of the deleted work {WorkId} could not be deleted.", path, workId);
                    }
                }

                try
                {
                    WorkRefiling.RemoveEmptied(free, storage);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(exception, "A folder the deleted work {WorkId} emptied could not be removed.", workId);
                }
            });
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception, "The files of the deleted work {WorkId} could not be deleted, so they are left in place.", workId);
        }
    }

    /// <summary>Why a work is not handed over to be changed.</summary>
    private enum Refusal
    {
        None,
        NotFound,
        Forbidden,
    }

    /// <summary>
    /// The work with its contributor and tag links, for a user who may change it. A work the user cannot see is
    /// reported the same way as one that does not exist.
    /// </summary>
    private async Task<(Work? Work, Refusal Refusal)> LoadToChangeAsync(
        ClaimsPrincipal user, SectionGrants grants, Guid id, CancellationToken cancellationToken)
    {
        var work = await db.Works
            .VisibleTo(grants)
            .Include(w => w.Contributors)
            .Include(w => w.Tags)
            .SingleOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (work is null)
        {
            return (null, Refusal.NotFound);
        }

        return MayChange(user, work.OwnerId) ? (work, Refusal.None) : (null, Refusal.Forbidden);
    }

    /// <summary>The work's author links, in the order the authors are shown.</summary>
    private static List<WorkContributor> AuthorLinks(Work work) =>
        [.. work.Contributors.Where(c => c.Role == ContributorRole.Author).OrderBy(c => c.Order).ThenBy(c => c.Id)];

    /// <summary>The parts of every item of the work, oldest first.</summary>
    private IQueryable<Part> PartsOf(Guid workId) =>
        from part in db.Parts
        join item in db.Items on part.ItemId equals item.Id
        where item.WorkId == workId
        orderby part.CreatedAt, part.Id
        select part;

    /// <summary>
    /// When the folder the new first author and title make is not the one the old ones made, moves the work's parts and
    /// cover into the new one and points the rows at the new paths, leaving the rows to be saved. Another work's
    /// folder is never taken: a name a part of another work is in gets a number. When a file cannot be moved, the ones
    /// moved go back and the error is thrown.
    /// </summary>
    private async Task<MovedFiles> MoveFilesAsync(
        Work work, string oldFolder, string folder, string newTitle, CancellationToken cancellationToken)
    {
        if (Filing.IsSamePath(oldFolder, folder))
        {
            return MovedFiles.None;
        }

        var parts = await PartsOf(work.Id).ToListAsync(cancellationToken);
        var ownPaths = parts.Select(p => p.Path).ToList();
        var claimed = (await _matcher.ClaimedPathsAsync(Filing.FolderOf(folder), cancellationToken))
            .Where(path => !Filing.IsClaimed(ownPaths, path))
            .ToList();
        var moves = WorkRefiling.Plan(folder, newTitle, ownPaths, work.CoverPath, claimed, storage);
        if (moves.Count == 0)
        {
            return MovedFiles.None;
        }

        WorkRefiling.Apply(moves, storage);

        // A file already in place has no move, so each row finds its own by the path it had.
        foreach (var part in parts)
        {
            if (moves.FirstOrDefault(m => m.FromRelative == part.Path) is { } move)
            {
                part.Path = move.ToRelative;
            }
        }

        var first = moves[0];
        var firstPart = parts.FirstOrDefault(p => p.Path == first.ToRelative);
        if (work.CoverPath is { } cover && moves.FirstOrDefault(m => m.FromRelative == cover) is { } coverMove)
        {
            work.CoverPath = coverMove.ToRelative;
        }

        return new MovedFiles(moves, work.Id, firstPart?.Id);
    }

    /// <summary>
    /// After a save or commit that threw, puts the files back unless the edit is saved. The edit's transaction is
    /// given up first, which lets go of the ingest lock, and the lock is taken again on a connection of its own: a
    /// commit that landed, or a connection that broke, leaves no way to tell otherwise. Under the lock the files go
    /// back unless the row behind the first move is stored under its new path, apart from a file whose new path
    /// another work's part has claimed in between. When the wait for the lock runs out or nothing says, the files stay
    /// where they are and that is logged.
    /// </summary>
    private async Task UndoUnlessSavedAsync(IDbContextTransaction transaction, MovedFiles moved)
    {
        if (moved.Moves.Count == 0)
        {
            return;
        }

        try
        {
            await IngestLock.RetakeAfterFailedSaveAsync(transaction, scopes, logger, async fresh =>
            {
                var first = moved.Moves[0];
                var saved = moved.FirstPartId is { } partId
                    ? await fresh.Parts.AnyAsync(p => p.Id == partId && p.Path == first.ToRelative, CancellationToken.None)
                    : await fresh.Works.AnyAsync(w => w.Id == moved.WorkId && w.CoverPath == first.ToRelative, CancellationToken.None);
                if (saved)
                {
                    return;
                }

                // While the lock was let go an ingest may have filed over a moved file and claimed its path. That
                // file is the ingest's now and stays where it is.
                var folders = moved.Moves.Select(m => Filing.FolderOf(m.ToRelative)).Distinct(StringComparer.Ordinal);
                var claimed = new List<string>();
                foreach (var folder in folders)
                {
                    claimed.AddRange(await new WorkMatcher(fresh).ClaimedPathsAsync(folder, CancellationToken.None));
                }

                var back = WorkRefiling.NotClaimed(moved.Moves, claimed);
                if (back.Count < moved.Moves.Count)
                {
                    logger.LogError(
                        "Another book was filed over {Count} of the files of work {WorkId} while its failed edit was undone, so they are not put back.",
                        moved.Moves.Count - back.Count,
                        moved.WorkId);
                }

                WorkRefiling.Undo(back, storage);

                // The folders the move made are empty now.
                WorkRefiling.RemoveEmptied(
                    [.. back.Select(m => new WorkRefiling.FileMove(m.ToRelative, m.FromRelative))], storage);
            });
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Could not put the files of work {WorkId} back after its edit failed, so they are left where they are.",
                moved.WorkId);
        }
    }

    private static void AddChange(Dictionary<string, object?> changes, string field, string? from, string? to)
    {
        if (from != to)
        {
            changes[field] = new { from, to };
        }
    }

    private sealed record SeriesValue(string Name, decimal? Position);

    private sealed record SectionValue(Guid Id, string Name);

    /// <summary>The files an edit moved, the work they belong to, and the part behind the first move, if it is a part.</summary>
    private sealed record MovedFiles(IReadOnlyList<WorkRefiling.FileMove> Moves, Guid WorkId, Guid? FirstPartId)
    {
        public static readonly MovedFiles None = new([], Guid.Empty, null);
    }

    /// <summary>
    /// A message when the list of names holds more than <paramref name="most"/>, or a name over the limit, naming
    /// the <paramref name="kind"/> of name, such as "author"; null when it is within the limits.
    /// </summary>
    private static string? TooLong(List<string> names, int most, string kind)
    {
        if (names.Count > most)
        {
            return $"A book has at most {most} {kind}s.";
        }

        return names.Any(n => n.Length > MaxNameLength) ? $"A {kind}'s name is over {MaxNameLength} characters." : null;
    }

    /// <summary>
    /// Deletes the people, the series and the tags that were linked to the edited work and that no work names now.
    /// Run after the save, so the work's own links to them are gone.
    /// </summary>
    private async Task RemoveUnusedAsync(
        List<Guid> peopleIds, Guid? seriesId, List<Guid> tagIds, CancellationToken cancellationToken)
    {
        await db.People
            .Where(p => peopleIds.Contains(p.Id) && !db.WorkContributors.Any(c => c.PersonId == p.Id))
            .ExecuteDeleteAsync(cancellationToken);
        if (seriesId is { } series)
        {
            await db.Series
                .Where(s => s.Id == series && !db.Works.Any(w => w.SeriesId == s.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        await db.Tags
            .Where(t => tagIds.Contains(t.Id) && !db.Works.Any(w => w.Tags.Any(x => x.Id == t.Id)))
            .ExecuteDeleteAsync(cancellationToken);
    }
}

/// <summary>What an edit came to.</summary>
public enum WorkEditStatus
{
    Saved,
    NotFound,
    Forbidden,

    /// <summary>A value is over a limit, or names something that does not exist.</summary>
    Invalid,
}

/// <summary>What an edit came to: its status and, for an invalid one, the field at fault and why.</summary>
public record WorkEditOutcome(WorkEditStatus Status, string? Field = null, string? Message = null)
{
    public static readonly WorkEditOutcome Saved = new(WorkEditStatus.Saved);

    public static readonly WorkEditOutcome NotFound = new(WorkEditStatus.NotFound);

    public static WorkEditOutcome Forbidden(string message) => new(WorkEditStatus.Forbidden, Message: message);

    public static WorkEditOutcome Invalid(string field, string message) => new(WorkEditStatus.Invalid, field, message);
}

/// <summary>What a delete came to.</summary>
public enum WorkDeleteStatus
{
    Deleted,
    NotFound,
    Forbidden,
}

/// <summary>What a delete came to: its status and, for a refused one, why.</summary>
public record WorkDeleteOutcome(WorkDeleteStatus Status, string? Message = null)
{
    public static readonly WorkDeleteOutcome Deleted = new(WorkDeleteStatus.Deleted);

    public static readonly WorkDeleteOutcome NotFound = new(WorkDeleteStatus.NotFound);

    public static WorkDeleteOutcome Forbidden(string message) => new(WorkDeleteStatus.Forbidden, message);
}
