namespace Nook.Api.Data;

/// <summary>An identifier a work is known by outside Nook, such as an ISBN. A work can hold several per scheme.</summary>
public class WorkExternalId : Entity
{
    public Guid WorkId { get; set; }

    /// <summary>The kind of identifier, such as "isbn", as plain text.</summary>
    public required string Scheme { get; set; }

    /// <summary>The identifier itself, stored as given.</summary>
    public required string Value { get; set; }
}
