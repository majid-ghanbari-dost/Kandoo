using System.Text.Json;

namespace Kandoo.App;

public static class DeviceLevels
{
    public const string Seller = "SELLER";
    public const string Operator = "OPERATOR";
    public const string Manager = "MANAGER";

    public static bool IsSupported(string? value) => value is Seller or Operator or Manager;
}

public sealed record DeviceRow(
    Guid StoreId,
    Guid DeviceId,
    string Label,
    string Platform,
    string DeviceRole,
    string PairingStatus,
    string? OperatorLevel)
{
    public string DeviceIdDisplay => DeviceId.ToString("D");
    public string LevelDisplay => OperatorLevel ?? "ثبت نشده";
    public string DetailsDisplay => $"{DeviceRole} · {PairingStatus} · {Platform} · {LevelDisplay}";
    public bool CanAdminister => DeviceRole == "COMPANION" && PairingStatus == "ACTIVE";
}

public static class DeviceMapper
{
    public static IReadOnlyList<DeviceRow> MapArray(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Hub device list must be an array.");
        }

        return root.EnumerateArray().Select(Map).ToArray();
    }

    public static DeviceRow Map(JsonElement device)
    {
        if (device.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Hub device entry must be an object.");
        }

        var role = HubApiUtilities.RequiredString(device, "device_role");
        if (role is not ("HUB" or "COMPANION"))
        {
            throw new JsonException("Hub returned an unsupported device role.");
        }

        var status = HubApiUtilities.RequiredString(device, "pairing_status");
        if (status is not ("ACTIVE" or "REVOKED"))
        {
            throw new JsonException("Hub returned an unsupported pairing status.");
        }

        string? level = null;
        if (device.TryGetProperty("operator_level", out var operatorLevel) &&
            operatorLevel.ValueKind != JsonValueKind.Null)
        {
            level = operatorLevel.GetString();
            if (!DeviceLevels.IsSupported(level))
            {
                throw new JsonException("Hub returned an unsupported operator level.");
            }
        }

        return new DeviceRow(
            HubApiUtilities.RequiredGuid(device, "store_id"),
            HubApiUtilities.RequiredGuid(device, "device_id"),
            HubApiUtilities.RequiredString(device, "label"),
            HubApiUtilities.RequiredString(device, "platform"),
            role,
            status,
            level);
    }
}
