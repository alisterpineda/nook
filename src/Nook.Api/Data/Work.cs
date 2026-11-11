using NpgsqlTypes;

namespace Nook.Api.Data;

/// <summary>A book as people think of it, in one library, whatever files it comes in.</summary>
public class Work : Entity
{
    /// <summary>The library the work belongs to. Every work has one.</summary>
    public Guid LibraryId { get; set; }

    /// <summary>The title people see.</summary>
    public required string Title { get; set; }

    /// <summary>The title used for sorting, such as "Hobbit, The".</summary>
    public required string SortTitle { get; set; }

    public string? Description { get; set; }

    /// <summary>The language the work is written in.</summary>
    public string? Language { get; set; }

    public DateOnly? PublicationDate { get; set; }

    /// <summary>The publisher's name as plain text.</summary>
    public string? Publisher { get; set; }

    /// <summary>The series the work belongs to, if any.</summary>
    public Guid? SeriesId { get; set; }

    /// <summary>The work's place in its series, which can fall between whole numbers, such as 1.5.</summary>
    public decimal? SeriesPosition { get; set; }

    /// <summary>The content hash of the cover image.</summary>
    public string? CoverHash { get; set; }

    /// <summary>The full-text search document. Empty until search is built.</summary>
    public NpgsqlTsVector? SearchVector { get; set; }

    /// <summary>The people who made the work, each with a role and display order.</summary>
    public ICollection<WorkContributor> Contributors { get; } = [];

    public ICollection<Tag> Tags { get; } = [];

    /// <summary>The identifiers the work is known by outside Nook, such as ISBNs.</summary>
    public ICollection<WorkExternalId> ExternalIds { get; } = [];

    /// <summary>The copies of the work Nook holds, each in one format.</summary>
    public ICollection<Item> Items { get; } = [];
}
