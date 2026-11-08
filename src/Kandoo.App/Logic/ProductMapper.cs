using System.Text.Json;

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

            rows.Add(new ProductRow(productId, name, groupId, unitPriceRials, isActive));
        }

        return rows;
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

    private static bool RequiredBoolean(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Boolean)
        {
            throw new JsonException($"Product field {name} must be a boolean.");
        }

        return value.GetBoolean();
    }
}
