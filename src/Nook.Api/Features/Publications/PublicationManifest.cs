using System.Text.Json.Serialization;
using Nook.Api.Ingest.Epub;

namespace Nook.Api.Features.Publications;

/// <summary>
/// A book as a Readium Web Publication Manifest. Every href but the self link's is relative to the self link.
/// </summary>
/// <param name="Context">The JSON-LD context every Readium manifest names.</param>
/// <param name="Metadata">What the book says about itself.</param>
/// <param name="Links">The manifest's own absolute address and the positions list.</param>
/// <param name="ReadingOrder">The spine's files in spine order.</param>
/// <param name="Resources">Every other file the package manifest lists.</param>
/// <param name="Toc">The table of contents, with its nesting and each entry's fragment.</param>
public sealed record PublicationManifest(
    [property: JsonPropertyName("@context")] string Context,
    PublicationMetadata Metadata,
    IReadOnlyList<PublicationLink> Links,
    IReadOnlyList<PublicationLink> ReadingOrder,
    IReadOnlyList<PublicationLink> Resources,
    IReadOnlyList<PublicationLink> Toc)
{
    public const string MediaType = "application/webpub+json";

    public const string PositionListMediaType = "application/vnd.readium.position-list+json";

    /// <summary>Where the positions list is, relative to the self link: the href Readium's own servers give it.</summary>
    public const string PositionsHref = "~readium/positions.json";

    private const string ReadiumContext = "https://readium.org/webpub-manifest/context.jsonld";
    private const string EpubProfile = "https://readium.org/webpub-manifest/profiles/epub";
    private const string PositionListRel = "http://readium.org/position-list";

    /// <summary>The manifest of the publication whose own absolute address is <paramref name="selfHref"/>.</summary>
    /// <param name="fallbackTitle">The title to give when the book gives none, since Readium requires one.</param>
    public static PublicationManifest For(EpubPublication publication, string selfHref, string fallbackTitle) =>
        new(
            ReadiumContext,
            new PublicationMetadata(
                EpubProfile,
                publication.Title ?? fallbackTitle,
                publication.Identifier,
                publication.Language,
                publication.Authors.Count > 0 ? publication.Authors : null),
            [
                new PublicationLink(selfHref, MediaType, Rel: "self"),
                new PublicationLink(PositionsHref, PositionListMediaType, Rel: PositionListRel),
            ],
            publication.ReadingOrder.Select(Link).ToList(),
            publication.Resources.Select(Link).ToList(),
            publication.Toc.Select(TocLink).ToList());

    private static PublicationLink Link(EpubResource resource) => new(resource.Href, resource.MediaType);

    private static PublicationLink TocLink(EpubTocEntry entry) =>
        new(
            entry.Fragment is null ? entry.Href : $"{entry.Href}#{entry.Fragment}",
            Title: entry.Title,
            Children: entry.Children.Count > 0 ? entry.Children.Select(TocLink).ToList() : null);
}

/// <summary>A publication's metadata. A value the book does not give is left out.</summary>
/// <param name="ConformsTo">The Readium profile the publication follows.</param>
/// <param name="Title">The book's title, or the work's when the book gives none.</param>
/// <param name="Identifier">The book's unique identifier.</param>
/// <param name="Language">The book's first language.</param>
/// <param name="Author">The book's authors, in the order it lists them.</param>
public sealed record PublicationMetadata(
    string ConformsTo,
    string Title,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Identifier,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Language,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Author);

/// <summary>A link in a manifest. A value the link does not have is left out.</summary>
/// <param name="Href">Where it leads: absolute for the self link, relative to it for every other.</param>
/// <param name="Type">The media type of what it leads to.</param>
/// <param name="Rel">How it relates to the publication.</param>
/// <param name="Title">The text a table of contents shows for it.</param>
/// <param name="Children">The table-of-contents entries nested under it.</param>
public sealed record PublicationLink(
    string Href,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Type = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Rel = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Title = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<PublicationLink>? Children = null);
