namespace Nook.Api.Data;

/// <summary>A label on works, shared across libraries. A work can carry many tags.</summary>
public class Tag : Entity
{
    /// <summary>The name people see. Not unique: matching tags belongs to ingest.</summary>
    public required string Name { get; set; }
}
