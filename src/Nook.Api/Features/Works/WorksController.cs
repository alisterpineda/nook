using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;
using Nook.Api.Features.Audit;
using Nook.Api.Features.Auth;
using Nook.Api.Features.Sections;
using Nook.Api.Storage;

namespace Nook.Api.Features.Works;

[ApiController]
[Route("api/works")]
[Authorize(Policy = Policies.Catalog)]
public partial class WorksController(
    NookDbContext db,
    NookStorage storage,
    CoverResizer resizer,
    CoverCache coverCache,
    SectionAccess access,
    ILogger<WorksController> logger) : ControllerBase
{
    /// <summary>One page of the works the caller can see.</summary>
    [HttpGet]
    public async Task<ActionResult<Page<WorkSummaryResponse>>> List(
        [FromQuery] WorkListQuery query, CancellationToken cancellationToken)
    {
        var works = db.Works.VisibleTo(await access.ForAsync(User, cancellationToken));
        if (query.SectionId is { } sectionId)
        {
            works = works.Where(w => w.SectionId == sectionId);
        }
        else if (query.NoSection)
        {
            works = works.Where(w => w.SectionId == null);
        }

        string? tsQuery = null;
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var words = SearchWords.Of(query.Search);
            if (words.Count == 0)
            {
                return new Page<WorkSummaryResponse>([], 0, query.Offset, query.Limit);
            }

            tsQuery = SearchQuery(words);
            var matching = tsQuery;
            works = works.Where(w => w.SearchVector!.Matches(EF.Functions.ToTsQuery("nook", matching)));
        }

        var total = await works.CountAsync(cancellationToken);

        // The id breaks ties, so a work never shows on two pages or on none.
        var sort = query.Sort ?? (tsQuery is null ? WorkSort.Title : WorkSort.Relevance);
        var ranking = tsQuery;
        works = sort switch
        {
            WorkSort.Relevance => works
                .OrderByDescending(w => w.SearchVector!.Rank(EF.Functions.ToTsQuery("nook", ranking!)))
                .ThenBy(w => w.SortTitle)
                .ThenBy(w => w.Id),
            WorkSort.Added => works.OrderByDescending(w => w.CreatedAt).ThenByDescending(w => w.Id),
            _ => works.OrderBy(w => w.SortTitle).ThenBy(w => w.Id),
        };

        var items = await works
            .Skip(query.Offset)
            .Take(query.Limit)
            .ToSummaries(db)
            .ToListAsync(cancellationToken);

        return new Page<WorkSummaryResponse>(items, total, query.Offset, query.Limit);
    }

    /// <summary>The words as a query where every one must match and the last matches as the start of a word.</summary>
    private static string SearchQuery(IReadOnlyList<string> words) => string.Join(" & ", words) + ":*";

    /// <summary>One work, with its contributors, tags, external ids and items.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<WorkDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkDetailResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        // A work the caller cannot see is reported the same way as one that does not exist.
        var grants = await access.ForAsync(User, cancellationToken);
        var work = await db.Works
            .VisibleTo(grants)
            .Where(w => w.Id == id)
            .Select(w => new WorkDetailResponse(
                w.Id,
                w.SectionId,
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
                w.UpdatedAt,
                // Set from the grants once the work is found, since they are not in the database.
                ContentAccess.None,
                null))
            .AsSplitQuery()
            .SingleOrDefaultAsync(cancellationToken);
        if (work is null)
        {
            return NotFound();
        }

        // Who added the work goes to signed-in users only: the name of whoever's upload was recorded first.
        string? addedBy = null;
        if (User.Identity?.IsAuthenticated == true)
        {
            addedBy = await db.AuditEntries
                .Where(e => e.Action == AuditActions.UploadAdded
                    && e.SubjectType == AuditSubjectTypes.Work
                    && e.SubjectId == id)
                .OrderBy(e => e.CreatedAt)
                .ThenBy(e => e.Id)
                .Select(e => e.ActorName)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return work with { Access = grants.In(work.SectionId), AddedBy = addedBy };
    }

    /// <summary>
    /// The work's cover, as the file it was saved as, or as a JPEG at a size: <c>small</c> fits inside 400 × 600 pixels
    /// and <c>large</c> inside 1000 × 1500. A cover is never enlarged, and one that cannot be resized is served as the
    /// file it was saved as at any size. The response is served so a browser runs nothing in it, since a cover can be
    /// an SVG, which can hold script. When <c>v</c> is the work's cover hash, the browser may keep the cover for good,
    /// since a new cover gives the work a new hash and so a new URL; otherwise it checks every time.
    /// </summary>
    [HttpGet("{id:guid}/cover")]
    [ProducesResponseType<Stream>(
        StatusCodes.Status200OK,
        CoverImageTypes.Jpeg,
        CoverImageTypes.Png,
        CoverImageTypes.Gif,
        CoverImageTypes.Webp,
        CoverImageTypes.Svg,
        CoverImageTypes.Fallback)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCover(
        Guid id, [FromQuery] string? size, [FromQuery] string? v, CancellationToken cancellationToken)
    {
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";

        // Bound as text, since an enum would also take the number of a member.
        CoverSize? coverSize = null;
        if (size is not null)
        {
            if (!CoverSizes.TryParse(size, out var parsed))
            {
                ModelState.AddModelError(nameof(size), $"The size must be {CoverSizes.Names}.");
                return ValidationProblem(ModelState);
            }
            coverSize = parsed;
        }

        // A work the caller cannot see is reported the same way as one that does not exist.
        var grants = await access.ForAsync(User, cancellationToken);
        var cover = await (
                from w in db.Works.VisibleTo(grants)
                where w.Id == id
                select new { w.CoverPath, w.CoverHash })
            .SingleOrDefaultAsync(cancellationToken);

        if (cover?.CoverPath is not { } coverPath)
        {
            return NotFound();
        }

        // The caching header goes only on a cover that is served, so a 404 is never kept.
        var keepForGood = v is not null && v == cover.CoverHash;

        var path = storage.LibraryFile(coverPath);
        var cachedPath = coverSize is { } cachedSize && cover.CoverHash is { } coverHash
            ? coverCache.PathOf(coverHash, cachedSize)
            : null;
        if (cachedPath is not null && TryOpen(cachedPath) is { } cached)
        {
            SetCoverCacheControl(keepForGood);
            return File(cached, CoverImageTypes.Jpeg);
        }

        // A cover gone from the library is not found, though a resized file still in the cache is served above.
        // PhysicalFile reads the disk only when the response is written, which is too late for a 404.
        if (!System.IO.File.Exists(path))
        {
            LogCoverMissing(logger, id, path);
            return NotFound();
        }

        if (coverSize is { } sized)
        {
            byte[]? resized;
            await using (var original = System.IO.File.OpenRead(path))
            {
                resized = resizer.TryResize(original, sized);
            }

            if (resized is not null)
            {
                if (cachedPath is not null)
                {
                    await TryKeepAsync(cachedPath, resized, cancellationToken);
                }
                SetCoverCacheControl(keepForGood);
                return File(resized, CoverImageTypes.Jpeg);
            }

            // Nothing is cached, so the next request for this size tries again.
            LogCoverNotResized(logger, id, coverPath);
        }

        SetCoverCacheControl(keepForGood);
        return PhysicalFile(path, CoverImageTypes.MediaTypeOf(path));
    }

    /// <summary>
    /// Lets the browser keep a cover for good when it was asked for by its current hash, and otherwise makes it check
    /// every time. Private, so a shared cache never keeps a cover, whether a signed-in user or a visitor asked for it.
    /// The file result that follows writes no <c>Cache-Control</c> of its own, and the header is set so that a renewal
    /// of the session cookie on the same response does not replace it.
    /// </summary>
    private void SetCoverCacheControl(bool keepForGood)
    {
        Response.KeepCacheControl(keepForGood
            ? "private, max-age=31536000, immutable"
            : "private, no-cache");
    }

    /// <summary>
    /// Keeps a resized cover in the cache. The cache only saves work, so a cover that cannot be kept, such as when the
    /// disk is full, is still served: the failure is logged and the next request resizes again.
    /// </summary>
    private async Task TryKeepAsync(string cachedPath, byte[] resized, CancellationToken cancellationToken)
    {
        try
        {
            await coverCache.WriteAsync(cachedPath, resized, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogCoverNotKept(logger, exception, cachedPath);
        }
    }

    /// <summary>The file opened for reading, or null when it is not there, such as after the cache was emptied.</summary>
    private static FileStream? TryOpen(string path)
    {
        try
        {
            return NookStorage.OpenFile(path);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The cover {CoverPath} of work {WorkId} is not an image that can be resized, so it is served as it is.")]
    private static partial void LogCoverNotResized(ILogger logger, Guid workId, string coverPath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not keep the resized cover {CachedPath}.")]
    private static partial void LogCoverNotKept(ILogger logger, Exception exception, string cachedPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "The cover of work {WorkId} is missing from {Path}.")]
    private static partial void LogCoverMissing(ILogger logger, Guid workId, string path);
}
