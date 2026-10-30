using System.Text.Json;
using Kandoo.Core;

namespace Kandoo.Hub;

public sealed record HubInvoiceDraftRequest(
    string? OperationId,
    string? ServiceType,
    string? CustomerPhone,
    string? ExternalInvoiceNumber);

public sealed record HubInvoiceItemRequest(
    string? OperationId,
    string? ServiceType,
    string? Description,
    long? QuantityMillis,
    long? UnitPriceRials,
    string? ProductId);

public sealed record HubInvoiceIssueRequest(
    string? OperationId,
    string? ServiceType,
    bool Print = false);

public sealed record HubInvoicePrintResult(string Status);

public interface IHubInvoicePrintAdapter
{
    Task<HubInvoicePrintResult> PrintAsync(Guid storeId, Guid invoiceId, CancellationToken cancellationToken = default);
}

/// <summary>Default no-device adapter; issuance never depends on physical print availability.</summary>
public sealed class DisabledHubInvoicePrintAdapter : IHubInvoicePrintAdapter
{
    public Task<HubInvoicePrintResult> PrintAsync(Guid storeId, Guid invoiceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new HubInvoicePrintResult("PRINT_ADAPTER_DISABLED"));
}

public static partial class HubApiFirstEndpoints
{
    private static void MapInvoiceApi(WebApplication app)
    {
        app.MapGet("/v1/stores/{store_id}/service-types", (
            Guid store_id,
            HttpContext context,
            IHubAccessStore access,
            IClock clock) =>
        {
            var auth = Authenticate(context, store_id, access, clock);
            if (!auth.IsAuthenticated)
            {
                return AuthError(auth.Code);
            }

            return Results.Json(new
            {
                status = "OK",
                items = new[]
                {
                    new { service_type = "SALE", enabled = true, disabled_error = (string?)null },
                    new { service_type = "RENTAL", enabled = false, disabled_error = (string?)"SERVICE_TYPE_DISABLED" },
                    new { service_type = "OTHER", enabled = false, disabled_error = (string?)"SERVICE_TYPE_DISABLED" }
                }
            }, HubJson.Options);
        });

        app.MapPost("/v1/stores/{store_id}/invoices/drafts", (
            Guid store_id,
            HubInvoiceDraftRequest? request,
            HttpContext context,
            IHubAccessStore access,
            IClock clock,
            HubOperationService operations) =>
        {
            var auth = Authenticate(context, store_id, access, clock);
            if (!auth.IsAuthenticated)
            {
                return AuthError(auth.Code);
            }

            if (request is null || string.IsNullOrWhiteSpace(request.OperationId) || string.IsNullOrWhiteSpace(request.CustomerPhone))
            {
                return HubHttp.Error(400, "VALIDATION_ERROR", "operation_id, service_type, and customer_phone are required.");
            }

            if (ServiceTypeError(request.ServiceType) is { } disabled)
            {
                return disabled;
            }

            var values = new Dictionary<string, object?>
            {
                ["customer_phone"] = request.CustomerPhone,
                ["service_type"] = "SALE"
            };
            if (request.ExternalInvoiceNumber is not null)
            {
                values["external_invoice_number"] = request.ExternalInvoiceNumber;
            }

            var result = Submit(
                store_id, auth.Session!, operations, request.OperationId, "invoice.draft_create", HubJson.Element(values));
            return OperationWithCurrency(result);
        });

        app.MapPost("/v1/stores/{store_id}/invoices/{invoice_id}/items", (
            Guid store_id,
            Guid invoice_id,
            HubInvoiceItemRequest? request,
            HttpContext context,
            IHubAccessStore access,
            IClock clock,
            HubOperationService operations) =>
        {
            var auth = Authenticate(context, store_id, access, clock);
            if (!auth.IsAuthenticated)
            {
                return AuthError(auth.Code);
            }

            if (request is null || string.IsNullOrWhiteSpace(request.OperationId) || string.IsNullOrWhiteSpace(request.Description) ||
                request.QuantityMillis is null || request.UnitPriceRials is null)
            {
                return HubHttp.Error(400, "VALIDATION_ERROR", "operation_id, service_type, description, quantity, and price are required.");
            }

            if (ServiceTypeError(request.ServiceType) is { } disabled)
            {
                return disabled;
            }

            long lineTotal;
            try
            {
                var quantity = request.QuantityMillis.Value;
                var price = request.UnitPriceRials.Value;
                if (quantity <= 0 || price < 0)
                {
                    return HubHttp.Error(400, "VALIDATION_ERROR", "Quantity must be positive and price must be non-negative.");
                }

                var scaled = checked(quantity * price);
                if (scaled % 1000 != 0)
                {
                    return HubHttp.Error(400, "VALIDATION_ERROR", "The exact line total must be an integer number of Rials.");
                }

                lineTotal = scaled / 1000;
            }
            catch (OverflowException)
            {
                return HubHttp.Error(400, "VALIDATION_ERROR", "The line total is outside the signed integer range.");
            }

            var values = new Dictionary<string, object?>
            {
                ["invoice_id"] = invoice_id,
                ["description"] = request.Description,
                ["quantity_millis"] = request.QuantityMillis.Value,
                ["unit_price_rials"] = request.UnitPriceRials.Value,
                ["line_total_rials"] = lineTotal,
                ["service_type"] = "SALE"
            };
            if (request.ProductId is not null)
            {
                values["product_id"] = request.ProductId;
            }

            var result = Submit(
                store_id, auth.Session!, operations, request.OperationId, "invoice.item_add", HubJson.Element(values));
            return OperationWithCurrency(result);
        });

        app.MapPost("/v1/stores/{store_id}/invoices/{invoice_id}/issue", async (
            Guid store_id,
            Guid invoice_id,
            HubInvoiceIssueRequest? request,
            HttpContext context,
            IHubAccessStore access,
            IClock clock,
            HubOperationService operations,
            IHubInvoicePrintAdapter printer,
            CancellationToken cancellationToken) =>
        {
            var auth = Authenticate(context, store_id, access, clock);
            if (!auth.IsAuthenticated)
            {
                return AuthError(auth.Code);
            }

            if (request is null || string.IsNullOrWhiteSpace(request.OperationId))
            {
                return HubHttp.Error(400, "VALIDATION_ERROR", "operation_id and service_type are required.");
            }

            if (ServiceTypeError(request.ServiceType) is { } disabled)
            {
                return disabled;
            }

            var result = Submit(
                store_id, auth.Session!, operations, request.OperationId, "invoice.issue",
                HubJson.Element(new { invoice_id, service_type = "SALE" }));
            var printStatus = request.Print ? "NOT_ATTEMPTED" : "NOT_REQUESTED";
            if (request.Print && result.Body.Status == "SUCCEEDED")
            {
                try
                {
                    printStatus = (await printer.PrintAsync(store_id, invoice_id, cancellationToken)).Status;
                }
                catch (InvalidOperationException)
                {
                    printStatus = "PRINT_FAILED";
                }
            }

            return OperationWithPrint(result, request.Print, printStatus);
        });
    }

    private static IResult? ServiceTypeError(string? serviceType)
    {
        if (serviceType == "SALE")
        {
            return null;
        }

        return serviceType is "RENTAL" or "OTHER"
            ? HubHttp.Error(501, "SERVICE_TYPE_DISABLED", "Only Sale is active in the MVP.", "ENABLE_SERVICE_TYPE")
            : HubHttp.Error(400, "VALIDATION_ERROR", "service_type must be SALE, RENTAL, or OTHER.", "FIX_REQUEST");
    }

    private static IResult OperationWithPrint(HubOperationResult result, bool requested, string printStatus) => Results.Json(new
    {
        status = result.Body.Status,
        result_status = result.Body.ResultStatus,
        operation_seq = result.Body.OperationSeq,
        request_hash = result.Body.RequestHash,
        idempotent_replay = result.Body.IdempotentReplay,
        response = result.Body.Response,
        print = new { requested, status = printStatus },
        currency = HubInvoiceCurrency.Body
    }, HubJson.Options, statusCode: result.HttpStatus);
}
