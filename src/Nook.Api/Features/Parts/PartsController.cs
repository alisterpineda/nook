using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;
using Nook.Api.Features.Auth;
using Nook.Api.Features.Sections;
using Nook.Api.Storage;

namespace Nook.Api.Features.Parts;

[ApiController]
[Route("api/parts")]
[Authorize(Policy = Policies.Catalog)]
public partial class PartsController(
    NookDbContext db, NookStorage storage, SectionAccess access, ILogger<PartsController> logger)
    : ControllerBase
{
    /// <summary>
    /// The part's file as a download, with the part's media type and the file's name on disk. A range of its bytes can
    /// be asked for. Only a caller whose level in the work's area is download gets it.
    /// </summary>
    [EnableRateLimiting(RateLimits.AnonymousContent)]
    [HttpGet("{id:guid}/file")]
    [ProducesResponseType<Stream>(StatusCodes.Status200OK, "application/epub+zip")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFile(Guid id, CancellationToken cancellationToken)
    {
        // A part of a work the caller may not download is reported the same way as one that does not exist.
        var grants = await access.ForAsync(User, cancellationToken);
        var part = await (
                from p in db.Parts
                join i in db.Items on p.ItemId equals i.Id
                join w in db.Works.VisibleTo(grants, ContentAccess.Download) on i.WorkId equals w.Id
                where p.Id == id
                select new { p.Path, p.MediaType })
            .SingleOrDefaultAsync(cancellationToken);

        if (part is null)
        {
            return NotFound();
        }

        var path = storage.LibraryFile(part.Path);

        // PhysicalFile reads the disk only when the response is written, which is too late for a 404.
        if (!System.IO.File.Exists(path))
        {
            LogPartFileMissing(logger, id, path);
            return NotFound();
        }

        var fileName = part.Path.Split('/')[^1];
        return PhysicalFile(path, part.MediaType, fileName, enableRangeProcessing: true);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The file of part {PartId} is missing from {Path}.")]
    private static partial void LogPartFileMissing(ILogger logger, Guid partId, string path);
}
