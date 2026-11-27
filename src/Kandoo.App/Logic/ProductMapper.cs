using System.Text.Json;
using Kandoo.Core;

namespace Kandoo.App;

public static class ProductMapper
{
    public static IReadOnlyList<ProductRow> Map(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Hub product response must contain an items array.");
        }

        var rows = new List<ProductRow>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Product entries must be objects.");
            }

            var productId = RequiredString(item, "product_id");
            var name = RequiredString(item, "name");
            var groupId = OptionalString(item, "group_id");
            var unitPriceRials = RequiredInt64(item, "unit_price_rials");
            var isActive = RequiredBoolean(item, "is_active");
            var productTagNumber = OptionalPositiveInt64(item, "product_tag_number");
            var stockMillis = OptionalSignedInt64(item, "stock_millis");
            var purchasePriceRials = OptionalNonNegativeInt64(item, "purchase_price_rials");
            var version = OptionalPositiveInt64(item, "version") ?? 1L;
            var type = OptionalString(item, "type") ?? OptionalString(item, "category");
            var color = OptionalString(item, "color");
            var specification = OptionalString(item, "specification");
            var barcode = OptionalString(item, "barcode");
            var operatorFields = ReadOperatorFields(item);
            var stockUnconfirmed = OptionalBoolean(item, "stock_unconfirmed") == true ||
                                   IsStockUnconfirmed(specification, operatorFields);

            rows.Add(new ProductRow(
                productId,
                name,
                groupId,
                unitPriceRials,
                isActive,
                productTagNumber,
                stockMillis,
                purchasePriceRials,
                version,
                type,
                color,
                operatorFields,
                stockUnconfirmed,
                specification,
                barcode));
        }

        return rows;
    }

    private static IReadOnlyList<ProductOperatorField> ReadOperatorFields(JsonElement item)
    {
        if (!item.TryGetProperty("operator_fields", out var element) ||
            element.ValueKind == JsonValueKind.Null)
        {
            return Array.Empty<ProductOperatorField>();
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Product field operator_fields must be an array.");
        }

        var fields = new List<ProductOperatorField>();
        foreach (var entry in element.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var key = OptionalString(entry, "key");
            if (key is null)
            {
                continue;
            }

            var value = entry.TryGetProperty("value", out var valueElement) &&
                        valueElement.ValueKind == JsonValueKind.String
                ? valueElement.GetString() ?? string.Empty
                : string.Empty;
            fields.Add(new ProductOperatorField(key, value));
        }

        return fields;
    }

    private static bool IsStockUnconfirmed(
        string? specification,
        IReadOnlyList<ProductOperatorField> operatorFields)
    {
        if (!string.IsNullOrWhiteSpace(specification) &&
            specification.Contains("UNCONFIRMED_STOCK", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var field in operatorFields)
        {
            if (string.Equals(field.Key, "stock_status", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(field.Value, "unconfirmed", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        var value = OptionalString(parent, name);
        return value ?? throw new JsonException($"Product field {name} must be a non-empty string.");
    }

    private static string? OptionalString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"Product field {name} must be a string.");
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static long RequiredInt64(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var result) || result < 0)
        {
            throw new JsonException($"Product field {name} must be a non-negative integer.");
        }

        return result;
    }

    private static long? OptionalPositiveInt64(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var result) || result <= 0)
        {
            throw new JsonException($"Product field {name} must be a positive integer when present.");
        }

        return result;
    }

    private static long? OptionalNonNegativeInt64(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var result) || result < 0)
        {
            throw new JsonException($"Product field {name} must be a non-negative integer when present.");
        }

        return result;
    }

    private static long? OptionalSignedInt64(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var result))
        {
            throw new JsonException($"Product field {name} must be an integer when present.");
        }

        return result;
    }

    private static bool? OptionalBoolean(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
        {
            throw new JsonException($"Product field {name} must be a boolean when present.");
        }

        return value.GetBoolean();
    }

    private static bool RequiredBoolean(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False))
        {
            throw new JsonException($"Product field {name} must be a boolean.");
        }

        return value.GetBoolean();
    }
}
