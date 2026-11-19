using System.Text;
using Nook.Api.Ingest;

namespace Nook.Api.Features.Works;

/// <summary>Splits text into the words search matches on, the same way for stored fields and for a typed term.</summary>
public static class SearchWords
{
    /// <summary>
    /// The text's words, lowercased. A word is a run of letters, combining marks and digits, so a word in a script
    /// whose letters carry marks stays whole. Every other character, an apostrophe or a hyphen included, separates words.
    /// </summary>
    public static IReadOnlyList<string> Of(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var words = new List<string>();
        var word = new StringBuilder();
        foreach (var rune in Filing.InForm(text, NormalizationForm.FormC).ToLowerInvariant().EnumerateRunes())
        {
            if (Rune.IsLetter(rune) || Rune.GetUnicodeCategory(rune) is
                    System.Globalization.UnicodeCategory.NonSpacingMark
                    or System.Globalization.UnicodeCategory.SpacingCombiningMark
                    or System.Globalization.UnicodeCategory.EnclosingMark
                || Rune.IsNumber(rune))
            {
                word.Append(rune.ToString());
            }
            else if (word.Length > 0)
            {
                words.Add(word.ToString());
                word.Clear();
            }
        }

        if (word.Length > 0)
        {
            words.Add(word.ToString());
        }

        return words;
    }
}
