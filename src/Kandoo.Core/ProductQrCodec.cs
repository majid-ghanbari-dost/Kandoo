using System.Globalization;
using System.Text.Json;

namespace Kandoo.Core;

public sealed record ProductQrLabel(Guid StoreId, long ProductTagNumber, string? CheckDigit);

public static class ProductQrCodec
{
    public static string Encode(Guid storeId, long productTagNumber, bool includeCheckDigit = false)
    {
        Validate(storeId, productTagNumber);
        var element = includeCheckDigit
            ? JsonSerializer.SerializeToElement(new
            {
                check_digit = LuhnCheckDigit(productTagNumber),
                product_tag_number = productTagNumber,
                schema_version = "0",
                store_id = storeId
            })
            : JsonSerializer.SerializeToElement(new
            {
                product_tag_number = productTagNumber,
                schema_version = "0",
                store_id = storeId
            });
        return CanonicalJson.Serialize(element);
    }

    public static byte[] EncodeUtf8(Guid storeId, long productTagNumber, bool includeCheckDigit = false) =>
        System.Text.Encoding.UTF8.GetBytes(Encode(storeId, productTagNumber, includeCheckDigit));

    public static ProductQrLabel Decode(string payload, Guid? expectedStoreId = null)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new InvalidOperationException("PRODUCT_TAG_PAYLOAD_INVALID");
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("PRODUCT_TAG_PAYLOAD_INVALID");
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!names.Add(property.Name) || property.Name is not ("schema_version" or "store_id" or "product_tag_number" or "check_digit"))
                {
                    throw new InvalidOperationException("PRODUCT_TAG_PAYLOAD_INVALID");
                }
            }

            if (document.RootElement.GetProperty("schema_version").ValueKind != JsonValueKind.String ||
                document.RootElement.GetProperty("schema_version").GetString() != "0" ||
                document.RootElement.GetProperty("store_id").ValueKind != JsonValueKind.String ||
                !document.RootElement.GetProperty("store_id").TryGetGuid(out var storeId) ||
                document.RootElement.GetProperty("product_tag_number").ValueKind != JsonValueKind.Number ||
                !document.RootElement.GetProperty("product_tag_number").TryGetInt64(out var tag))
            {
                throw new InvalidOperationException("PRODUCT_TAG_PAYLOAD_INVALID");
            }

            Validate(storeId, tag);
            if (expectedStoreId is Guid expected && expected != storeId)
            {
                throw new InvalidOperationException("PRODUCT_TAG_STORE_MISMATCH");
            }

            string? checkDigit = null;
            if (document.RootElement.TryGetProperty("check_digit", out var check))
            {
                if (check.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidOperationException("CHECK_DIGIT_INVALID");
                }

                checkDigit = check.GetString();
                if (!string.Equals(checkDigit, LuhnCheckDigit(tag), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("CHECK_DIGIT_INVALID");
                }
            }

            return new ProductQrLabel(storeId, tag, checkDigit);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or FormatException or ArgumentException)
        {
            throw new InvalidOperationException("PRODUCT_TAG_PAYLOAD_INVALID");
        }
    }

    public static string FormatTag(long productTagNumber, bool includeCheckDigit = false)
    {
        if (productTagNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(productTagNumber));
        }

        return includeCheckDigit
            ? $"{productTagNumber.ToString(CultureInfo.InvariantCulture)}-{LuhnCheckDigit(productTagNumber)}"
            : productTagNumber.ToString(CultureInfo.InvariantCulture);
    }

    public static string LuhnCheckDigit(long productTagNumber)
    {
        if (productTagNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(productTagNumber));
        }

        var digits = productTagNumber.ToString(CultureInfo.InvariantCulture);
        var sum = 0;
        for (var index = 0; index < digits.Length; index++)
        {
            var value = digits[digits.Length - 1 - index] - '0';
            if (index % 2 == 0)
            {
                value *= 2;
                if (value > 9)
                {
                    value -= 9;
                }
            }

            sum += value;
        }

        return ((10 - sum % 10) % 10).ToString(CultureInfo.InvariantCulture);
    }

    private static void Validate(Guid storeId, long productTagNumber)
    {
        if (storeId == Guid.Empty || productTagNumber <= 0)
        {
            throw new ArgumentException("A store and positive product tag are required.");
        }
    }
}
