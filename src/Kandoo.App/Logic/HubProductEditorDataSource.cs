using System.Globalization;
using System.Net;
using System.Text.Json;
using Kandoo.Core;

namespace Kandoo.App;

/// <summary>Data source for product create, edit, stock-event, and group operations against the Hub.</summary>
public sealed class HubProductEditorDataSource
{
    private readonly HttpClient _client;
    private readonly HubConnectionSettings _settings;
    private readonly string _baseUrl;

    public HubProductEditorDataSource(HttpClient client, HubConnectionSettings settings)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _baseUrl = HubApiUtilities.ValidatedBaseUrl(settings);
        if (string.IsNullOrWhiteSpace(settings.SessionToken))
        {
            throw new ArgumentException("A Hub session token is required.", nameof(settings));
        }
    }

    public async Task<string> CreateProductAsync(
        string name,
        long unitPriceRials,
        string? groupId = null,
        long? purchasePriceRials = null,
        long initialStockMillis = 0,
        CancellationToken cancellationToken = default)
    {
        var result = await CreateProductDetailedAsync(
            new ProductEditorCreateRequest(
                Name: name,
                UnitPriceRials: unitPriceRials,
                InitialStockMillis: initialStockMillis,
                GroupId: groupId,
                PurchasePriceRials: purchasePriceRials),
            cancellationToken);
        return result.ProductId;
    }

    public async Task<ProductEditorCreateResult> CreateProductDetailedAsync(
        ProductEditorCreateRequest input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            throw new ArgumentException("Product name is required.", nameof(input));
        }

        if (input.UnitPriceRials < 0)
        {
            throw new ArgumentException("Unit price must be non-negative.", nameof(input));
        }

        if (input.InitialStockMillis < 0)
        {
            throw new ArgumentException("Initial stock must be non-negative.", nameof(input));
        }

        if (input.ProductTagNumber is <= 0)
        {
            throw new ArgumentException("Product tag number must be positive when supplied.", nameof(input));
        }

        if (input.PurchasePriceRials is < 0)
        {
            throw new ArgumentException("Purchase price must be non-negative when supplied.", nameof(input));
        }

        var validatedFields = ProductRegistrationInput.ValidateOperatorFields(input.OperatorFields);
        var operationId = string.IsNullOrWhiteSpace(input.OperationId)
            ? $"prod-create-{Guid.NewGuid():N}"
            : input.OperationId.Trim();

        var body = new Dictionary<string, object?>
        {
            ["operation_id"] = operationId,
            ["name"] = input.Name.Trim(),
            ["unit_price_rials"] = input.UnitPriceRials,
            ["initial_stock_millis"] = input.InitialStockMillis
        };

        if (!string.IsNullOrWhiteSpace(input.Type))
            body["type"] = input.Type.Trim();
        if (!string.IsNullOrWhiteSpace(input.Color))
            body["color"] = input.Color.Trim();
        if (input.PurchasePriceRials is long purchasePrice)
            body["purchase_price_rials"] = purchasePrice;
        if (input.ProductTagNumber is long tagNumber)
            body["product_tag_number"] = tagNumber;
        if (!string.IsNullOrWhiteSpace(input.GroupId))
            body["group_id"] = input.GroupId.Trim();
        if (!string.IsNullOrWhiteSpace(input.Barcode))
            body["barcode"] = input.Barcode.Trim();
        if (!string.IsNullOrWhiteSpace(input.Specification))
            body["specification"] = input.Specification.Trim();
        if (!string.IsNullOrWhiteSpace(input.AcknowledgedCandidateId))
            body["acknowledged_candidate_id"] = input.AcknowledgedCandidateId.Trim();
        if (validatedFields.Count > 0)
        {
            body["operator_fields"] = validatedFields
                .Select(f => new Dictionary<string, string>
                {
                    ["key"] = f.Key,
                    ["value"] = f.Value
                })
                .ToArray();
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{_baseUrl}/v1/stores/{_settings.StoreId:D}/products");
        HubApiUtilities.Authorize(request, _settings);
        request.Content = HubApiUtilities.JsonBody(body);
        using var response = await _client.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var conflictReason = TryFormatCreateConflict(response.StatusCode, responseText);
            throw new InvalidOperationException(conflictReason ??
                $"Hub product creation failed with HTTP {(int)response.StatusCode}: {responseText}.");
        }

        using var document = JsonDocument.Parse(responseText);
        return ParseCreateResponse(document.RootElement);
    }

    public async Task UpdateProductAsync(
        string productId,
        string? name = null,
        long? unitPriceRials = null,
        string? groupId = null,
        long baseVersion = 1,
        CancellationToken cancellationToken = default)
    {
        await UpdateProductDetailedAsync(
            new ProductEditorUpdateRequest(
                ProductId: productId,
                BaseVersion: baseVersion,
                Name: name,
                UnitPriceRials: unitPriceRials,
                GroupId: groupId),
            cancellationToken);
    }

    public async Task<ProductEditorUpdateResult> UpdateProductDetailedAsync(
        ProductEditorUpdateRequest input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.ProductId))
        {
            throw new ArgumentException("Product ID is required.", nameof(input));
        }

        if (input.BaseVersion <= 0)
        {
            throw new ArgumentException("A positive base_version is required.", nameof(input));
        }

        var productId = input.ProductId.Trim();
        var operationId = string.IsNullOrWhiteSpace(input.OperationId)
            ? $"prod-update-{Guid.NewGuid():N}"
            : input.OperationId.Trim();

        var payload = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(input.Name))
            payload["name"] = input.Name.Trim();
        if (input.IncludeType)
            payload["type"] = string.IsNullOrWhiteSpace(input.Type) ? null : input.Type.Trim();
        if (input.IncludeColor)
            payload["color"] = string.IsNullOrWhiteSpace(input.Color) ? null : input.Color.Trim();
        if (input.UnitPriceRials is long unitPrice)
            payload["unit_price_rials"] = unitPrice;
        if (input.IncludePurchasePrice)
            payload["purchase_price_rials"] = input.PurchasePriceRials;
        if (input.StockMillis is long stockMillis)
            payload["stock_millis"] = stockMillis;
        if (input.IncludeSpecification)
            payload["specification"] = string.IsNullOrWhiteSpace(input.Specification) ? null : input.Specification.Trim();
        if (input.OperatorFields is not null)
        {
            var validated = ProductRegistrationInput.ValidateOperatorFields(input.OperatorFields);
            payload["operator_fields"] = validated
                .Select(f => new Dictionary<string, string>
                {
                    ["key"] = f.Key,
                    ["value"] = f.Value
                })
                .ToArray();
        }

        var envelope = new Dictionary<string, object?>
        {
            ["operation_id"] = operationId,
            ["base_version"] = input.BaseVersion,
            ["payload"] = payload
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"{_baseUrl}/v1/stores/{_settings.StoreId:D}/products/{Uri.EscapeDataString(productId)}");
        HubApiUtilities.Authorize(request, _settings);
        request.Content = HubApiUtilities.JsonBody(envelope);
        using var response = await _client.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Hub product update failed with HTTP {(int)response.StatusCode}: {responseText}.");
        }

        using var document = JsonDocument.Parse(responseText);
        var result = ParseUpdateResponse(document.RootElement, productId, input.BaseVersion);

        if (input.GroupId is not null || input.UnassignGroup)
        {
            await AssignGroupAsync(
                productId,
                input.UnassignGroup ? null : input.GroupId,
                $"{operationId}-group",
                cancellationToken);
        }

        return result;
    }

    public async Task AssignGroupAsync(
        string productId,
        string? groupId,
        string? operationId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productId))
        {
            throw new ArgumentException("Product ID is required.", nameof(productId));
        }

        var body = new Dictionary<string, object?>
        {
            ["operation_id"] = string.IsNullOrWhiteSpace(operationId)
                ? $"prod-group-{Guid.NewGuid():N}"
                : operationId.Trim(),
            ["group_id"] = string.IsNullOrWhiteSpace(groupId) ? null : groupId.Trim()
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_baseUrl}/v1/stores/{_settings.StoreId:D}/products/{Uri.EscapeDataString(productId.Trim())}/group");
        HubApiUtilities.Authorize(request, _settings);
        request.Content = HubApiUtilities.JsonBody(body);
        using var response = await _client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var bodyText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Hub product group assignment failed with HTTP {(int)response.StatusCode}: {bodyText}.");
        }
    }

    public async Task<IReadOnlyList<GroupOption>> LoadGroupOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_baseUrl}/v1/stores/{_settings.StoreId:D}/product-groups");
        HubApiUtilities.Authorize(request, _settings);
        using var response = await _client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Hub group options request failed with HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ParseGroupOptions(document.RootElement);
    }

    public static IReadOnlyList<GroupOption> ParseGroupOptions(JsonElement root)
    {
        var raw = new List<(string Id, string? ParentId, string Name, bool IsActive)>();
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("groups", out var groupsArray) &&
            groupsArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var g in groupsArray.EnumerateArray())
            {
                if (g.ValueKind != JsonValueKind.Object)
                    continue;
                var id = OptionalString(g, "group_id");
                var name = OptionalString(g, "name");
                if (id is null || name is null)
                    continue;
                var parentId = OptionalString(g, "parent_group_id");
                var isActive = !g.TryGetProperty("is_active", out var activeProp) ||
                               activeProp.ValueKind != JsonValueKind.False;
                if (isActive)
                {
                    raw.Add((id, parentId, name, isActive));
                }
            }
        }

        var byId = raw.ToDictionary(item => item.Id, item => item.Name, StringComparer.Ordinal);
        return raw.Select(item =>
        {
            var display = item.ParentId is not null && byId.TryGetValue(item.ParentId, out var parentName)
                ? $"{parentName} / {item.Name}"
                : item.Name;
            return new GroupOption(item.Id, display, item.ParentId);
        }).ToArray();
    }

    public static ProductEditorCreateResult ParseCreateResponse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Hub product response must be a JSON object.");
        }

        string? productId = null;
        long? tagNumber = null;
        long version = 1;

        if (root.TryGetProperty("product", out var product) && product.ValueKind == JsonValueKind.Object)
        {
            productId = OptionalString(product, "product_id");
            tagNumber = OptionalInt64(product, "product_tag_number");
            version = OptionalInt64(product, "version") ?? 1;
        }

        productId ??= OptionalString(root, "product_id");
        if (string.IsNullOrWhiteSpace(productId))
        {
            throw new JsonException("Hub product response missing product.product_id.");
        }

        var stockMillis = OptionalInt64(root, "stock_millis") ?? 0;
        var status = OptionalString(root, "status") ?? "CREATED";
        return new ProductEditorCreateResult(productId, tagNumber, stockMillis, version, status);
    }

    public static ProductEditorUpdateResult ParseUpdateResponse(
        JsonElement root,
        string fallbackProductId,
        long fallbackVersion)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Hub product update response must be a JSON object.");
        }

        var status = OptionalString(root, "status") ?? "SUCCEEDED";
        var productId = fallbackProductId;
        var version = fallbackVersion + 1;
        long? stockMillis = null;

        if (root.TryGetProperty("response", out var response) && response.ValueKind == JsonValueKind.Object)
        {
            version = OptionalInt64(response, "version") ?? version;
            stockMillis = OptionalInt64(response, "stock_millis");
            if (response.TryGetProperty("product", out var product) && product.ValueKind == JsonValueKind.Object)
            {
                productId = OptionalString(product, "product_id") ?? productId;
                version = OptionalInt64(product, "version") ?? version;
            }
        }

        return new ProductEditorUpdateResult(productId, version, stockMillis, status);
    }

    public static bool TryValidateCreateForm(
        string? nameText,
        string? priceText,
        string? stockText,
        string? purchasePriceText,
        string? tagText,
        out long unitPriceRials,
        out long initialStockMillis,
        out long? purchasePriceRials,
        out long? productTagNumber,
        out string persianError)
    {
        unitPriceRials = 0;
        initialStockMillis = 0;
        purchasePriceRials = null;
        productTagNumber = null;
        persianError = string.Empty;

        if (string.IsNullOrWhiteSpace(nameText))
        {
            persianError = "نام کالا الزامی است. اگر نام، قیمت یا تعداد خالی باشد، کالا ساخته نمی‌شود.";
            return false;
        }

        var normalizedPrice = HubConnectionResolver.NormalizeAsciiDigits(priceText);
        if (string.IsNullOrWhiteSpace(normalizedPrice) ||
            !long.TryParse(normalizedPrice, NumberStyles.Integer, CultureInfo.InvariantCulture, out unitPriceRials) ||
            unitPriceRials < 0)
        {
            persianError = "قیمت فروش الزامی است و باید عدد صحیح غیرمنفی باشد.";
            return false;
        }

        var normalizedStock = HubConnectionResolver.NormalizeAsciiDigits(stockText);
        if (string.IsNullOrWhiteSpace(normalizedStock) ||
            !decimal.TryParse(normalizedStock, NumberStyles.Number, CultureInfo.InvariantCulture, out var stockUnits) ||
            stockUnits < 0)
        {
            persianError = "تعداد در انبار الزامی است و باید عدد غیرمنفی باشد.";
            return false;
        }

        var scaledStock = decimal.Round(stockUnits * 1000m, 0, MidpointRounding.AwayFromZero);
        if (scaledStock > long.MaxValue)
        {
            persianError = "تعداد در انبار خارج از محدوده مجاز است.";
            return false;
        }

        initialStockMillis = (long)scaledStock;

        var normalizedPurchase = HubConnectionResolver.NormalizeAsciiDigits(purchasePriceText);
        if (!string.IsNullOrWhiteSpace(normalizedPurchase))
        {
            if (!long.TryParse(normalizedPurchase, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPurchase) ||
                parsedPurchase < 0)
            {
                persianError = "قیمت خرید معتبر نیست؛ باید عدد صحیح غیرمنفی باشد یا خالی بماند.";
                return false;
            }

            purchasePriceRials = parsedPurchase;
        }

        var normalizedTag = HubConnectionResolver.NormalizeAsciiDigits(tagText);
        if (!string.IsNullOrWhiteSpace(normalizedTag))
        {
            if (!long.TryParse(normalizedTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTag) ||
                parsedTag <= 0)
            {
                persianError = "تگ رقمی باید عدد صحیح مثبت باشد یا خالی بماند تا کندو خودکار شماره بدهد.";
                return false;
            }

            productTagNumber = parsedTag;
        }

        return true;
    }

    public static bool TryValidateStockAdjustment(
        long currentStockMillis,
        string? deltaText,
        string? reasonText,
        out long nextStockMillis,
        out long deltaMillis,
        out string persianError)
    {
        nextStockMillis = currentStockMillis;
        deltaMillis = 0;
        persianError = string.Empty;

        var normalizedDelta = HubConnectionResolver.NormalizeAsciiDigits(deltaText);
        if (string.IsNullOrWhiteSpace(normalizedDelta) ||
            !decimal.TryParse(normalizedDelta, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var deltaUnits) ||
            deltaUnits == 0)
        {
            persianError = "تعداد تغییر موجودی (مثبت یا منفی غیرصفر) را وارد کنید.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(reasonText))
        {
            persianError = "دلیل تغییر موجودی الزامی است (مثلاً شمارش انبار یا ضایعات).";
            return false;
        }

        var scaledDelta = decimal.Round(deltaUnits * 1000m, 0, MidpointRounding.AwayFromZero);
        deltaMillis = (long)scaledDelta;
        var candidateNext = currentStockMillis + deltaMillis;
        if (candidateNext < 0)
        {
            persianError = "موجودی انبار پس از کسر نمی‌تواند منفی شود.";
            return false;
        }

        nextStockMillis = candidateNext;
        return true;
    }

    private static string? TryFormatCreateConflict(HttpStatusCode statusCode, string responseText)
    {
        if (statusCode != HttpStatusCode.Conflict || string.IsNullOrWhiteSpace(responseText))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            var status = OptionalString(root, "status") ?? OptionalString(root, "code");
            var candidate = OptionalString(root, "candidate_product_id");
            return status switch
            {
                "DUPLICATE_PRODUCT_TAG" => "این تگ رقمی قبلاً برای کالای دیگری در فروشگاه ثبت شده است.",
                "EXACT_DUPLICATE" => "کالایی با همین نام دقیقاً در فروشگاه وجود دارد.",
                "NEAR_DUPLICATE_WARNING" => candidate is null
                    ? "کالای مشابهی در فروشگاه وجود دارد."
                    : $"کالای مشابهی در فروشگاه وجود دارد (شناسه: {candidate}).",
                "DUPLICATE_PRODUCT_ID" => "شناسه کالا تکراری است.",
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? OptionalString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            return null;
        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static long? OptionalInt64(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : null;
    }
}

/// <summary>Product group option for the editor dropdown.</summary>
public sealed record GroupOption(string GroupId, string Name, string? ParentGroupId = null);

/// <summary>Legacy product creation input preserved for compatibility.</summary>
public sealed record ProductEditorInput(
    string Name,
    long UnitPriceRials,
    string? GroupId = null,
    long? PurchasePriceRials = null);

/// <summary>Full product registration input matching <c>Product.dc.html</c> and Hub contract.</summary>
public sealed record ProductEditorCreateRequest(
    string Name,
    long UnitPriceRials,
    long InitialStockMillis,
    string? Type = null,
    string? Color = null,
    long? PurchasePriceRials = null,
    long? ProductTagNumber = null,
    IReadOnlyList<ProductOperatorField>? OperatorFields = null,
    string? GroupId = null,
    string? Barcode = null,
    string? Specification = null,
    string? AcknowledgedCandidateId = null,
    string? OperationId = null);

public sealed record ProductEditorCreateResult(
    string ProductId,
    long? ProductTagNumber,
    long StockMillis,
    long Version,
    string Status);

public sealed record ProductEditorUpdateRequest(
    string ProductId,
    long BaseVersion,
    string? Name = null,
    string? Type = null,
    bool IncludeType = false,
    string? Color = null,
    bool IncludeColor = false,
    long? UnitPriceRials = null,
    long? PurchasePriceRials = null,
    bool IncludePurchasePrice = false,
    long? StockMillis = null,
    IReadOnlyList<ProductOperatorField>? OperatorFields = null,
    string? Specification = null,
    bool IncludeSpecification = false,
    string? GroupId = null,
    bool UnassignGroup = false,
    string? OperationId = null);

public sealed record ProductEditorUpdateResult(
    string ProductId,
    long Version,
    long? StockMillis,
    string Status);
