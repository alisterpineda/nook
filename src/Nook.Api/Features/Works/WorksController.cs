using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;
using Nook.Api.Features.Libraries;
using Nook.Api.Storage;

namespace Nook.Api.Features.Works;

[ApiController]
[Route("api/works")]
public partial class WorksController(
    NookDbContext db,
    NookStorage storage,
    CoverResizer resizer,
    CoverCache coverCache,
    ILogger<WorksController> logger) : ControllerBase
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
    [ProducesResponseType<WorkDetailResponse>(StatusCodes.Status200OK)]
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

    /// <summary>
    /// The work's cover, as the file it was saved as, or as a JPEG at a size: <c>small</c> fits inside 400 × 600 pixels
    /// and <c>large</c> inside 1000 × 1500. A cover is never enlarged, and one that cannot be resized is served as the
    /// file it was saved as at any size. The response is served so a browser runs nothing in it, since a cover can be
    /// an SVG, which can hold script.
    /// </summary>
    [HttpGet("{id:guid}/cover")]
    [ProducesResponseType<Stream>(
        StatusCodes.Status200OK,
        CoverMediaTypes.Jpeg,
        CoverMediaTypes.Png,
        CoverMediaTypes.Gif,
        CoverMediaTypes.Webp,
        CoverMediaTypes.Svg,
        CoverMediaTypes.Fallback)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCover(
        Guid id, [FromQuery] string? size, CancellationToken cancellationToken)
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

        // A work the user cannot see is reported the same way as one that does not exist.
        var cover = await (
                from w in db.Works.VisibleTo(User)
                join l in db.Libraries on w.LibraryId equals l.Id
                where w.Id == id
                select new { w.CoverPath, w.CoverHash, Library = l })
            .SingleOrDefaultAsync(cancellationToken);

        if (cover?.CoverPath is not { } coverPath)
        {
            return NotFound();
        }

        var path = storage.LibraryFile(cover.Library, coverPath);
        var cachedPath = coverSize is { } cachedSize && cover.CoverHash is { } coverHash
            ? coverCache.PathOf(coverHash, cachedSize)
            : null;
        if (cachedPath is not null && TryOpen(cachedPath) is { } cached)
        {
            return File(cached, CoverMediaTypes.Jpeg);
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
                return File(resized, CoverMediaTypes.Jpeg);
            }

            // Nothing is cached, so the next request for this size tries again.
            LogCoverNotResized(logger, id, coverPath);
        }

        return PhysicalFile(path, CoverMediaTypes.Of(path));
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
