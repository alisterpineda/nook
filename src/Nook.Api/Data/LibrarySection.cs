namespace Nook.Api.Data;

/// <summary>A grouping of works that exists only in the database. A work is in at most one section.</summary>
public class LibrarySection : Entity
{
    /// <summary>The name people see.</summary>
    public required string Name { get; set; }
}
