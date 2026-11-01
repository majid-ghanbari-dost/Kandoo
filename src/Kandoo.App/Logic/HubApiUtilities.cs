using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Kandoo.App;

internal static class HubApiUtilities
{
    public static StringContent JsonBody(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    public static void Authorize(HttpRequestMessage request, HubConnectionSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SessionToken))
        {
            throw new ArgumentException("A Hub session token is required.", nameof(settings));
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.SessionToken);
    }

    public static string ValidatedBaseUrl(HubConnectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!Uri.TryCreate(settings.BaseUrl?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Hub base URL must be an absolute HTTP or HTTPS URL.", nameof(settings));
        }

        if (settings.StoreId == Guid.Empty)
        {
            throw new ArgumentException("A Hub store ID is required.", nameof(settings));
        }

        return uri.AbsoluteUri.TrimEnd('/');
    }

    public static async Task<JsonElement> ReadResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonDocument? document = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    document = JsonDocument.Parse(body);
                }
                catch (JsonException) when (!response.IsSuccessStatusCode)
                {
                    // Preserve the HTTP failure below even when an intermediary returns non-JSON.
                }
            }

            if (!response.IsSuccessStatusCode)
            {
                var code = document is not null &&
                    document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("code", out var errorCode)
                        ? errorCode.GetString()
                        : null;
                throw new InvalidOperationException(
                    $"Hub request failed with HTTP {(int)response.StatusCode}" +
                    (string.IsNullOrWhiteSpace(code) ? "." : $" ({code})."));
            }

            if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Hub returned an empty or non-object success response.");
            }

            return document.RootElement.Clone();
        }
        finally
        {
            document?.Dispose();
        }
    }

    public static string RequiredString(JsonElement value, string propertyName)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new JsonException($"Required Hub string is missing: {propertyName}.");
        }

        return property.GetString()!;
    }

    public static Guid RequiredGuid(JsonElement value, string propertyName)
    {
        var text = RequiredString(value, propertyName);
        if (!Guid.TryParse(text, out var result) || result == Guid.Empty)
        {
            throw new JsonException($"Required Hub GUID is invalid: {propertyName}.");
        }

        return result;
    }
}
