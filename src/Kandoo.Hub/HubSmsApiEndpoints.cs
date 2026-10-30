using Kandoo.Core;

namespace Kandoo.Hub;

public sealed record HubSmsResendRequest(string? OperationId, string? OriginalOperationId);

public sealed record HubSmsGatewayResult(
    string Status,
    string? OperationId = null,
    Guid? InvoiceId = null,
    bool IdempotentReplay = false,
    string? ProviderMessageId = null,
    DateTimeOffset? CreatedAt = null,
    DateTimeOffset? SentAt = null,
    int? RetryAfterMs = null);

/// <summary>Server-internal delivery boundary. Hub sessions authorize callers; gateway credentials never enter the API body.</summary>
public interface IHubSmsDeliveryGateway
{
    Task<HubSmsGatewayResult> ResendAsync(
        Guid storeId,
        Guid invoiceId,
        string operationId,
        string originalOperationId,
        CancellationToken cancellationToken = default);

    Task<HubSmsGatewayResult?> GetStatusAsync(
        Guid storeId,
        Guid invoiceId,
        string operationId,
        CancellationToken cancellationToken = default);
}

public sealed class DisabledHubSmsDeliveryGateway : IHubSmsDeliveryGateway
{
    public Task<HubSmsGatewayResult> ResendAsync(
        Guid storeId,
        Guid invoiceId,
        string operationId,
        string originalOperationId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new HubSmsGatewayResult("SMS_GATEWAY_NOT_CONFIGURED"));

    public Task<HubSmsGatewayResult?> GetStatusAsync(
        Guid storeId,
        Guid invoiceId,
        string operationId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<HubSmsGatewayResult?>(new HubSmsGatewayResult("SMS_GATEWAY_NOT_CONFIGURED"));
}

public static partial class HubApiFirstEndpoints
{
    private static void MapSmsApi(WebApplication app)
    {
        app.MapPost("/v1/stores/{store_id}/invoices/{invoice_id}/sms/resend", async (
            Guid store_id,
            Guid invoice_id,
            HubSmsResendRequest? request,
            HttpContext context,
            IHubAccessStore access,
            IClock clock,
            IHubSmsDeliveryGateway gateway,
            CancellationToken cancellationToken) =>
        {
            var auth = Authenticate(context, store_id, access, clock);
            if (!auth.IsAuthenticated)
            {
                return AuthError(auth.Code);
            }

            if (request is null || !ValidOperationId(request.OperationId) || !ValidOperationId(request.OriginalOperationId) ||
                request.OperationId == request.OriginalOperationId)
            {
                return HubHttp.Error(400, "VALIDATION_ERROR", "Distinct operation_id and original_operation_id are required.");
            }

            var result = await gateway.ResendAsync(
                store_id, invoice_id, request.OperationId!, request.OriginalOperationId!, cancellationToken);
            return Results.Json(result, HubJson.Options, statusCode: SmsHttpStatus(result.Status));
        });

        app.MapGet("/v1/stores/{store_id}/invoices/{invoice_id}/sms/{operation_id}", async (
            Guid store_id,
            Guid invoice_id,
            string operation_id,
            HttpContext context,
            IHubAccessStore access,
            IClock clock,
            IHubSmsDeliveryGateway gateway,
            CancellationToken cancellationToken) =>
        {
            var auth = Authenticate(context, store_id, access, clock);
            if (!auth.IsAuthenticated)
            {
                return AuthError(auth.Code);
            }

            if (!ValidOperationId(operation_id))
            {
                return HubHttp.Error(400, "VALIDATION_ERROR", "A valid SMS operation_id is required.");
            }

            var result = await gateway.GetStatusAsync(store_id, invoice_id, operation_id, cancellationToken);
            return result is null
                ? HubHttp.Error(404, "SMS_DELIVERY_NOT_FOUND", "The SMS delivery does not exist in this store and invoice.")
                : Results.Json(result, HubJson.Options, statusCode: SmsHttpStatus(result.Status));
        });
    }

    private static bool ValidOperationId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
        value.All(character => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '.');

    private static int SmsHttpStatus(string status) => status switch
    {
        "SENT" or "DUPLICATE" => 200,
        "SMS_DELIVERY_NOT_FOUND" => 404,
        "OPERATION_ID_CONFLICT" => 409,
        "SMS_SEND_IN_PROGRESS" or "RATE_LIMITED" => 429,
        "SMS_GATEWAY_NOT_CONFIGURED" => 503,
        _ => 400
    };
}
