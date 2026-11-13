using Nook.Api.Ingest.ExternalIds;

namespace Nook.Api.Tests.Unit;

public class ExternalIdRegistryTests
{
    [Theory]
    [InlineData("978-0-441-47812-5", "9780441478125")]
    [InlineData("978 0 441 47812 5", "9780441478125")]
    [InlineData("0441478123", "9780441478125")]
    [InlineData("0-8044-2957-X", "9780804429573")]
    [InlineData("080442957x", "9780804429573")]
    public void Isbn_is_normalised_to_13_digits(string value, string expected)
    {
        Assert.Equal(expected, ExternalIdRegistry.Normalise("isbn", value));
    }

    [Theory]
    [InlineData("9780441478126")]
    [InlineData("0441478124")]
    [InlineData("97804414781")]
    [InlineData("97804414781255")]
    [InlineData("978044147812A")]
    [InlineData("ISBN 9780441478125")]
    [InlineData("")]
    public void Invalid_isbn_is_rejected(string value)
    {
        Assert.Null(ExternalIdRegistry.Normalise("isbn", value));
    }

    [Fact]
    public void Scheme_is_matched_whatever_its_case()
    {
        Assert.Equal("9780441478125", ExternalIdRegistry.Normalise("ISBN", "0441478123"));
        Assert.Null(ExternalIdRegistry.Normalise("Isbn", "9780441478126"));
    }

    [Theory]
    [InlineData("goodreads", "12345")]
    [InlineData("uuid", "0F3B6A6E-1C2D-4E5F-8A9B-0C1D2E3F4A5B")]
    [InlineData("asin", "B00 ABC-123")]
    public void Unknown_scheme_is_kept_as_given(string scheme, string value)
    {
        Assert.Equal(value, ExternalIdRegistry.Normalise(scheme, value));
    }
}
