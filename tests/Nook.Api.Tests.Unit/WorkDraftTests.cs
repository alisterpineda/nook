using Nook.Api.Data;
using Nook.Api.Ingest;

namespace Nook.Api.Tests.Unit;

public class WorkDraftTests
{
    [Theory]
    [InlineData("mystery-book.epub", "mystery-book")]
    [InlineData(@"C:\books\mystery-book.epub", "mystery-book")]
    [InlineData("books/mystery-book.epub", "mystery-book")]
    [InlineData(".epub", "Untitled")]
    public void Title_falls_back_to_the_file_name_without_folder_or_extension(string fileName, string expected)
    {
        var draft = WorkDraft.From(Metadata(title: null), fileName);

        Assert.Equal(expected, draft.Title);
    }

    [Fact]
    public void Sort_title_falls_back_to_the_title()
    {
        var draft = WorkDraft.From(Metadata(title: "The Dispossessed", sortTitle: null), "book.epub");

        Assert.Equal("The Dispossessed", draft.SortTitle);
    }

    [Fact]
    public void Creator_named_twice_in_one_role_is_one_contributor()
    {
        var metadata = Metadata(creators:
        [
            new BookCreator("Ursula K. Le Guin", ContributorRole.Author, "Le Guin, Ursula K."),
            new BookCreator(" ursula  k. le guin", ContributorRole.Author, "Guin, Le"),
        ]);

        var draft = WorkDraft.From(metadata, "book.epub");

        var contributor = Assert.Single(draft.Contributors);
        Assert.Equal(
            new WorkDraft.Contributor("Ursula K. Le Guin", "Le Guin, Ursula K.", ContributorRole.Author), contributor);
    }

    [Fact]
    public void Creator_with_two_roles_is_listed_once_for_each()
    {
        var metadata = Metadata(creators:
        [
            new BookCreator("Ann Leckie", ContributorRole.Author, null),
            new BookCreator("Ann Leckie", ContributorRole.Translator, null),
            new BookCreator("Ann Leckie", ContributorRole.Author, null),
        ]);

        var draft = WorkDraft.From(metadata, "book.epub");

        Assert.Equal(
            [ContributorRole.Author, ContributorRole.Translator],
            draft.Contributors.Select(c => c.Role));
        Assert.All(draft.Contributors, c => Assert.Equal("Ann Leckie", c.Name));
    }

    [Fact]
    public void Creator_without_a_kept_role_is_left_out()
    {
        var metadata = Metadata(creators:
        [
            new BookCreator("Illustrator", null, null),
            new BookCreator("Author", ContributorRole.Author, null),
        ]);

        var draft = WorkDraft.From(metadata, "book.epub");

        var contributor = Assert.Single(draft.Contributors);
        Assert.Equal("Author", contributor.Name);
    }

    [Fact]
    public void Subject_repeated_in_other_case_or_spacing_is_listed_once()
    {
        var draft = WorkDraft.From(
            Metadata(subjects: ["Science Fiction", "science  fiction ", "Fantasy"]), "book.epub");

        Assert.Equal(["Science Fiction", "Fantasy"], draft.Subjects);
    }

    [Fact]
    public void Invalid_isbn_and_repeated_identifiers_are_left_out()
    {
        var metadata = Metadata(identifiers:
        [
            new BookIdentifier("isbn", "978-0-441-47812-5"),
            new BookIdentifier("isbn", "9780441478126"),
            new BookIdentifier("isbn", "0441478123"),
            new BookIdentifier("goodreads", "12345"),
            new BookIdentifier("goodreads", "12345"),
        ]);

        var draft = WorkDraft.From(metadata, "book.epub");

        Assert.Equal(
            [new WorkDraft.ExternalId("isbn", "9780441478125"), new WorkDraft.ExternalId("goodreads", "12345")],
            draft.ExternalIds);
        Assert.Equal(["9780441478125"], draft.Isbns);
    }

    private static BookMetadata Metadata(
        string? title = "Title",
        string? sortTitle = null,
        IReadOnlyList<BookCreator>? creators = null,
        IReadOnlyList<string>? subjects = null,
        IReadOnlyList<BookIdentifier>? identifiers = null) =>
        new(title, sortTitle, creators ?? [], subjects ?? [], null, null, null, null, null, identifiers ?? []);
}
