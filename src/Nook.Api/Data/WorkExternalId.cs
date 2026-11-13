namespace Nook.Api.Data;

/// <summary>An identifier a work is known by outside Nook, such as an ISBN. A work can hold several per scheme.</summary>
public class WorkExternalId : Entity
{
    public Guid WorkId { get; set; }

    /// <summary>The kind of identifier, such as "isbn", as plain text.</summary>
    public required string Scheme { get; set; }

    /// <summary>The identifier itself, in the form the scheme's rule gives it, or as given for a scheme with no rule.</summary>
    public required string Value { get; set; }
}
