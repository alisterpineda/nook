using System.Text;
using Nook.Api.Ingest;

namespace Nook.Api.Tests.Unit;

public class FilingTests
{
    [Theory]
    [InlineData('<')]
    [InlineData('>')]
    [InlineData(':')]
    [InlineData('"')]
    [InlineData('/')]
    [InlineData('\\')]
    [InlineData('|')]
    [InlineData('?')]
    [InlineData('*')]
    [InlineData('\0')]
    [InlineData('\t')]
    [InlineData('\u001f')]
    [InlineData('\u007f')]
    public void Illegal_characters_are_replaced(char illegal)
    {
        Assert.Equal("Before_After", Filing.Segment($"Before{illegal}After"));
    }

    [Theory]
    [InlineData("Title.", "Title")]
    [InlineData("Title...", "Title")]
    [InlineData("Title . . ", "Title")]
    [InlineData("  Title  ", "Title")]
    [InlineData("What? A \"Test\": Part 1/2...", "What_ A _Test__ Part 1_2")]
    [InlineData("J. R. R.", "J. R. R")]
    public void Trailing_dots_and_spaces_are_trimmed(string name, string expected)
    {
        Assert.Equal(expected, Filing.Segment(name));
    }

    [Fact]
    public void Long_name_is_cut_to_about_100_characters()
    {
        var segment = Filing.Segment(new string('a', 150));

        Assert.Equal(new string('a', 100), segment);
    }

    [Fact]
    public void Long_name_of_multibyte_characters_stays_within_200_bytes_and_ends_on_a_whole_character()
    {
        var name = string.Concat(Enumerable.Repeat("€", 150));

        var segment = Filing.Segment(name);

        Assert.InRange(Encoding.UTF8.GetByteCount(segment), 1, 200);
        Assert.Equal(string.Concat(Enumerable.Repeat("€", segment.Length)), segment);
        Assert.Equal(66, segment.Length);
    }

    [Fact]
    public void Long_name_is_not_cut_inside_a_surrogate_pair()
    {
        var name = "a" + string.Concat(Enumerable.Repeat("\U0001F4DA", 60));

        var segment = Filing.Segment(name);

        Assert.True(segment.Length <= 100);
        Assert.True(Encoding.UTF8.GetByteCount(segment) <= 200);
        Assert.False(char.IsHighSurrogate(segment[^1]), "The segment ends with half a character.");
        Assert.Equal(segment, Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(segment)));
    }

    [Fact]
    public void Name_cut_before_spaces_or_dots_is_trimmed_again()
    {
        var segment = Filing.Segment(new string('a', 98) + " ." + new string('b', 20));

        Assert.Equal(new string('a', 98), segment);
    }

    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("nul", "_nul")]
    [InlineData("Aux", "_Aux")]
    [InlineData("PRN", "_PRN")]
    [InlineData("COM1", "_COM1")]
    [InlineData("lpt9", "_lpt9")]
    [InlineData("CON.tar", "_CON.tar")]
    [InlineData("CON.", "_CON")]
    [InlineData("CONSOLE", "CONSOLE")]
    [InlineData("COM10", "COM10")]
    public void Reserved_windows_name_is_changed(string name, string expected)
    {
        Assert.Equal(expected, Filing.Segment(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData(" . . ")]
    public void Empty_name_becomes_untitled(string name)
    {
        Assert.Equal("Untitled", Filing.Segment(name));
    }

    [Fact]
    public void Work_folder_makes_each_segment_safe()
    {
        Assert.Equal("Books/A_B/What_ A _Test__ Part 1_2", Filing.WorkFolder("A/B", "What? A \"Test\": Part 1/2..."));
    }

    [Fact]
    public void Work_folder_without_an_author_is_under_unknown_author()
    {
        Assert.Equal("Books/Unknown Author/mystery-book", Filing.WorkFolder(null, "mystery-book"));
    }
}
