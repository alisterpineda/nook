using Nook.Api.Ingest.Epub;

namespace Nook.Api.Tests.Unit;

public class EpubPositionsTests
{
    [Fact]
    public void Each_entry_gets_one_position_per_1024_compressed_bytes_and_at_least_one()
    {
        var publication = Publication(
            Chapter("empty.xhtml", 0),
            Chapter("one.xhtml", 1),
            Chapter("exact.xhtml", 1024),
            Chapter("over.xhtml", 1025),
            Chapter("long.xhtml", 5000));

        var positions = EpubPositions.For(publication);

        Assert.Equal(
            [("empty.xhtml", 1), ("one.xhtml", 1), ("exact.xhtml", 1), ("over.xhtml", 2), ("long.xhtml", 5)],
            positions.Positions.CountBy(position => position.Href).Select(pair => (pair.Key, pair.Value)));
        Assert.Equal(10, positions.Total);
        Assert.Equal(positions.Total, positions.Positions.Count);
    }

    [Fact]
    public void Positions_count_from_one_across_the_book()
    {
        var publication = Publication(Chapter("a.xhtml", 2048), Chapter("b%20c.xhtml", 3000));

        var positions = EpubPositions.For(publication).Positions;

        Assert.Equal([1, 2, 3, 4, 5], positions.Select(position => position.Position));
        Assert.Equal(
            ["a.xhtml", "a.xhtml", "b%20c.xhtml", "b%20c.xhtml", "b%20c.xhtml"],
            positions.Select(position => position.Href));
        Assert.All(positions, position => Assert.Equal("application/xhtml+xml", position.MediaType));
    }

    [Fact]
    public void Progression_is_the_index_within_the_entry_over_its_count()
    {
        var publication = Publication(Chapter("a.xhtml", 2048), Chapter("b.xhtml", 3000));

        var positions = EpubPositions.For(publication).Positions;

        Assert.Equal([0, 0.5, 0, 1 / 3.0, 2 / 3.0], positions.Select(position => position.Progression));
    }

    [Fact]
    public void Total_progression_is_the_position_before_it_over_the_total()
    {
        var publication = Publication(Chapter("a.xhtml", 2048), Chapter("b.xhtml", 3000));

        var positions = EpubPositions.For(publication).Positions;

        Assert.Equal([0, 0.2, 0.4, 0.6, 0.8], positions.Select(position => position.TotalProgression));
    }

    [Fact]
    public void Resources_outside_the_reading_order_get_no_positions()
    {
        var publication = Publication(Chapter("a.xhtml", 10)) with
        {
            Resources = [new EpubResource("style.css", "style.css", "text/css", 4096)],
        };

        var positions = EpubPositions.For(publication);

        Assert.Equal(1, positions.Total);
        Assert.Equal("a.xhtml", Assert.Single(positions.Positions).Href);
    }

    private static EpubResource Chapter(string href, long compressedLength) =>
        new(Uri.UnescapeDataString(href), href, "application/xhtml+xml", compressedLength);

    private static EpubPublication Publication(params EpubResource[] readingOrder) =>
        new(Title: "Book", Identifier: null, Language: null, Authors: [], ReadingOrder: readingOrder, Resources: [], Toc: []);
}
