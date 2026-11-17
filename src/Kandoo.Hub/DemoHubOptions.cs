using System.Net;

namespace Kandoo.Hub;

/// <summary>Demo profile settings for Order 15 local-demo week (D-L).</summary>
/// <remarks>
/// D-L: "آدرس پایه کلاود = یک تنظیم" — the Hub exposes a single demo base address
/// (LAN base address) plus a demo mode flag. Raw cleartext HTTP is permitted only
/// to private LAN ranges in the demo Android build; the app enforces this at startup.
/// </remarks>
public sealed class DemoHubOptions
{
    public bool Mode { get; set; }
    public string? BaseUrl { get; set; }

    public void Validate()
    {
        if (!Mode)
            return;

        if (string.IsNullOrWhiteSpace(BaseUrl))
            throw new ArgumentException("Demo base URL is required when demo mode is enabled.");

        if (!Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Demo base URL must be an absolute HTTP or HTTPS URL.");
        }

        if (uri.Scheme == Uri.UriSchemeHttp && !IsPrivateLanOrLoopback(uri.Host))
        {
            throw new ArgumentException(
                "Cleartext HTTP demo base URL is allowed only for private LAN ranges " +
                "(10/8, 172.16/12, 192.168/16) or localhost.");
        }
    }

    private static bool IsPrivateLanOrLoopback(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "127.0.0.1", StringComparison.Ordinal) ||
            string.Equals(host, "::1", StringComparison.Ordinal))
        {
            return true;
        }

        if (!IPAddress.TryParse(host, out var address))
            return false; // hostnames are not cleartext HTTP targets in demo

        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
        {
            // 10/8
            if (bytes[0] == 10) return true;
            // 172.16/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            // 192.168/16
            if (bytes[0] == 192 && bytes[1] == 168) return true;
        }
        else if (bytes.Length == 16)
        {
            // IPv6 loopback / unique local (fc00::/7)
            if (address.IsLoopback) return true;
            if (bytes[0] == 0xfd || bytes[0] == 0xfc) return true;
        }

        return false;
    }
}
