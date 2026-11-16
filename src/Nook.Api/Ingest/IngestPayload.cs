namespace Nook.Api.Ingest;

/// <summary>What an ingest job works on.</summary>
/// <param name="SectionId">The section the file goes into, or null for none.</param>
/// <param name="StagedName">The staged file's name inside the staging folder. It has no extension.</param>
/// <param name="FileName">The name the file was uploaded under.</param>
public record IngestPayload(Guid? SectionId, string StagedName, string FileName);
