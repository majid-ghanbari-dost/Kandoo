using System.Globalization;
using System.Text;

namespace Kandoo.App;

/// <summary>
/// Resolves Hub connection settings across pages by combining caller-provided settings
/// with the persisted shared token file (<c>kandoo.device-token.v1.json</c>) and the
/// local Hub URL file (<c>kandoo.hub-url.v1.txt</c>) written by First-Run Setup.
/// </summary>
public static class HubConnectionResolver
{
    public const string HubUrlFileName = "kandoo.hub-url.v1.txt";

    public static HubConnectionSettings? Resolve(
        HubConnectionSettings? provided = null,
        string? localAppDataFolder = null)
    {
        var folder = string.IsNullOrWhiteSpace(localAppDataFolder)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localAppDataFolder;

        var baseUrl = provided?.BaseUrl?.Trim();
        var storeId = provided?.StoreId ?? Guid.Empty;
        var sessionToken = provided?.SessionToken?.Trim();

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = LoadHubBaseUrl(folder);
        }

        if (storeId == Guid.Empty || string.IsNullOrWhiteSpace(sessionToken))
        {
            var tokenRecord = TryLoadToken(folder);
            if (tokenRecord is not null)
            {
                if (storeId == Guid.Empty)
                {
                    storeId = tokenRecord.StoreId;
                }

                if (string.IsNullOrWhiteSpace(sessionToken))
                {
                    sessionToken = tokenRecord.Token;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(baseUrl) ||
            !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            storeId == Guid.Empty ||
            string.IsNullOrWhiteSpace(sessionToken))
        {
            return null;
        }

        return new HubConnectionSettings(uri.AbsoluteUri.TrimEnd('/'), storeId, sessionToken);
    }

    public static string? LoadHubBaseUrl(string? localAppDataFolder = null)
    {
        var folder = string.IsNullOrWhiteSpace(localAppDataFolder)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localAppDataFolder;

        if (string.IsNullOrWhiteSpace(folder))
        {
            return null;
        }

        var path = Path.Combine(folder, HubUrlFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var text = File.ReadAllText(path).Trim();
            return Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
                   (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                ? uri.AbsoluteUri.TrimEnd('/')
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string NormalizeAsciiDigits(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            if (ch is >= '۰' and <= '۹')
            {
                builder.Append((char)('0' + (ch - '۰')));
            }
            else if (ch is >= '٠' and <= '٩')
            {
                builder.Append((char)('0' + (ch - '٠')));
            }
            else if (ch is '−' or '–' or '—')
            {
                builder.Append('-');
            }
            else if (ch is '٬' or ',' or '،')
            {
                // Strip thousand separators when normalizing numeric text.
            }
            else if (ch is '\u200E' or '\u200F' or '\u200C')
            {
                // Strip directional / ZWNJ marks around signed Persian numbers (e.g. "‎−۲").
            }
            else
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Trim();
    }

    public static string NormalizeSearchDigits(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(input.Length);
        foreach (var ch in input.Trim())
        {
            if (ch is >= '۰' and <= '۹')
            {
                builder.Append((char)('0' + (ch - '۰')));
            }
            else if (ch is >= '٠' and <= '٩')
            {
                builder.Append((char)('0' + (ch - '٠')));
            }
            else if (ch == 'ي')
            {
                builder.Append('ی');
            }
            else if (ch == 'ك')
            {
                builder.Append('ک');
            }
            else
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }

    public static string FormatQuantityMillis(long? stockMillis)
    {
        if (stockMillis is not long millis)
        {
            return "—";
        }

        if (millis % 1000 == 0)
        {
            return (millis / 1000).ToString("N0", CultureInfo.InvariantCulture);
        }

        var exact = millis / 1000m;
        return exact.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static DeviceTokenRecord? TryLoadToken(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return null;
        }

        try
        {
            return new SharedTokenFile(folder).Load();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
