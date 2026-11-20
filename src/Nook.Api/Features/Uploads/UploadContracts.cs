using System.ComponentModel.DataAnnotations;

namespace Nook.Api.Features.Uploads;

/// <summary>A file to ingest into a section or the Default area, sent as a multipart form.</summary>
public class UploadRequest
{
    /// <summary>The section the file goes into, or none for the Default area.</summary>
    public Guid? SectionId { get; set; }

    /// <summary>The file. Any file is accepted here; its job decides whether Nook can read it.</summary>
    [Required]
    public IFormFile? File { get; set; }
}

/// <summary>The job that ingests the uploaded file, to watch through the jobs endpoints.</summary>
public record UploadResponse(Guid JobId);

/// <summary>What an upload may be, so a client can refuse a file before sending it.</summary>
/// <param name="MaxBytes">The size in bytes of the largest file an upload may carry.</param>
public record UploadLimitsResponse(long MaxBytes);
