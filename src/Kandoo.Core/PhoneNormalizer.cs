namespace Kandoo.Core;

public static class PhoneNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A phone number is required.", nameof(value));
        }

        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length < 3 || !value.TrimStart().StartsWith('+'))
        {
            throw new ArgumentException("The synthetic phone must use international '+' syntax.", nameof(value));
        }

        return "+" + digits;
    }
}
