using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Kandoo.App;

/// <summary>
/// Per-install device identity for the P-31 customer API (X-Kandoo-Device).
/// The PHP side validates /^[A-Za-z0-9_-]{43}$/ — a 43-character base64url token.
/// The verified device is remembered on the phone (C-92): the id is generated
/// once and persisted with the customer session; it is never a credential.
/// </summary>
public static partial class CustomerDeviceId
{
    private const int RandomBytes = 32;

    [GeneratedRegex("^[A-Za-z0-9_-]{43}$")]
    private static partial Regex ValidPattern();

    public static string New()
    {
        var bytes = RandomNumberGenerator.GetBytes(RandomBytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public static bool IsValid(string? deviceId) =>
        deviceId is not null && ValidPattern().IsMatch(deviceId);

    public static void EnsureValid(string deviceId)
    {
        if (!IsValid(deviceId))
        {
            throw new ArgumentException(
                "The device id must be a 43-character base64url token.", nameof(deviceId));
        }
    }
}
