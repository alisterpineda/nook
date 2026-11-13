namespace Nook.Api.Ingest;

/// <summary>
/// How ingest tells whether two names of a person, series or tag are the same: they are when they differ only in
/// case or in spacing.
/// </summary>
public static class NameKey
{
    /// <summary>The name trimmed, with each run of whitespace made one space, and lowercased invariantly.</summary>
    public static string Of(string name) => Tidy(name).ToLowerInvariant();

    /// <summary>The name as ingest stores it: trimmed, with each run of whitespace made one space.</summary>
    public static string Tidy(string name) =>
        string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
