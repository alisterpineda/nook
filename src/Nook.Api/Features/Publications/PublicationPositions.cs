using Nook.Api.Ingest.Epub;

namespace Nook.Api.Features.Publications;

/// <summary>A publication's positions list, in the shape Readium reads.</summary>
/// <param name="Total">How many positions the book has.</param>
/// <param name="Positions">Every position, in reading order.</param>
public sealed record PublicationPositions(int Total, IReadOnlyList<PublicationPosition> Positions)
{
    /// <summary>The positions list of the publication.</summary>
    public static PublicationPositions For(EpubPublication publication)
    {
        var positions = EpubPositions.For(publication);
        return new PublicationPositions(
            positions.Total,
            positions.Positions
                .Select(position => new PublicationPosition(
                    position.Href,
                    position.MediaType,
                    new PublicationLocations(position.Position, position.Progression, position.TotalProgression)))
                .ToList());
    }
}

/// <summary>A position, as a Readium Locator.</summary>
/// <param name="Href">The reading-order file's href, exactly as the manifest gives it.</param>
/// <param name="Type">The file's media type.</param>
/// <param name="Locations">Where in the file and the book the position is.</param>
public sealed record PublicationPosition(string Href, string Type, PublicationLocations Locations);

/// <summary>Where a position is.</summary>
/// <param name="Position">The position's number, counting from 1 across the book.</param>
/// <param name="Progression">How far into its file the position starts, from 0 to 1.</param>
/// <param name="TotalProgression">How far into the book the position starts, from 0 to 1.</param>
public sealed record PublicationLocations(int Position, double Progression, double TotalProgression);
