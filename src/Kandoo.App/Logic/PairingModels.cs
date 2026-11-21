using System.Text.Json;

namespace Kandoo.App;

public sealed record PairingCode(
    string PairingId,
    string Nonce,
    Guid StoreId,
    string HubEndpoint);

public sealed record PairingResult(
    Guid DeviceId,
    Guid StoreId,
    string HubEndpoint,
    string SessionToken,
    string DeviceCredential,
    string? OperatorLevel);

public static class PairingCodeParser
{
    public static PairingCode Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormatException("A pairing QR JSON code is required.");
        }

        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            HubApiUtilities.RequiredString(root, "protocol_version") != "0" ||
            HubApiUtilities.RequiredString(root, "device_role") != "COMPANION" ||
            !root.TryGetProperty("single_use", out var singleUse) ||
            singleUse.ValueKind != JsonValueKind.True)
        {
            throw new JsonException("The pairing code is not a single-use companion QR payload.");
        }

        var pairingId = HubApiUtilities.RequiredString(root, "pairing_id");
        var nonce = HubApiUtilities.RequiredString(root, "nonce");
        var storeId = HubApiUtilities.RequiredGuid(root, "store_id");
        var endpoint = HubApiUtilities.RequiredString(root, "hub_endpoint");
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.IsLoopback)
        {
            throw new FormatException("The pairing Hub endpoint must be a non-loopback HTTPS URL.");
        }

        return new PairingCode(pairingId, nonce, storeId, uri.AbsoluteUri.TrimEnd('/'));
    }
}
