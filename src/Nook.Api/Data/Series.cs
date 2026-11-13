namespace Nook.Api.Data;

/// <summary>A named run of works, shared across libraries. A work belongs to at most one series.</summary>
public class Series : Entity, INamed
{
    /// <summary>The name people see. Not unique: matching series belongs to ingest.</summary>
    public required string Name { get; set; }
}
