using System.Globalization;
using System.Text.Json;
using Kandoo.Core;

namespace Kandoo.App;

public static class ReviewQueueMapper
{
    public const string MissingDateMarker = "تاریخ ثبت نشده";

    public static IReadOnlyList<ReviewQueueRow> Map(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Hub review response must contain an items array.");
        }

        var rows = new List<ReviewQueueRow>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Review queue entries must be objects.");
            }

            var reviewId = RequiredString(item, "review_id");
            var queueStatus = RequiredString(item, "queue_status");
            if (queueStatus is not ("REVIEW" or "ISSUED"))
            {
                throw new JsonException("Only the certified REVIEW/ISSUED queue states are supported.");
            }

            var source = RequiredObject(item, "source");
            var header = RequiredObject(source, "header");
            var summary = RequiredObject(source, "summary");
            var invoiceNumber = OptionalString(header, "invoice_number")
                ?? OptionalString(source, "invoice_id")
                ?? reviewId;
            if (!summary.TryGetProperty("payable", out var payableElement) ||
                !payableElement.TryGetInt64(out var payable) || payable < 0)
            {
                throw new JsonException("Review summary.payable must be a non-negative integer Rial amount.");
            }

            var captured = OptionalString(header, "captured_at");
            var jalaliDate = MissingDateMarker;
            if (captured is not null)
            {
                if (!DateTimeOffset.TryParse(
                        captured, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var capturedAt))
                {
                    throw new JsonException("Review header.captured_at is not a valid timestamp.");
                }

                jalaliDate = JalaliDateConverter.Format(capturedAt);
            }

            rows.Add(new ReviewQueueRow(reviewId, invoiceNumber, jalaliDate, queueStatus, payable));
        }

        return rows;
    }

    private static JsonElement RequiredObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"Review field {name} must be an object.");
        }

        return value;
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        var value = OptionalString(parent, name);
        return value ?? throw new JsonException($"Review field {name} must be a non-empty string.");
    }

    private static string? OptionalString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"Review field {name} must be a string.");
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
