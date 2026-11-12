using System.ComponentModel.DataAnnotations;

namespace Nook.Api.Features.Uploads;

/// <summary>What an upload may be, bound from <c>Nook:Uploads</c>.</summary>
public sealed class UploadOptions
{
    public const string Section = "Nook:Uploads";

    /// <summary>The largest file an upload may carry, in bytes. The default is 200 MB.</summary>
    [Range(1, long.MaxValue)]
    public long MaxBytes { get; set; } = 200 * 1024 * 1024;
}
