namespace Nook.Api.Data;

/// <summary>A collection of works kept in one folder on disk.</summary>
public class Library : Entity
{
    /// <summary>The id of the single library every install starts with, created by the first migration.</summary>
    public static readonly Guid DefaultId = new("01a1c7e7-8000-7000-8000-000000000001");

    /// <summary>The name people see.</summary>
    public required string Name { get; set; }

    /// <summary>The library's folder, relative to the root where Nook keeps its files.</summary>
    public required string FolderName { get; set; }
}
