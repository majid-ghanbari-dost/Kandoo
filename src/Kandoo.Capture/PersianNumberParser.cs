using System.Globalization;
using System.Text;

namespace Kandoo.Capture;

/// <summary>
/// Parses Persian, Arabic-Indic, and ASCII numeric strings from E-30 extraction candidates
/// into strict base-10 integer values (rials and quantity millis). Fails closed on ambiguous input.
/// </summary>
public static class PersianNumberParser
{
    public static string NormalizeDigits(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (ch is >= '\u06F0' and <= '\u06F9')
            {
                sb.Append((char)('0' + (ch - '\u06F0')));
            }
            else if (ch is >= '\u0660' and <= '\u0669')
            {
                sb.Append((char)('0' + (ch - '\u0660')));
            }
            else if (ch is ',' or '\u066C' or ' ' or '\t' or '\u00A0' or '\u200C' or '\u200F' or '\u200E')
            {
                // Strip grouping/direction whitespace markers cleanly
            }
            else if (ch == '\u066B')
            {
                sb.Append('.');
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    public static bool TryParseNonNegativeInt64(string? raw, out long value)
    {
        value = 0;
        var normalized = NormalizeDigits(raw);
        if (normalized.Length == 0)
            return false;

        foreach (var ch in normalized)
        {
            if (ch is < '0' or > '9')
                return false;
        }

        return long.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryParsePositiveQuantityMillis(string? raw, out long quantityMillis)
    {
        quantityMillis = 0;
        var normalized = NormalizeDigits(raw);
        if (normalized.Length == 0)
            return false;

        var dotIndex = normalized.IndexOf('.', StringComparison.Ordinal);
        if (dotIndex < 0)
        {
            if (!TryParseNonNegativeInt64(normalized, out var whole) || whole <= 0)
                return false;

            try
            {
                quantityMillis = checked(whole * 1000L);
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        if (normalized.IndexOf('.', dotIndex + 1) >= 0)
            return false;

        var intPartStr = normalized[..dotIndex];
        var fracPartStr = normalized[(dotIndex + 1)..];
        if (fracPartStr.Length == 0 || fracPartStr.Length > 3)
            return false;

        long intPart = 0;
        if (intPartStr.Length > 0 && !TryParseNonNegativeInt64(intPartStr, out intPart))
            return false;

        if (!TryParseNonNegativeInt64(fracPartStr.PadRight(3, '0'), out var fracMillis))
            return false;

        try
        {
            quantityMillis = checked(intPart * 1000L + fracMillis);
            return quantityMillis > 0;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
