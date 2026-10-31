using System.Security.Cryptography;
using System.Text;
using Kandoo.Core;

namespace Kandoo.Hub;

public enum HubDeviceRole
{
    Hub,
    Companion
}

public enum HubPairingStatus
{
    Active,
    Revoked
}

public static class HubOperatorLevel
{
    public const string Seller = "SELLER";
    public const string Operator = "OPERATOR";
    public const string Manager = "MANAGER";

    public static bool IsSupported(string? value) =>
        value is Seller or Operator or Manager;
}

public enum HubSessionStatus
{
    Active,
    Expired,
    Revoked
}

public sealed record HubDevice(
    Guid StoreId,
    Guid DeviceId,
    HubDeviceRole DeviceRole,
    HubPairingStatus PairingStatus,
    Guid? OwnerUserId,
    string Label,
    string Platform,
    string? DevicePublicKey,
    string? OperatorLevel = null);

public sealed record HubSession(
    string SessionId,
    Guid StoreId,
    Guid DeviceId,
    HubDeviceRole DeviceRole,
    Guid? OwnerUserId,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    HubSessionStatus Status);

public sealed record HubSessionIssued(HubSession Session, string SessionToken);

public sealed record HubPairingGrant(
    string PairingId,
    Guid StoreId,
    Guid HubDeviceId,
    string HubEndpoint,
    string ProofHash,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    bool Consumed,
    string OperatorLevel = HubOperatorLevel.Operator);

public sealed record HubPairingConsumeResult(
    string Code,
    HubDevice? Device = null,
    HubSessionIssued? Session = null,
    string? DeviceCredential = null);

public sealed record HubSessionResumeResult(string Code, HubSessionIssued? Session = null);

public sealed record HubDeviceLevelChangeResult(string Code, HubDevice? Device = null);

public sealed record HubAuthenticationResult(
    string Code,
    HubSession? Session = null,
    HubDevice? Device = null)
{
    public bool IsAuthenticated => Code == "ACTIVE" && Session is not null && Device is not null;
}

public interface IHubAccessStore
{
    HubPairingGrant CreatePairingGrant(
        Guid storeId,
        Guid hubDeviceId,
        Guid ownerUserId,
        string hubEndpoint,
        string pairingId,
        string proofHash,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        string operatorLevel = HubOperatorLevel.Operator);

    HubPairingConsumeResult ConsumePairingGrant(
        string pairingId,
        string pairingProof,
        Guid deviceId,
        string label,
        string platform,
        string devicePublicKey,
        string deviceCredential,
        string sessionId,
        DateTimeOffset now,
        DateTimeOffset sessionExpiresAt);

    HubSessionIssued IssueOwnerSession(
        Guid storeId,
        Guid hubDeviceId,
        Guid ownerUserId,
        string sessionId,
        DateTimeOffset now,
        DateTimeOffset expiresAt);

    HubSessionResumeResult ResumeSession(
        Guid storeId,
        Guid deviceId,
        string deviceCredential,
        string sessionId,
        DateTimeOffset now,
        DateTimeOffset expiresAt);

    HubAuthenticationResult Authenticate(string sessionToken, DateTimeOffset now);
    IReadOnlyList<HubDevice> GetDevices(Guid storeId);
    HubDeviceLevelChangeResult ChangeDeviceLevel(
        Guid storeId, Guid deviceId, Guid ownerUserId, string operatorLevel, DateTimeOffset now);
    bool RevokeDevice(Guid storeId, Guid deviceId, Guid ownerUserId, DateTimeOffset now);
    int DisconnectDevice(Guid storeId, Guid deviceId, DateTimeOffset now);
}

public static class HubToken
{
    public static string NewToken(int entropyBytes = 32)
    {
        if (entropyBytes < 16)
        {
            throw new ArgumentOutOfRangeException(nameof(entropyBytes), "At least 128 bits are required.");
        }

        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(entropyBytes))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static string Hash(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    public static bool FixedTimeEquals(string left, string right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left),
            Encoding.ASCII.GetBytes(right));
    }
}
