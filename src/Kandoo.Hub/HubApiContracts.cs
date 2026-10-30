using System.Text.Json;
using System.Text.Json.Serialization;
using Kandoo.Core;

namespace Kandoo.Hub;

public static class HubJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value, Options);
}

public sealed record HubErrorBody(string Code, string Message, string RetryHint);

public sealed record OwnerSessionRequest(
    Guid HubDeviceId,
    Guid OwnerUserId,
    string? BootstrapProof);

public sealed record PairingGrantRequest(
    Guid HubDeviceId,
    string? HubEndpoint,
    string? RequestedDeviceRole);

public sealed record PairingConsumeRequest(
    string? PairingId,
    string? Nonce,
    Guid DeviceId,
    string? Label,
    string? Platform,
    string? DevicePublicKey);

public sealed record HubProductRegistrationRequest(
    string? OperationId,
    string? Name,
    long? UnitPriceRials,
    long? InitialStockMillis,
    string? ProductId = null,
    string? AcknowledgedCandidateId = null,
    string? Category = null,
    string? Color = null,
    string? Size = null,
    string? Material = null,
    string? Brand = null,
    string? Barcode = null,
    string? Specification = null,
    string? Type = null,
    long? ProductTagNumber = null,
    IReadOnlyList<ProductOperatorField>? OperatorFields = null);

public sealed record SessionResumeRequest(string? DeviceCredential);

public sealed record HubOperationRequest(
    string? ProtocolVersion,
    string? SessionId,
    Guid? StoreId,
    Guid? DeviceId,
    string? OperationId,
    string? OperationType,
    long? BaseVersion,
    JsonElement Payload,
    string? RequestHash,
    DateTimeOffset? ClientSentAt);

public sealed record HubOperationReply(
    string Status,
    string ResultStatus,
    long? OperationSeq,
    string? RequestHash,
    bool IdempotentReplay,
    JsonElement Response);

public sealed record HubOperationResult(int HttpStatus, HubOperationReply Body);

internal sealed record StoredOperationResponse(
    string Status,
    string ResultStatus,
    JsonElement Response);

public sealed class SystemHubClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public static class HubHttp
{
    public static IResult Error(int status, string code, string message, string retryHint = "NONE") =>
        Results.Json(new HubErrorBody(code, message, retryHint), HubJson.Options, statusCode: status);

    public static string? BearerToken(HttpContext context)
    {
        var value = context.Request.Headers.Authorization.FirstOrDefault();
        const string prefix = "Bearer ";
        return value?.StartsWith(prefix, StringComparison.Ordinal) == true
            ? value[prefix.Length..].Trim()
            : null;
    }
}
