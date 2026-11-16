using System.Net;
using System.Text.RegularExpressions;

namespace Nook.Api.Ingest;

/// <summary>
/// Turns a book file's description into the plain text Nook stores. A description with at least one HTML tag or
/// comment is read as HTML: its tags go, with line breaks where they broke the text, and its character references are
/// decoded. Any other description only has its character references decoded, so its line breaks stay as written.
/// </summary>
public static partial class DescriptionText
{
    /// <summary>The description as plain text, or null when no text is left.</summary>
    public static string? ToPlainText(string? description)
    {
        if (description is null)
        {
            return null;
        }

        var text = Comment().IsMatch(description) || Tag().IsMatch(description)
            ? HtmlToText(description)
            : WebUtility.HtmlDecode(description).Trim();
        return text.Length == 0 ? null : text;
    }

    private static string HtmlToText(string html)
    {
        var text = Comment().Replace(html, "");
        text = MarkupWhitespace().Replace(text, " ");
        text = LineBreakTag().Replace(text, "\n");
        text = BlockTag().Replace(text, "\n\n");
        text = LineTag().Replace(text, "\n");
        text = Tag().Replace(text, "");

        // Decoded only now, so a reference that spells a tag stays as visible text.
        text = WebUtility.HtmlDecode(text);
        text = SpaceAroundLineBreak().Replace(text, "\n");
        text = BlankLines().Replace(text, "\n\n");
        return text.Trim();
    }

    // The whitespace of HTML markup. Not \s, which would also take a non-breaking space.
    private const string MarkupSpace = "[ \t\r\n\f]";

    // The rest of a tag after its name. A name is matched whole: it must end at whitespace, a slash or the tag's
    // end, so <pre> is not a p tag.
    private const string TagNameEnd = "(?=[ \t\r\n\f/>])[^>]*>";

    [GeneratedRegex("<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex("</?[A-Za-z][^>]*>")]
    private static partial Regex Tag();

    [GeneratedRegex(MarkupSpace + "+")]
    private static partial Regex MarkupWhitespace();

    [GeneratedRegex("<br" + TagNameEnd, RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakTag();

    [GeneratedRegex("</?(p|div|blockquote|ul|ol|h[1-6])" + TagNameEnd, RegexOptions.IgnoreCase)]
    private static partial Regex BlockTag();

    [GeneratedRegex("<(li|tr)" + TagNameEnd, RegexOptions.IgnoreCase)]
    private static partial Regex LineTag();

    [GeneratedRegex("[ \t]*\n[ \t]*")]
    private static partial Regex SpaceAroundLineBreak();

    [GeneratedRegex("\n{3,}")]
    private static partial Regex BlankLines();
}
