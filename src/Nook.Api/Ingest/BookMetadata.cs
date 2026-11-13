using Nook.Api.Data;

namespace Nook.Api.Ingest;

/// <summary>What a book file says about itself. Text values are trimmed, and a blank value counts as missing.</summary>
/// <param name="Title">The book's title, or null when the file gives none.</param>
/// <param name="SortTitle">The title the file gives for sorting, such as "Left Hand of Darkness, The", or null when it gives none.</param>
/// <param name="Creators">The people who made the book, in the order the file lists them, whatever their role.</param>
/// <param name="Subjects">The book's subjects, in the order the file lists them.</param>
/// <param name="Description">The file's first description.</param>
/// <param name="Publisher">The file's first publisher.</param>
/// <param name="Language">The file's first language.</param>
/// <param name="PublicationDate">The publication date, only when the file gives a complete date.</param>
/// <param name="Series">The series the book belongs to, if the file names one.</param>
/// <param name="Identifiers">The identifiers the file gives a scheme for, in the order it lists them.</param>
public sealed record BookMetadata(
    string? Title,
    string? SortTitle,
    IReadOnlyList<BookCreator> Creators,
    IReadOnlyList<string> Subjects,
    string? Description,
    string? Publisher,
    string? Language,
    DateOnly? PublicationDate,
    BookSeries? Series,
    IReadOnlyList<BookIdentifier> Identifiers);

/// <summary>Someone a book file names as one of its makers.</summary>
/// <param name="Name">The name people see.</param>
/// <param name="Role">The role the file gives, or null when it is one Nook does not keep. A creator with no role is an author.</param>
/// <param name="SortName">The name the file gives for sorting, such as "Le Guin, Ursula K.", or null when it gives none.</param>
public sealed record BookCreator(string Name, ContributorRole? Role, string? SortName);

/// <summary>A series a book file names.</summary>
/// <param name="Name">The series' name.</param>
/// <param name="Position">The book's place in the series, or null when the file gives none or gives one that is not a number.</param>
public sealed record BookSeries(string Name, decimal? Position);

/// <summary>An identifier a book file gives, such as an ISBN.</summary>
/// <param name="Scheme">The kind of identifier, in any case; the record keeps it lowercased.</param>
/// <param name="Value">The identifier as the file gives it, without a prefix naming its scheme.</param>
public sealed record BookIdentifier(string Scheme, string Value)
{
    public string Scheme { get; } = Scheme.ToLowerInvariant();
}
