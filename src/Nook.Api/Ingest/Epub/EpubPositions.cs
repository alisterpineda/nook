namespace Nook.Api.Ingest.Epub;

/// <summary>
/// A book's positions list as Readium computes it for a reflowable EPUB: each reading-order file is split into one
/// position per 1024 bytes of its compressed size, and at least one.
/// </summary>
/// <param name="Total">How many positions the book has.</param>
/// <param name="Positions">Every position, in reading order.</param>
public sealed record EpubPositions(int Total, IReadOnlyList<EpubPosition> Positions)
{
    private const int PageLength = 1024;

    /// <summary>The positions of the publication's reading order.</summary>
    public static EpubPositions For(EpubPublication publication)
    {
        var counts = publication.ReadingOrder
            .Select(resource => (Resource: resource, Count: CountFor(resource.CompressedLength)))
            .ToList();
        var total = counts.Sum(entry => entry.Count);

        var positions = new List<EpubPosition>(total);
        foreach (var (resource, count) in counts)
        {
            for (var index = 0; index < count; index++)
            {
                var position = positions.Count + 1;
                positions.Add(new EpubPosition(
                    resource.Href,
                    resource.MediaType,
                    position,
                    (double)index / count,
                    (double)(position - 1) / total));
            }
        }

        return new EpubPositions(total, positions);
    }

    /// <summary><c>max(ceil(compressedLength / 1024), 1)</c>, in whole numbers.</summary>
    private static int CountFor(long compressedLength) =>
        (int)Math.Max((compressedLength + PageLength - 1) / PageLength, 1);
}

/// <summary>A place in the book, shaped as a Readium Locator.</summary>
/// <param name="Href">The reading-order file's href, exactly as the publication gives it.</param>
/// <param name="MediaType">The file's media type.</param>
/// <param name="Position">The position's number, counting from 1 across the book.</param>
/// <param name="Progression">How far into its file the position starts: its index there over the file's count.</param>
/// <param name="TotalProgression">How far into the book the position starts: the positions before it over the total.</param>
public sealed record EpubPosition(
    string Href, string MediaType, int Position, double Progression, double TotalProgression);
