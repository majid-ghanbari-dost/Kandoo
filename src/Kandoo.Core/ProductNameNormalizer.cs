using System.Globalization;
using System.Text;

namespace Kandoo.Core;

public static class ProductNameNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A product name is required.", nameof(value));
        }

        var input = value.Normalize(NormalizationForm.FormKC);
        var result = new StringBuilder(input.Length);
        var pendingSpace = false;

        foreach (var character in input)
        {
            var mapped = character switch
            {
                '\u064A' => '\u06CC', // Arabic yeh to Persian yeh.
                '\u0643' => '\u06A9', // Arabic kaf to Persian kaf.
                _ => character
            };

            if (mapped == '\u200C' || char.IsWhiteSpace(mapped))
            {
                pendingSpace = result.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                result.Append(' ');
                pendingSpace = false;
            }

            result.Append(char.ToLowerInvariant(mapped));
        }

        var normalized = result.ToString().Normalize(NormalizationForm.FormKC);
        if (normalized.Length == 0)
        {
            throw new ArgumentException("A product name is required.", nameof(value));
        }

        return normalized;
    }

    public static string SimilarityKey(string? value)
    {
        var normalized = Normalize(value);
        var result = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (!char.IsWhiteSpace(character) && char.GetUnicodeCategory(character) != UnicodeCategory.DashPunctuation)
            {
                result.Append(character);
            }
        }

        return result.ToString();
    }
}
