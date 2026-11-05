using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kandoo.Hub;

public sealed class HubInvoiceReplicationPayloadV11(string operationId)
{
    [JsonPropertyName("operation_id")]
    public string OperationId { get; } = operationId;

    [JsonPropertyName("customer_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CustomerName { get; set; }

    [JsonPropertyName("payments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<HubReplicationPaymentV11>? Payments { get; set; }

    [JsonPropertyName("remaining_balance")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? RemainingBalance { get; set; }
}

public sealed record HubReplicationPaymentV11(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("amount")] long Amount);

/// <summary>Provisional Hub-only v1.1 batch extension; the PHP contract remains pending.</summary>
public static class HubReplicationPayloadV11
{
    private static readonly HashSet<string> PaymentTypes = new(StringComparer.Ordinal)
    {
        "card-reader", "card-to-card", "cash", "credit"
    };

    public static ReplicationBatch Apply(ReplicationBatch batch, HubReplicationOptions options)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.PayloadV11Enabled)
        {
            return batch;
        }

        batch.PayloadVersion = "1.1";
        batch.Currency = ReadRecordedCurrency(batch, options);
        var rows = new List<HubInvoiceReplicationPayloadV11>();
        foreach (var entry in batch.Entries ?? [])
        {
            var row = ReadRecordedInvoice(entry);
            if (row is not null) rows.Add(row);
        }

        batch.InvoicePayloads = rows.Count == 0 ? null : rows;
        return batch;
    }

    private static string? ReadRecordedCurrency(ReplicationBatch batch, HubReplicationOptions options)
    {
        if (options.PayloadV11StoreCurrencies is null ||
            !options.PayloadV11StoreCurrencies.TryGetValue(batch.StoreId, out var currency) ||
            currency is null)
        {
            return null;
        }

        return currency is "rial" or "toman"
            ? currency
            : throw new InvalidOperationException("RECORDED_C45_CURRENCY_INVALID");
    }

    private static HubInvoiceReplicationPayloadV11? ReadRecordedInvoice(ReplicationLogEntry entry)
    {
        if (!string.Equals(entry.OperationType, "invoice.issue", StringComparison.Ordinal) ||
            entry.Payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var row = new HubInvoiceReplicationPayloadV11(entry.OperationId);
        var present = false;
        if (TryGetValue(entry.Payload, "customer_name", out var customer))
        {
            if (customer.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(customer.GetString()))
            {
                throw new InvalidOperationException("RECORDED_C45_CUSTOMER_NAME_INVALID");
            }

            row.CustomerName = customer.GetString();
            present = true;
        }

        if (TryGetValue(entry.Payload, "payments", out var payments))
        {
            if (payments.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("RECORDED_C45_PAYMENTS_INVALID");
            }

            row.Payments = payments.EnumerateArray().Select(ReadRecordedPayment).ToList();
            present = true;
        }

        if (TryGetValue(entry.Payload, "remaining_balance", out var remaining))
        {
            if (remaining.ValueKind != JsonValueKind.Number || !remaining.TryGetInt64(out var amount))
            {
                throw new InvalidOperationException("RECORDED_C45_REMAINING_BALANCE_INVALID");
            }

            row.RemainingBalance = amount;
            present = true;
        }

        return present ? row : null;
    }

    private static HubReplicationPaymentV11 ReadRecordedPayment(JsonElement payment)
    {
        if (payment.ValueKind != JsonValueKind.Object ||
            !payment.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
            !PaymentTypes.Contains(type.GetString() ?? string.Empty) ||
            !payment.TryGetProperty("amount", out var amount) ||
            amount.ValueKind != JsonValueKind.Number || !amount.TryGetInt64(out var value))
        {
            throw new InvalidOperationException("RECORDED_C45_PAYMENT_INVALID");
        }

        return new HubReplicationPaymentV11(type.GetString()!, value);
    }

    private static bool TryGetValue(JsonElement payload, string name, out JsonElement value) =>
        payload.TryGetProperty(name, out value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
}
