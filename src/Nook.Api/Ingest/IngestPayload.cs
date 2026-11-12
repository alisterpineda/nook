namespace Nook.Api.Ingest;

/// <summary>What an ingest job works on.</summary>
/// <param name="LibraryId">The library the file goes into.</param>
/// <param name="StagedName">The staged file's name inside the staging folder. It has no extension.</param>
/// <param name="FileName">The name the file was uploaded under.</param>
public record IngestPayload(Guid LibraryId, string StagedName, string FileName);
