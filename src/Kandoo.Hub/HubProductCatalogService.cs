using System.Globalization;
using System.Text;
using System.Text.Json;
using Kandoo.Core;

namespace Kandoo.Hub;

public sealed record HubProductView(
    Product Product,
    long StockMillis,
    IReadOnlyList<ProductOperatorField> OperatorFields);

public sealed record HubProductPage(
    IReadOnlyList<HubProductView> Items,
    int TotalCount,
    string? NextCursor);

public sealed record HubProductRegistrationResult(
    ProductRegistrationStatus Status,
    Product? Product,
    long? StockMillis,
    string? CandidateProductId,
    long? OperationSeq,
    bool IdempotentReplay);

public sealed record HubProductRetirementResult(
    ProductUpdateStatus Status,
    Product? Product,
    int? CurrentVersion,
    long? OperationSeq,
    bool IdempotentReplay);

internal sealed record ProductRegistrationResponse(
    ProductRegistrationStatus Status,
    Product? Product,
    long? StockMillis,
    string? CandidateProductId);

public sealed class HubProductCatalogService
{
    private readonly SqliteHubStore _store;

    public HubProductCatalogService(SqliteHubStore store)
    {
        _store = store;
    }

    public HubProductRegistrationResult Register(
        Guid storeId,
        Guid deviceId,
        string operationId,
        ProductRegistrationInput input)
    {
        input = input.Validate();
        var values = new Dictionary<string, object?>
        {
            ["product_id"] = input.ProductId,
            ["name"] = input.Name,
            ["unit_price_rials"] = input.UnitPriceRials,
            ["initial_stock_millis"] = input.InitialStockMillis,
            ["acknowledged_candidate_id"] = input.AcknowledgedCandidateId,
            ["category"] = input.Category,
            ["color"] = input.Color,
            ["size"] = input.Size,
            ["material"] = input.Material,
            ["brand"] = input.Brand,
            ["barcode"] = input.Barcode,
            ["specification"] = input.Specification
        };
        if (input.ProductTagNumber is not null)
        {
            values["product_tag_number"] = input.ProductTagNumber;
        }

        if (input.PurchasePriceRials is not null)
        {
            values["purchase_price_rials"] = input.PurchasePriceRials;
        }

        if (input.OperatorFields is { Count: > 0 })
        {
            values["operator_fields"] = input.OperatorFields;
        }

        // The legacy shape is retained when C-23 features are unused so pre-upgrade retries keep their request hash.
        var payload = HubJson.Element(values);
        var request = new OperationRequest(storeId, deviceId, operationId, "product.create", null, payload);
        var result = _store.RegisterProduct(request, input);
        if (result.Status == OperationExecutionStatus.OperationIdConflict)
        {
            throw new InvalidOperationException("OPERATION_ID_CONFLICT");
        }

        var response = JsonSerializer.Deserialize<ProductRegistrationResponse>(result.Response!, HubJson.Options)!;
        return new HubProductRegistrationResult(
            response.Status, response.Product, response.StockMillis, response.CandidateProductId,
            result.OperationSeq, result.IsReplay);
    }

    public HubProductRetirementResult Retire(
        Guid storeId,
        Guid deviceId,
        string operationId,
        string productId,
        int baseVersion)
    {
        var payload = HubJson.Element(new { product_id = productId, base_version = baseVersion });
        var request = new OperationRequest(storeId, deviceId, operationId, "product.retire", baseVersion, payload);
        var result = _store.RetireProduct(request, productId, baseVersion);
        if (result.Status == OperationExecutionStatus.OperationIdConflict)
        {
            throw new InvalidOperationException("OPERATION_ID_CONFLICT");
        }

        var response = JsonSerializer.Deserialize<ProductUpdateResult>(result.Response!, HubJson.Options)!;
        return new HubProductRetirementResult(
            response.Status, response.Product, response.CurrentVersion, result.OperationSeq, result.IsReplay);
    }

    public HubProductPage List(
        Guid storeId,
        string? query = null,
        bool includeRetired = false,
        int limit = 50,
        string? cursor = null,
        string? groupId = null)
    {
        if (storeId == Guid.Empty || limit is < 1 or > 100 || (query?.Length ?? 0) > 128 || (cursor?.Length ?? 0) > 512)
        {
            throw new ArgumentException("A store, bounded query, and limit from 1 through 100 are required.");
        }

        var normalizedQuery = string.IsNullOrWhiteSpace(query) ? string.Empty : query.Trim();
        var normalizedGroup = string.IsNullOrWhiteSpace(groupId) ? string.Empty : groupId.Trim();
        var offset = DecodeCursor(storeId, normalizedQuery, includeRetired, cursor, normalizedGroup);
        var inventory = _store.LoadInventoryLedger();
        var views = _store.ListProducts(storeId)
            .Where(product => includeRetired || product.IsActive)
            .Where(product => normalizedGroup.Length == 0 || product.GroupId == normalizedGroup)
            .OrderBy(product => product.NormalizedName, StringComparer.Ordinal)
            .ThenBy(product => product.ProductId, StringComparer.Ordinal)
            .Select(product => new HubProductView(
                product,
                inventory.StockMillis(storeId, product.ProductId),
                _store.GetProductOperatorFields(storeId, product.ProductId)))
            .Where(view => Matches(view, normalizedQuery))
            .ToArray();
        var items = views.Skip(offset).Take(limit).ToArray();
        var nextOffset = offset + items.Length;
        var nextCursor = nextOffset < views.Length
            ? EncodeCursor(storeId, normalizedQuery, includeRetired, nextOffset, normalizedGroup)
            : null;
        return new HubProductPage(items, views.Length, nextCursor);
    }

    public HubProductPage CascadeOptions(Guid storeId, string name, int limit = 50, string? cursor = null)
    {
        if (storeId == Guid.Empty || string.IsNullOrWhiteSpace(name) || name.Length > 256 ||
            limit is < 1 or > 100 || (cursor?.Length ?? 0) > 512)
        {
            throw new ArgumentException("A store, product name, bounded limit, and valid cursor are required.");
        }

        var normalized = ProductNameNormalizer.Normalize(name);
        var offset = DecodeCursor(storeId, normalized, false, cursor, string.Empty, "CASCADE");
        var inventory = _store.LoadInventoryLedger();
        var views = _store.ListProducts(storeId)
            .Where(product => product.IsActive && product.NormalizedName == normalized)
            .OrderBy(product => product.ProductId, StringComparer.Ordinal)
            .Select(product => new HubProductView(
                product,
                inventory.StockMillis(storeId, product.ProductId),
                _store.GetProductOperatorFields(storeId, product.ProductId)))
            .ToArray();
        var items = views.Skip(offset).Take(limit).ToArray();
        var nextOffset = offset + items.Length;
        var nextCursor = nextOffset < views.Length
            ? EncodeCursor(storeId, normalized, false, nextOffset, string.Empty, "CASCADE")
            : null;
        return new HubProductPage(items, views.Length, nextCursor);
    }

    public Product? GetByTag(Guid storeId, long productTagNumber) => _store.GetProductByTag(storeId, productTagNumber);
    public long GetTagHighWatermark(Guid storeId) => _store.GetProductTagHighWatermark(storeId);
    public long GetStockMillis(Guid storeId, string productId) => _store.LoadInventoryLedger().StockMillis(storeId, productId);
    public IReadOnlyList<ProductOperatorField> GetOperatorFields(Guid storeId, string productId) =>
        _store.GetProductOperatorFields(storeId, productId);

    public string CreateProductGroup(
        Guid storeId, Guid deviceId, string operationId, string groupId, string? parentGroupId, string name)
    {
        if (string.IsNullOrWhiteSpace(groupId) || groupId.Length > 128)
        {
            throw new ArgumentException("A group ID from 1 through 128 characters is required.", nameof(groupId));
        }

        if (string.IsNullOrWhiteSpace(name) || name.Length > 256)
        {
            throw new ArgumentException("A group name from 1 through 256 characters is required.", nameof(name));
        }

        var payload = HubJson.Element(new
        {
            group_id = groupId,
            parent_group_id = parentGroupId,
            name = name.Trim()
        });
        var request = new OperationRequest(storeId, deviceId, operationId, "product.group.create", null, payload);
        var result = _store.Execute(request, transaction =>
        {
            var concrete = (SqliteHubTransaction)transaction;
            return concrete.CreateProductGroup(storeId, groupId, parentGroupId, name.Trim(), clockNow());
        });
        if (result.Status == OperationExecutionStatus.OperationIdConflict)
        {
            throw new InvalidOperationException("OPERATION_ID_CONFLICT");
        }

        return result.Response!;
    }

    public IReadOnlyList<ProductGroup> GetProductGroups(Guid storeId) => _store.GetProductGroups(storeId);

    public ProductGroup? GetProductGroup(Guid storeId, string groupId) => _store.GetProductGroup(storeId, groupId);

    public string AssignProductToGroup(
        Guid storeId, Guid deviceId, string operationId, string productId, string? groupId)
    {
        if (string.IsNullOrWhiteSpace(productId))
        {
            throw new ArgumentException("A product ID is required.", nameof(productId));
        }

        var payload = HubJson.Element(new { product_id = productId, group_id = groupId });
        var request = new OperationRequest(storeId, deviceId, operationId, "product.group.assign", null, payload);
        var result = _store.Execute(request, transaction =>
        {
            var concrete = (SqliteHubTransaction)transaction;
            return concrete.AssignProductToGroup(storeId, productId, groupId);
        });
        if (result.Status == OperationExecutionStatus.OperationIdConflict)
        {
            throw new InvalidOperationException("OPERATION_ID_CONFLICT");
        }

        return result.Response!;
    }

    private static DateTimeOffset clockNow() => DateTimeOffset.UtcNow;

    private static bool Matches(HubProductView view, string query)
    {
        if (query.Length == 0)
        {
            return true;
        }

        var normalized = ProductNameNormalizer.Normalize(query);
        if (view.Product.NormalizedName.Contains(normalized, StringComparison.Ordinal) ||
            (long.TryParse(query, NumberStyles.None, CultureInfo.InvariantCulture, out var tag) &&
             view.Product.ProductTagNumber == tag))
        {
            return true;
        }

        var texts = new string?[]
        {
            view.Product.Category, view.Product.Color, view.Product.Size, view.Product.Material,
            view.Product.Brand, view.Product.Barcode, view.Product.Specification
        }.Concat(view.OperatorFields.SelectMany(field => new[] { field.Key, field.Value }));
        return texts.Any(value => !string.IsNullOrWhiteSpace(value) &&
            ProductNameNormalizer.Normalize(value).Contains(normalized, StringComparison.Ordinal));
    }

    private static string EncodeCursor(Guid storeId, string query, bool includeRetired, int offset, string groupId, string kind = "SEARCH")
    {
        var state = new CursorState(storeId, query, includeRetired, offset, groupId, CursorSignature(storeId, query, includeRetired, offset, groupId, kind));
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(state, HubJson.Options));
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static int DecodeCursor(Guid storeId, string query, bool includeRetired, string? cursor, string groupId, string kind = "SEARCH")
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return 0;
        }

        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
            var state = JsonSerializer.Deserialize<CursorState>(Encoding.UTF8.GetString(Convert.FromBase64String(base64)), HubJson.Options)
                ?? throw new ArgumentException("CURSOR_INVALID");
            if (state.StoreId != storeId || state.Query != query || state.IncludeRetired != includeRetired ||
                !string.Equals(state.GroupId, groupId, StringComparison.Ordinal) ||
                state.Offset < 0 || state.Signature != CursorSignature(storeId, query, includeRetired, state.Offset, groupId, kind))
            {
                throw new ArgumentException("CURSOR_INVALID");
            }

            return state.Offset;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            throw new ArgumentException("CURSOR_INVALID", nameof(cursor));
        }
    }

    private static string CursorSignature(Guid storeId, string query, bool includeRetired, int offset, string groupId, string kind) =>
        CanonicalJson.Sha256Hex(HubJson.Element(new
        {
            store_id = storeId,
            query,
            include_retired = includeRetired,
            offset,
            group_id = groupId,
            kind
        }));

    private sealed record CursorState(Guid StoreId, string Query, bool IncludeRetired, int Offset, string GroupId, string Signature);
}
