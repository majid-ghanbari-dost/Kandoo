using System.Text.Json;
using System.Text.Json.Nodes;
using Kandoo.Core;

namespace Kandoo.Hub;

public sealed record HubProductUpdateRequest(
    string? OperationId,
    long? BaseVersion,
    JsonElement Payload);

public static partial class HubApiFirstEndpoints
{
    public static void MapHubApiFirstEndpoints(this WebApplication app)
    {
        MapProductApi(app);
        MapInvoiceApi(app);
        MapReviewApi(app);
        MapSmsApi(app);
    }

    private static void MapProductApi(WebApplication app)
    {
        app.MapGet("/v1/stores/{store_id}/products", (
            Guid store_id,
            string? query,
            bool? include_retired,
            int? limit,
            string? cursor,
            HttpContext context,
            IHubAccessStore access,
            IClock clock,
            HubProductCatalogService catalog) =>
        {
            var auth = Authenticate(context, store_id, access, clock);
            if (!auth.IsAuthenticated)
            {
                return AuthError(auth.Code);
            }

            try
            {
                var page = catalog.List(store_id, query, include_retired == true, limit ?? 50, cursor);
                return Results.Json(new
                {
                    status = "OK",
                    items = page.Items.Select(ProductBody).ToArray(),
                    total_count = page.TotalCount,
                    next_cursor = page.NextCursor,
                    matching_fields = HubProductMatching.Fields
                }, HubJson.Options);
            }
            catch (ArgumentException exception)
            {
                return QueryError(exception);
            }
        });

        app.MapGet("/v1/stores/{store_id}/products/cascade-options", (
            Guid store_id,
            string? name,
            int? limit,
            string? cursor,
            HttpContext context,
            IHubAccessStore access,
            IClock clock,
            HubProductCatalogService catalog) =>
        {
            var auth = Authenticate(context, store_id, access, clock);
            if (!auth.IsAuthenticated)
            {
                return AuthError(auth.Code);
            }

            try
            {
                var page = catalog.CascadeOptions(store_id, name ?? string.Empty, limit ?? 50, cursor);
                return Results.Json(new
                {
                    status = "CASCADE_OPTIONS",
                    scope = "STORE_SAME_NAME",
                    items = page.Items.Select(ProductBody).ToArray(),
                    total_count = page.TotalCount,
                    next_cursor = page.NextCursor
                }, HubJson.Options);
            }
            catch (ArgumentException exception)
            {
                return QueryError(exception);
            }
        });

        app.MapPatch("/v1/stores/{store_id}/products/{product_id}", (
            Guid store_id,
            string product_id,
            HubProductUpdateRequest? request,
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

            if (request is null || string.IsNullOrWhiteSpace(request.OperationId) || request.BaseVersion is null ||
                string.IsNullOrWhiteSpace(product_id) || request.Payload.ValueKind != JsonValueKind.Object)
            {
                return HubHttp.Error(400, "VALIDATION_ERROR", "operation_id, base_version, and an object payload are required.");
            }

            if (request.Payload.TryGetProperty("product_id", out var routeId) &&
                (routeId.ValueKind != JsonValueKind.String || routeId.GetString() != product_id))
            {
                return HubHttp.Error(400, "VALIDATION_ERROR", "payload product_id must match the route.");
            }

            var node = JsonNode.Parse(request.Payload.GetRawText())!.AsObject();
            node["product_id"] = product_id;
            var payload = JsonSerializer.SerializeToElement(node, HubJson.Options);
            var result = Submit(
                store_id, auth.Session!, operations, request.OperationId, "product.update", payload, request.BaseVersion);
            return OperationWithCurrency(result);
        });
    }

    private static object ProductBody(HubProductView view)
    {
        var product = view.Product;
        return new
        {
            store_id = product.StoreId,
            product_id = product.ProductId,
            name = product.Name,
            normalized_name = product.NormalizedName,
            type = product.Category,
            category = product.Category,
            color = product.Color,
            unit_price_rials = product.UnitPriceRials,
            product_tag_number = product.ProductTagNumber,
            stock_millis = view.StockMillis,
            version = product.Version,
            is_active = product.IsActive,
            specification = product.Specification,
            size = product.Size,
            material = product.Material,
            brand = product.Brand,
            barcode = product.Barcode,
            operator_fields = view.OperatorFields.Select((field, position) => new
            {
                position,
                key = field.Key,
                value = field.Value
            }).ToArray(),
            match = new
            {
                name = product.Name,
                type = product.Category,
                color = product.Color,
                unit_price_rials = product.UnitPriceRials,
                product_tag_number = product.ProductTagNumber,
                stock_millis = view.StockMillis
            },
            currency = HubInvoiceCurrency.Body
        };
    }

    private static class HubProductMatching
    {
        public static readonly string[] Fields =
        {
            "name", "type", "color", "unit_price_rials", "product_tag_number", "stock_millis"
        };
    }

    private static IResult QueryError(ArgumentException exception) => HubHttp.Error(
        400,
        exception.Message.StartsWith("CURSOR_INVALID", StringComparison.Ordinal) ? "CURSOR_INVALID" : "VALIDATION_ERROR",
        "The store-scoped query, limit, or cursor is invalid.",
        "FIX_REQUEST");

    private static HubAuthenticationResult Authenticate(
        HttpContext context,
        Guid storeId,
        IHubAccessStore access,
        IClock clock)
    {
        var auth = access.Authenticate(HubHttp.BearerToken(context) ?? string.Empty, clock.UtcNow);
        return auth.IsAuthenticated && auth.Session!.StoreId != storeId
            ? new HubAuthenticationResult("STORE_ACCESS_DENIED")
            : auth;
    }

    private static IResult AuthError(string code) => code switch
    {
        "DEVICE_REVOKED" or "DEVICE_NOT_PAIRED" or "SESSION_REVOKED" or "STORE_ACCESS_DENIED" =>
            HubHttp.Error(403, code, "The device or session is not authorized."),
        "SESSION_EXPIRED" => HubHttp.Error(401, code, "The session has expired.", "RESUME_SESSION"),
        _ => HubHttp.Error(401, "SESSION_INVALID", "A valid session token is required.")
    };

    private static HubOperationResult Submit(
        Guid storeId,
        HubSession session,
        HubOperationService operations,
        string operationId,
        string operationType,
        JsonElement payload,
        long? baseVersion = null)
    {
        var domain = new OperationRequest(storeId, session.DeviceId, operationId, operationType, baseVersion, payload);
        return operations.Submit(storeId, session, new HubOperationRequest(
            "0", session.SessionId, storeId, session.DeviceId, operationId, operationType,
            baseVersion, payload, domain.RequestHash, null));
    }

    private static IResult OperationWithCurrency(HubOperationResult result) => Results.Json(new
    {
        status = result.Body.Status,
        result_status = result.Body.ResultStatus,
        operation_seq = result.Body.OperationSeq,
        request_hash = result.Body.RequestHash,
        idempotent_replay = result.Body.IdempotentReplay,
        response = result.Body.Response,
        currency = HubInvoiceCurrency.Body
    }, HubJson.Options, statusCode: result.HttpStatus);
}

public static class HubInvoiceCurrency
{
    public const string Code = "IRR";
    public const string Unit = "Iranian Rial";
    public const string ProductSource = "src/Kandoo.Core/ProductCatalog.cs:Product.UnitPriceRials";
    public const string InvoiceSource = "src/Kandoo.Core/Invoice.cs:InvoiceItem.UnitPriceRials/LineTotalRials";

    public static readonly object Body = new
    {
        code = Code,
        unit = Unit,
        integer_amounts = true,
        product_source = ProductSource,
        invoice_source = InvoiceSource
    };
}
