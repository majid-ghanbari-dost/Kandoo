using System.Security.Cryptography;

namespace Kandoo.Cloud;

public static class PublicTokenGenerator
{
    public const int EntropyBytes = 16;

    public static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(EntropyBytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool IsValid(string? token)
    {
        if (token is null || token.Length is < 22 or > 128)
        {
            return false;
        }

        return token.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
    }
}
