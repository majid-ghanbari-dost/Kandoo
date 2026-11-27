using System.Text;

namespace Kandoo.App;

/// <summary>
/// Mobile-number normalization for the customer identity (C-92: the mobile number
/// is the identity; roles live under it). The P-31 contract requires E.164 syntax
/// (/^\+[1-9][0-9]{7,14}$/ on the PHP side); the UI accepts Iranian local forms
/// and Persian/Arabic digits and normalizes to +98….
/// </summary>
public static class CustomerPhone
{
    /// <summary>
    /// Normalize user input to E.164. Accepts: ۰۹۱۲…, 0912…, 912…, +98912…, 0098912….
    /// </summary>
    public static bool TryNormalize(string? input, out string e164)
    {
        e164 = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var builder = new StringBuilder(input.Length);
        foreach (var ch in input.Trim())
        {
            if (ch == '+')
            {
                builder.Append('+');
                continue;
            }

            var digit = AsciiDigit(ch);
            if (digit is not null)
            {
                builder.Append(digit);
            }
        }

        var value = builder.ToString();
        if (value.StartsWith('+'))
        {
            // Already international — digits only after the plus.
            var tail = value[1..];
            if (tail.Length is < 8 or > 15 || tail.Any(c => c is < '0' or > '9'))
            {
                return false;
            }

            e164 = "+" + tail;
            return true;
        }

        if (value.StartsWith("00"))
        {
            value = value[2..];
        }

        if (value.Length == 11 && value.StartsWith("09"))
        {
            value = "98" + value[1..];
        }
        else if (value.Length == 10 && value.StartsWith('9'))
        {
            value = "98" + value;
        }
        else if (value.Length == 12 && value.StartsWith("989"))
        {
            // already 989… without plus
        }
        else
        {
            return false;
        }

        if (!value.StartsWith("989"))
        {
            return false;
        }

        e164 = "+" + value;
        return true;
    }

    private static char? AsciiDigit(char ch) => ch switch
    {
        >= '0' and <= '9' => ch,
        // Persian digits
        >= '\u06F0' and <= '\u06F9' => (char)('0' + (ch - '\u06F0')),
        // Arabic-Indic digits
        >= '\u0660' and <= '\u0669' => (char)('0' + (ch - '\u0660')),
        _ => null,
    };
}
