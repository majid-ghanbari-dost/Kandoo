using System.Globalization;
using System.Text;

namespace Kandoo.App;

/// <summary>
/// Persian digit and amount formatting for the customer boards
/// (e.g. «۱۲۰٬۰۰۰ تومان» — Customer-Invoices-v2). Digit mapping is explicit so
/// formatting is deterministic on every runtime/ICU configuration.
/// </summary>
public static class PersianDigits
{
    private const char PersianZero = '\u06F0'; // ۰
    private const char GroupSeparator = '\u066C'; // ٬

    /// <summary>Map ASCII digits to Persian digits; other characters pass through.</summary>
    public static string ToPersian(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            builder.Append(ch is >= '0' and <= '9'
                ? (char)(PersianZero + (ch - '0'))
                : ch);
        }

        return builder.ToString();
    }

    /// <summary>Map Persian/Arabic digits to ASCII; other characters pass through.</summary>
    public static string ToAsciiDigits(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            builder.Append(ch switch
            {
                >= '\u06F0' and <= '\u06F9' => (char)('0' + (ch - '\u06F0')),
                >= '\u0660' and <= '\u0669' => (char)('0' + (ch - '\u0660')),
                _ => ch,
            });
        }

        return builder.ToString();
    }

    /// <summary>Group with the Persian thousands separator and Persian digits: 120000 → «۱۲۰٬۰۰۰».</summary>
    public static string Format(long value)
    {
        var negative = value < 0;
        var digits = Math.Abs(value).ToString(CultureInfo.InvariantCulture);
        var builder = new StringBuilder();
        var count = 0;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            if (count > 0 && count % 3 == 0)
            {
                builder.Insert(0, GroupSeparator);
            }

            builder.Insert(0, digits[i]);
            count++;
        }

        return (negative ? "-" : string.Empty) + ToPersian(builder.ToString());
    }

    /// <summary>
    /// Amount + currency word. The unit word follows the recorded currency_unit
    /// (PHP policy: «toman» → تومان, otherwise ریال) — never an assumption about
    /// the invoice's unit; the recorded number is shown as-is, never converted.
    /// </summary>
    public static string FormatAmount(long amount, string? currencyUnit) =>
        $"{Format(amount)} {UnitWord(currencyUnit)}";

    public static string UnitWord(string? currencyUnit) =>
        currencyUnit == "toman" ? "تومان" : "ریال";
}
