using System.IO.Compression;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using Nook.Api.Data;
using Nook.Api.Features.Auth;
using Nook.Api.Features.Libraries;
using Nook.Api.Ingest;
using Nook.Api.Ingest.Epub;
using Nook.Api.Storage;

namespace Nook.Api.Features.Publications;

/// <summary>
/// An EPUB item as a Readium publication. The manifest here needs the sign-in cookie; its self link puts the
/// publication under a token path, so the files it lists are reached by the token alone.
/// </summary>
/// <remarks>
/// A reader's frames do not always send the cookie, so everything under the token path is reached without it. Each
/// such request checks again that the token's user exists and can see the work. Anything the token does not reach is
/// reported the same way as an item that does not exist.
/// </remarks>
[ApiController]
[Route("api/items/{itemId:guid}/publication")]
public partial class PublicationsController(
    NookDbContext db,
    NookStorage storage,
    PublicationCache publications,
    PublicationTokens tokens,
    UserManager<NookUser> users,
    IUserClaimsPrincipalFactory<NookUser> principals,
    ILogger<PublicationsController> logger)
    : ControllerBase
{
    /// <summary>How long the browser may keep a book's file: as long as a token is sure to last.</summary>
    private const string ResourceCacheControl = "private, max-age=86400";

    /// <summary>
    /// The policy on a book's file: opened on its own, it runs no script, loads nothing from anywhere, and keeps only
    /// inline styles and inline images.
    /// </summary>
    private const string ResourceContentSecurityPolicy =
        "sandbox; default-src 'none'; style-src 'unsafe-inline'; img-src data:";

    /// <summary>The JavaScript MIME types, as the WHATWG MIME Sniffing standard lists them.</summary>
    private static readonly HashSet<string> ScriptMediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/ecmascript",
        "application/javascript",
        "application/x-ecmascript",
        "application/x-javascript",
        "text/ecmascript",
        "text/javascript",
        "text/javascript1.0",
        "text/javascript1.1",
        "text/javascript1.2",
        "text/javascript1.3",
        "text/javascript1.4",
        "text/javascript1.5",
        "text/jscript",
        "text/livescript",
        "text/x-ecmascript",
        "text/x-javascript",
    };

    /// <summary>
    /// The item's publication manifest, for an EPUB item with exactly one part. Its self link is the absolute address
    /// of the same manifest under a token path that is the same for the user and item all day, and every other href is
    /// relative to it.
    /// </summary>
    [HttpGet("manifest.json")]
    [ProducesResponseType<PublicationManifest>(StatusCodes.Status200OK, PublicationManifest.MediaType)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetManifest(Guid itemId, CancellationToken cancellationToken)
    {
        var userId = users.GetUserId(User);
        if (userId is null || await ReadAsync(itemId, User, cancellationToken) is not { } book)
        {
            return NotFound();
        }

        return Manifest(itemId, Uri.EscapeDataString(tokens.Issue(itemId, userId)), book);
    }

    /// <summary>The same manifest as the signed-in one, reached by its token alone.</summary>
    [AllowAnonymous]
    [HttpGet("t/{token}/manifest.json")]
    [ProducesResponseType<PublicationManifest>(StatusCodes.Status200OK, PublicationManifest.MediaType)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTokenManifest(Guid itemId, string token, CancellationToken cancellationToken)
    {
        SetTokenPathHeaders();

        return await ReadByTokenAsync(itemId, token, cancellationToken) is { } book
            ? Manifest(itemId, token, book)
            : NotFound();
    }

    /// <summary>
    /// The publication's positions list, reached by its token alone: one position per 1024 compressed bytes of each
    /// reading-order file, and at least one.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("t/{token}/" + PublicationManifest.PositionsHref)]
    [ProducesResponseType<PublicationPositions>(StatusCodes.Status200OK, PublicationManifest.PositionListMediaType)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPositions(Guid itemId, string token, CancellationToken cancellationToken)
    {
        SetTokenPathHeaders();

        if (await ReadByTokenAsync(itemId, token, cancellationToken) is not { } book)
        {
            return NotFound();
        }

        return new ObjectResult(PublicationPositions.For(book.Publication))
        {
            ContentTypes = { PublicationManifest.PositionListMediaType },
        };
    }

    /// <summary>
    /// A file the package manifest lists, reached by its token alone at its href, with the media type the package
    /// manifest declares. A script is never served.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("t/{token}/{**path}")]
    [ProducesResponseType<Stream>(StatusCodes.Status200OK, "application/octet-stream")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetResource(
        Guid itemId, string token, string path, CancellationToken cancellationToken)
    {
        SetTokenPathHeaders();

        if (await ReadByTokenAsync(itemId, token, cancellationToken) is not { } book)
        {
            return NotFound();
        }

        // The path arrives decoded, as the archive names the file, except for an encoded slash, which no listed file
        // matches.
        var resource = book.Publication.ReadingOrder
            .Concat(book.Publication.Resources)
            .FirstOrDefault(resource => resource.EntryName == path);
        if (resource is null || IsScript(resource))
        {
            return NotFound();
        }

        if (OpenArchive(itemId, book.Path) is not { } archive)
        {
            return NotFound();
        }

        if (archive.GetEntry(resource.EntryName) is not { } entry)
        {
            archive.Dispose();
            LogResourceMissing(logger, itemId, resource.EntryName, book.Path);
            return NotFound();
        }

        Response.RegisterForDispose(archive);
        Response.ContentLength = entry.Length;
        Response.KeepCacheControl(ResourceCacheControl);
        Response.Headers.ContentSecurityPolicy = ResourceContentSecurityPolicy;
        return File(entry.Open(), ContentTypeOf(resource));
    }

    private ObjectResult Manifest(Guid itemId, string token, Book book)
    {
        var self = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/api/items/{itemId}/publication/t/{token}/manifest.json";

        return new ObjectResult(PublicationManifest.For(book.Publication, self, book.WorkTitle))
        {
            ContentTypes = { PublicationManifest.MediaType },
        };
    }

    /// <summary>
    /// Headers for every answer under the token path, a refusal included: the browser takes each file only as the type
    /// it is served with, and a link followed out of a book does not carry the token to another site.
    /// </summary>
    private void SetTokenPathHeaders()
    {
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }

    /// <summary>
    /// Whether the file is a script, by its declared media type, ignoring case and parameters, or by its extension. A
    /// book's script is never served, so a page cannot load it even from a policy that would allow it.
    /// </summary>
    private static bool IsScript(EpubResource resource)
    {
        var mediaType = resource.MediaType.Split(';', 2)[0].Trim();
        return ScriptMediaTypes.Contains(mediaType)
            || resource.EntryName.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
            || resource.EntryName.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The media type the package manifest declares, or <c>application/octet-stream</c> when what it declares is not
    /// a media type.
    /// </summary>
    private static string ContentTypeOf(EpubResource resource) =>
        MediaTypeHeaderValue.TryParse(resource.MediaType, out _) ? resource.MediaType : "application/octet-stream";

    private sealed record Book(EpubPublication Publication, string WorkTitle, string Path);

    /// <summary>
    /// The item's book for the token's user, when the token was issued to them for this item and is still current,
    /// and they still exist and can see the work.
    /// </summary>
    private async Task<Book?> ReadByTokenAsync(Guid itemId, string token, CancellationToken cancellationToken)
    {
        if (!tokens.TryRead(token, itemId, out var userId) || await users.FindByIdAsync(userId) is not { } user)
        {
            return null;
        }

        return await ReadAsync(itemId, await principals.CreateAsync(user), cancellationToken);
    }

    /// <summary>
    /// The item's book, when the item is an EPUB of a work <paramref name="user"/> can see, has exactly one part, and
    /// that part's file is on disk and readable. Anything else is reported the same way as an item that does not exist.
    /// </summary>
    private async Task<Book?> ReadAsync(Guid itemId, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var item = await (
                from i in db.Items
                join w in db.Works.VisibleTo(user) on i.WorkId equals w.Id
                join l in db.Libraries on w.LibraryId equals l.Id
                where i.Id == itemId && i.Format == ItemFormat.Epub
                select new
                {
                    WorkTitle = w.Title,
                    Library = l,
                    Paths = db.Parts.Where(p => p.ItemId == i.Id).Select(p => p.Path).Take(2).ToList(),
                })
            .SingleOrDefaultAsync(cancellationToken);

        if (item is not { Paths: [var partPath] })
        {
            return null;
        }

        var path = storage.LibraryFile(item.Library, partPath);
        try
        {
            return new Book(await publications.GetAsync(path, cancellationToken), item.WorkTitle, path);
        }
        catch (FileNotFoundException)
        {
            LogBookMissing(logger, itemId, path);
            return null;
        }
        catch (UnreadableBookException exception)
        {
            LogBookUnreadable(logger, exception, itemId, path);
            return null;
        }
    }

    /// <summary>
    /// The book's archive, opened afresh for this request, or null when it has gone or is no longer an archive since it
    /// was described.
    /// </summary>
    private ZipArchive? OpenArchive(Guid itemId, string path)
    {
        FileStream? file = null;
        try
        {
            file = NookStorage.OpenFile(path);
            return new ZipArchive(file, ZipArchiveMode.Read);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            LogBookMissing(logger, itemId, path);
            return null;
        }
        catch (InvalidDataException exception)
        {
            file?.Dispose();
            LogBookUnreadable(logger, exception, itemId, path);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The file of item {ItemId} is missing from {Path}.")]
    private static partial void LogBookMissing(ILogger logger, Guid itemId, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "The file of item {ItemId} at {Path} cannot be read as a book.")]
    private static partial void LogBookUnreadable(ILogger logger, Exception exception, Guid itemId, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "The entry {EntryName} of item {ItemId} is missing from {Path}.")]
    private static partial void LogResourceMissing(ILogger logger, Guid itemId, string entryName, string path);
}
