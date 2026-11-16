namespace Nook.Api.Data;

/// <summary>Someone who contributed to works, shared across sections.</summary>
public class Person : NamedEntity
{
    /// <summary>The name used for sorting, such as "Le Guin, Ursula K.".</summary>
    public required string SortName { get; set; }
}
