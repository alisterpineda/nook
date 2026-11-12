namespace Nook.Api.Ingest;

/// <summary>What a book file says about itself. Text values are trimmed, and a blank value counts as missing.</summary>
/// <param name="Title">The book's title, or null when the file gives none.</param>
/// <param name="Creators">The names of the people who made the book, in the order the file lists them.</param>
/// <param name="Subjects">The book's subjects, in the order the file lists them.</param>
/// <param name="Description">The file's first description.</param>
/// <param name="Publisher">The file's first publisher.</param>
/// <param name="Language">The file's first language.</param>
/// <param name="PublicationDate">The publication date, only when the file gives a complete date.</param>
/// <param name="Series">The series the book belongs to, if the file names one.</param>
public sealed record BookMetadata(
    string? Title,
    IReadOnlyList<string> Creators,
    IReadOnlyList<string> Subjects,
    string? Description,
    string? Publisher,
    string? Language,
    DateOnly? PublicationDate,
    BookSeries? Series);

/// <summary>A series a book file names.</summary>
/// <param name="Name">The series' name.</param>
/// <param name="Position">The book's place in the series, or null when the file gives none or gives one that is not a number.</param>
public sealed record BookSeries(string Name, decimal? Position);
