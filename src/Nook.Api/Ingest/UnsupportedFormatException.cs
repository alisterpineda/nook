namespace Nook.Api.Ingest;

/// <summary>A file whose content is none of the book formats Nook knows.</summary>
public sealed class UnsupportedFormatException(string fileName)
    : InvalidOperationException($"The uploaded file '{fileName}' is in an unsupported format.");
