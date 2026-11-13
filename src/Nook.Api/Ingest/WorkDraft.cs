using Nook.Api.Data;
using Nook.Api.Ingest.ExternalIds;

namespace Nook.Api.Ingest;

/// <summary>What a book file says its Work is, before anything in the database is looked at.</summary>
/// <param name="Title">The file's title, or one made from the uploaded file name when the file gives none.</param>
/// <param name="SortTitle">The file's sort title, or the title.</param>
/// <param name="Description">The file's first description.</param>
/// <param name="Publisher">The file's first publisher.</param>
/// <param name="Language">The file's first language.</param>
/// <param name="PublicationDate">The publication date, only when the file gives a complete date.</param>
/// <param name="Contributors">
/// The creators in a role Nook keeps, in file order, once per name key and role. A creator the file names more than
/// once is given the name and sort name of their first appearance.
/// </param>
/// <param name="Series">The series the file names, with the book's place in it.</param>
/// <param name="Subjects">The file's subjects in file order, once per name key, as first written.</param>
/// <param name="ExternalIds">
/// The file's identifiers in the form Nook stores them, in file order: an ISBN the registry rejects is left out, and
/// an identifier the file repeats is listed once.
/// </param>
/// <param name="Isbns">The file's valid ISBNs, as 13 digits.</param>
public sealed record WorkDraft(
    string Title,
    string SortTitle,
    string? Description,
    string? Publisher,
    string? Language,
    DateOnly? PublicationDate,
    IReadOnlyList<WorkDraft.Contributor> Contributors,
    BookSeries? Series,
    IReadOnlyList<string> Subjects,
    IReadOnlyList<WorkDraft.ExternalId> ExternalIds,
    IReadOnlyList<string> Isbns)
{
    /// <summary>Someone who made the book, in a role Nook keeps.</summary>
    /// <param name="Name">The name as the file first gives it.</param>
    /// <param name="SortName">The sort name the file gives with that first appearance, or null when it gives none.</param>
    /// <param name="Role">The role on the Work.</param>
    public sealed record Contributor(string Name, string? SortName, ContributorRole Role);

    /// <summary>An identifier in the form Nook stores it.</summary>
    /// <param name="Scheme">The lowercased scheme.</param>
    /// <param name="Value">The value, normalised by its scheme's rule.</param>
    public sealed record ExternalId(string Scheme, string Value);

    /// <summary>The draft of the Work the file describes. The file name is the uploaded one, used only for a title.</summary>
    public static WorkDraft From(BookMetadata metadata, string fileName)
    {
        var title = metadata.Title ?? TitleFromFileName(fileName);
        var externalIds = ExternalIdsOf(metadata);
        return new WorkDraft(
            title,
            metadata.SortTitle ?? title,
            metadata.Description,
            metadata.Publisher,
            metadata.Language,
            metadata.PublicationDate,
            ContributorsOf(metadata),
            metadata.Series,
            metadata.Subjects.DistinctBy(NameKey.Of).ToList(),
            externalIds,
            externalIds.Where(e => e.Scheme == ExternalIdRegistry.Isbn).Select(e => e.Value).ToList());
    }

    private static List<Contributor> ContributorsOf(BookMetadata metadata)
    {
        var firstAppearances = new Dictionary<string, BookCreator>();
        var seen = new HashSet<(string Key, ContributorRole Role)>();
        var contributors = new List<Contributor>();
        foreach (var creator in metadata.Creators)
        {
            if (creator.Role is not { } role)
            {
                continue;
            }

            var key = NameKey.Of(creator.Name);
            var first = firstAppearances.TryAdd(key, creator) ? creator : firstAppearances[key];
            if (seen.Add((key, role)))
            {
                contributors.Add(new Contributor(first.Name, first.SortName, role));
            }
        }

        return contributors;
    }

    private static List<ExternalId> ExternalIdsOf(BookMetadata metadata)
    {
        var externalIds = new List<ExternalId>();
        foreach (var identifier in metadata.Identifiers)
        {
            if (ExternalIdRegistry.Normalise(identifier.Scheme, identifier.Value) is { } value)
            {
                var externalId = new ExternalId(identifier.Scheme, value);
                if (!externalIds.Contains(externalId))
                {
                    externalIds.Add(externalId);
                }
            }
        }

        return externalIds;
    }

    /// <summary>
    /// The title of a book whose file gives none: the uploaded name without any folder or extension, or
    /// <c>Untitled</c> when nothing is left. The name comes from the client, so a folder in it, written with either
    /// kind of slash, is dropped whatever the server's own separator.
    /// </summary>
    private static string TitleFromFileName(string fileName)
    {
        var name = fileName[(fileName.LastIndexOfAny(['/', '\\']) + 1)..];
        var title = Path.GetFileNameWithoutExtension(name).Trim();
        return title.Length == 0 ? Filing.Untitled : title;
    }
}
