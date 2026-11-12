namespace Nook.Api.Ingest;

/// <summary>A file that is a book format Nook knows, but that Nook cannot read: it is DRM-encrypted or broken.</summary>
public sealed class UnreadableBookException(string message, Exception? innerException = null)
    : Exception(message, innerException);
