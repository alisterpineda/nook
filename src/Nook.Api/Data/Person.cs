namespace Nook.Api.Data;

/// <summary>Someone who contributed to works, shared across libraries.</summary>
public class Person : Entity
{
    /// <summary>The name people see. Not unique: matching people belongs to ingest.</summary>
    public required string Name { get; set; }

    /// <summary>The name used for sorting, such as "Le Guin, Ursula K.".</summary>
    public required string SortName { get; set; }
}
