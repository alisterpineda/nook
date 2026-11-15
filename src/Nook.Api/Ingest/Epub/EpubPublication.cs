using Nook.Api.Data;
using VersOne.Epub;

namespace Nook.Api.Ingest.Epub;

/// <summary>
/// An EPUB described for reading: what the book says about itself, its files in reading order and the rest, and its
/// table of contents. It holds no open book; a file's bytes are read from the archive by its entry name.
/// </summary>
/// <param name="Title">The book's first non-blank title, trimmed, or null when it gives none.</param>
/// <param name="Identifier">The package's unique identifier, else its first identifier, or null when it has none.</param>
/// <param name="Language">The book's first language, or null when it gives none.</param>
/// <param name="Authors">The creators who are authors, as ingest reads them, in the order the book lists them.</param>
/// <param name="ReadingOrder">The spine's files in spine order.</param>
/// <param name="Resources">Every other file the package manifest lists, in manifest order.</param>
/// <param name="Toc">The table of contents, from the nav document in EPUB 3 and the NCX in EPUB 2.</param>
public sealed record EpubPublication(
    string? Title,
    string? Identifier,
    string? Language,
    IReadOnlyList<string> Authors,
    IReadOnlyList<EpubResource> ReadingOrder,
    IReadOnlyList<EpubResource> Resources,
    IReadOnlyList<EpubTocEntry> Toc);

/// <summary>A file the package manifest lists and the archive holds.</summary>
/// <param name="EntryName">The file's exact entry name in the archive.</param>
/// <param name="Href">The entry name with each path segment percent-encoded, relative to the archive's root.</param>
/// <param name="MediaType">The media type the package manifest declares for it.</param>
/// <param name="CompressedLength">The entry's size in the archive, compressed.</param>
public sealed record EpubResource(string EntryName, string Href, string MediaType, long CompressedLength);

/// <summary>An entry in the table of contents.</summary>
/// <param name="Title">The entry's text, trimmed.</param>
/// <param name="Href">
/// The target file's href, as a resource's is, without the fragment. A heading with no link of its own takes its
/// first child's.
/// </param>
/// <param name="Fragment">The fragment the entry points to within the file, without the <c>#</c>, or null.</param>
/// <param name="Children">The entries nested under it.</param>
public sealed record EpubTocEntry(string Title, string Href, string? Fragment, IReadOnlyList<EpubTocEntry> Children);

/// <summary>Reads an EPUB into an <see cref="EpubPublication"/>.</summary>
public static class EpubPublicationReader
{
    /// <summary>
    /// Opens the book with the same checks and reader options as ingest and describes it. A manifest item that is
    /// remote or missing from the archive is left out, and so is a spine entry or table-of-contents entry that leads
    /// nowhere.
    /// </summary>
    /// <exception cref="UnreadableBookException">The book is one ingest would refuse.</exception>
    public static async Task<EpubPublication> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        using var book = await EpubFormatHandler.OpenAsync(path, cancellationToken);
        var package = book.Schema.Package;
        var metadata = package.Metadata;

        var localFiles = new Dictionary<string, EpubLocalContentFileRef>(StringComparer.Ordinal);
        foreach (var file in book.Content.AllFiles.Local)
        {
            localFiles.TryAdd(file.Key, file);
        }

        // Manifest items by id, and the same resources by entry name for the table of contents to find.
        var byId = new Dictionary<string, EpubResource>(StringComparer.Ordinal);
        var byEntryName = new Dictionary<string, EpubResource>(StringComparer.Ordinal);
        var inManifestOrder = new List<EpubResource>();
        foreach (var item in package.Manifest.Items)
        {
            if (!localFiles.TryGetValue(item.Href, out var file) || file.ContentFileEntry is not { } entry)
            {
                continue;
            }

            if (!byEntryName.TryGetValue(file.FilePath, out var resource))
            {
                resource = new EpubResource(
                    file.FilePath, EncodeHref(file.FilePath), MediaTypeOf(item.MediaType), entry.CompressedLength);
                byEntryName.Add(resource.EntryName, resource);
                inManifestOrder.Add(resource);
            }

            byId.TryAdd(item.Id, resource);
        }

        var readingOrder = package.Spine.Items
            .Select(itemRef => byId.GetValueOrDefault(itemRef.IdRef))
            .OfType<EpubResource>()
            .ToList();
        var inSpine = readingOrder.Select(resource => resource.EntryName).ToHashSet(StringComparer.Ordinal);
        var resources = inManifestOrder.Where(resource => !inSpine.Contains(resource.EntryName)).ToList();

        // An EPUB 3 nav comes back wrapped in one heading entry for the nav itself, with no link; the contents are
        // what it holds.
        var navigation = (book.GetNavigation() ?? [])
            .SelectMany(item => item.Type == EpubNavigationItemType.HEADER && item.Link is null
                ? item.NestedItems ?? []
                : [item]);
        var toc = navigation.Select(item => TocEntry(item, byEntryName)).OfType<EpubTocEntry>().ToList();

        var uniqueIdentifier = metadata.Identifiers.FirstOrDefault(identifier => identifier.Id == package.UniqueIdentifier)
            ?? metadata.Identifiers.FirstOrDefault();

        return new EpubPublication(
            Title: EpubFormatHandler.FirstValue(metadata.Titles.Select(title => title.Title)),
            Identifier: EpubFormatHandler.FirstValue([uniqueIdentifier?.Identifier]),
            Language: EpubFormatHandler.FirstValue(metadata.Languages.Select(language => language.Language)),
            Authors: EpubFormatHandler.ReadCreators(metadata.Creators, metadata.MetaItems)
                .Where(creator => creator.Role == ContributorRole.Author)
                .Select(creator => creator.Name)
                .ToList(),
            ReadingOrder: readingOrder,
            Resources: resources,
            Toc: toc);
    }

    /// <summary>
    /// The entry and its children. The library gives the target already percent-decoded and resolved against the
    /// navigation file's folder. An entry with neither a link nor a child that has one is left out.
    /// </summary>
    private static EpubTocEntry? TocEntry(EpubNavigationItemRef item, Dictionary<string, EpubResource> byEntryName)
    {
        var children = (item.NestedItems ?? [])
            .Select(child => TocEntry(child, byEntryName))
            .OfType<EpubTocEntry>()
            .ToList();

        string href;
        string? fragment;
        if (item.Link?.ContentFilePath is { } filePath)
        {
            href = byEntryName.TryGetValue(filePath, out var resource) ? resource.Href : EncodeHref(filePath);
            fragment = string.IsNullOrEmpty(item.Link.Anchor) ? null : item.Link.Anchor;
        }
        else if (children.FirstOrDefault() is { } first)
        {
            (href, fragment) = (first.Href, first.Fragment);
        }
        else
        {
            return null;
        }

        return new EpubTocEntry(item.Title?.Trim() ?? "", href, fragment, children);
    }

    /// <summary>An archive entry name as an href: each path segment percent-encoded.</summary>
    private static string EncodeHref(string entryName) =>
        string.Join('/', entryName.Split('/').Select(Uri.EscapeDataString));

    /// <summary>The declared media type, or <c>application/octet-stream</c> when the manifest item declares none.</summary>
    private static string MediaTypeOf(string? declared) =>
        string.IsNullOrWhiteSpace(declared) ? "application/octet-stream" : declared.Trim();
}
