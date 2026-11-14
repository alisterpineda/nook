namespace Nook.Api.Features.Works;

/// <summary>A size a cover can be asked for other than its original.</summary>
public enum CoverSize
{
    Small,
    Large,
}

/// <summary>What is known of each cover size: the name a request gives it and the box a cover at it fits inside.</summary>
public static class CoverSizes
{
    private static readonly (CoverSize Size, string Name, int Width, int Height)[] All =
    [
        (CoverSize.Small, "small", 400, 600),
        (CoverSize.Large, "large", 1000, 1500),
    ];

    /// <summary>The names a request can give a size, as a sentence lists them: "small or large".</summary>
    public static string Names { get; } = string.Join(" or ", All.Select(entry => entry.Name));

    /// <summary>The name a request gives the size, such as <c>small</c>.</summary>
    public static string NameOf(CoverSize size) => Entry(size).Name;

    /// <summary>The box a cover at the size fits inside, in pixels.</summary>
    public static (int Width, int Height) BoxOf(CoverSize size)
    {
        var entry = Entry(size);
        return (entry.Width, entry.Height);
    }

    /// <summary>
    /// The size the text names. Only the exact names are sizes: a name in other case or a number is not.
    /// </summary>
    public static bool TryParse(string value, out CoverSize size)
    {
        foreach (var entry in All)
        {
            if (entry.Name == value)
            {
                size = entry.Size;
                return true;
            }
        }

        size = default;
        return false;
    }

    private static (CoverSize Size, string Name, int Width, int Height) Entry(CoverSize size)
    {
        foreach (var entry in All)
        {
            if (entry.Size == size)
            {
                return entry;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(size), size, null);
    }
}
