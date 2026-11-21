using System.Security.Cryptography;
using System.Text.Json;

namespace Kandoo.App;

public sealed class HubPairingDataSource
{
    private readonly HttpClient _client;
    private readonly Func<Guid> _deviceIdFactory;
    private readonly Func<string> _publicKeyFactory;

    public HubPairingDataSource(
        HttpClient client,
        Func<Guid>? deviceIdFactory = null,
        Func<string>? publicKeyFactory = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _deviceIdFactory = deviceIdFactory ?? Guid.NewGuid;
        _publicKeyFactory = publicKeyFactory ?? CreatePublicKey;
    }

    public async Task<PairingResult> PairAsync(
        PairingCode code,
        string? label,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("A device label is required.", nameof(label));
        }

        var deviceId = _deviceIdFactory();
        if (deviceId == Guid.Empty)
        {
            throw new InvalidOperationException("A nonempty device identity is required.");
        }

        var publicKey = _publicKeyFactory();
        if (string.IsNullOrWhiteSpace(publicKey))
        {
            throw new InvalidOperationException("A device public key is required.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{code.HubEndpoint}/v1/pairing-grants/consume");
        request.Content = HubApiUtilities.JsonBody(new
        {
            pairing_id = code.PairingId,
            nonce = code.Nonce,
            device_id = deviceId,
            label = label.Trim(),
            platform = "ANDROID",
            device_public_key = publicKey
        });
        using var response = await _client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var root = await HubApiUtilities.ReadResponseAsync(response, cancellationToken);
        if (HubApiUtilities.RequiredString(root, "status") != "DEVICE_PAIRED" ||
            !root.TryGetProperty("device", out var device) || device.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("qr_consumed", out var consumed) || consumed.ValueKind != JsonValueKind.True ||
            !root.TryGetProperty("hub_role_granted", out var hubRole) || hubRole.ValueKind != JsonValueKind.False)
        {
            throw new JsonException("Hub returned an unsupported pairing result.");
        }

        var storeId = HubApiUtilities.RequiredGuid(device, "store_id");
        if (storeId != code.StoreId || HubApiUtilities.RequiredGuid(device, "device_id") != deviceId)
        {
            throw new JsonException("Hub returned a pairing identity that does not match the request.");
        }

        string? operatorLevel = null;
        if (device.TryGetProperty("operator_level", out var level) && level.ValueKind != JsonValueKind.Null)
        {
            operatorLevel = level.GetString();
            if (!DeviceLevels.IsSupported(operatorLevel))
            {
                throw new JsonException("Hub returned an unsupported operator level.");
            }
        }

        return new PairingResult(
            deviceId,
            storeId,
            code.HubEndpoint,
            HubApiUtilities.RequiredString(root, "session_token"),
            HubApiUtilities.RequiredString(root, "device_credential"),
            operatorLevel);
    }

    private static string CreatePublicKey()
    {
        using var rsa = RSA.Create();
        rsa.KeySize = 2048;
        return Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
    }
}
