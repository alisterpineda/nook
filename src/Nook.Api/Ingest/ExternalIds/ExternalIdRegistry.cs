namespace Nook.Api.Ingest.ExternalIds;

/// <summary>
/// The schemes of external id that Nook knows the shape of, each with a rule that puts a value in the one form Nook
/// stores it in. A value of any other scheme is stored as given.
/// </summary>
public static class ExternalIdRegistry
{
    public const string Isbn = "isbn";

    private static readonly Dictionary<string, Func<string, string?>> Rules = new(StringComparer.OrdinalIgnoreCase)
    {
        [Isbn] = NormaliseIsbn,
    };

    /// <summary>
    /// The value in the form Nook stores for its scheme, or null when the scheme's rule rejects it. The scheme is
    /// matched whatever its case.
    /// </summary>
    public static string? Normalise(string scheme, string value) =>
        Rules.TryGetValue(scheme, out var rule) ? rule(value) : value;

    /// <summary>
    /// An ISBN-10 or ISBN-13 as 13 digits, without hyphens or spaces, or null when it is neither or its check digit
    /// is wrong. An ISBN-10 takes the <c>978</c> prefix.
    /// </summary>
    private static string? NormaliseIsbn(string value)
    {
        var isbn = value.Replace("-", "").Replace(" ", "");
        return isbn.Length switch
        {
            10 when IsValidIsbn10(isbn) => Isbn13From("978" + isbn[..9]),
            13 when isbn.All(char.IsAsciiDigit) && Isbn13CheckDigit(isbn[..12]) == isbn[12] => isbn,
            _ => null,
        };
    }

    /// <summary>Nine digits and a check digit, 0 to 9 or X, where the digits weighted 10 down to 1 sum to a multiple of 11.</summary>
    private static bool IsValidIsbn10(string isbn)
    {
        var sum = 0;
        for (var i = 0; i < 10; i++)
        {
            int digit;
            if (char.IsAsciiDigit(isbn[i]))
            {
                digit = isbn[i] - '0';
            }
            else if (i == 9 && isbn[i] is 'X' or 'x')
            {
                digit = 10;
            }
            else
            {
                return false;
            }

            sum += (10 - i) * digit;
        }

        return sum % 11 == 0;
    }

    private static string Isbn13From(string twelveDigits) => twelveDigits + Isbn13CheckDigit(twelveDigits);

    /// <summary>The ISBN-13 check digit of twelve digits weighted 1 and 3 in turn.</summary>
    private static char Isbn13CheckDigit(string twelveDigits)
    {
        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            sum += (twelveDigits[i] - '0') * (i % 2 == 0 ? 1 : 3);
        }

        return (char)('0' + (10 - sum % 10) % 10);
    }
}
