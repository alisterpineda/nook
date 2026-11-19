using Nook.Api.Features.Works;

namespace Nook.Api.Tests.Unit;

public class SearchWordsTests
{
    [Fact]
    public void Punctuation_separates_words_and_case_is_dropped()
    {
        Assert.Equal(["flann", "o", "brien", "life", "changing"], SearchWords.Of("Flann O'Brien: Life-Changing!"));
    }

    [Fact]
    public void Text_with_no_unicode_form_still_gives_its_words()
    {
        // Half of a surrogate pair on its own has no normal form.
        Assert.Equal(["a", "b"], SearchWords.Of("a\uD800b"));
    }
}
