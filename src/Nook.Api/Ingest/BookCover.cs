namespace Nook.Api.Ingest;

/// <summary>A book file's cover image.</summary>
/// <param name="Bytes">The image exactly as the file holds it.</param>
/// <param name="Extension">The extension the image is saved under, lowercased and with its leading dot, such as <c>.png</c>.</param>
public sealed record BookCover(byte[] Bytes, string Extension);
