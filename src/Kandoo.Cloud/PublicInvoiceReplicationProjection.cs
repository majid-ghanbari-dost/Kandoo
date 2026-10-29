using System.Text.Json;
using Kandoo.Core;
using Npgsql;
using NpgsqlTypes;

namespace Kandoo.Cloud;

/// <summary>
/// Projects only the Hub's successful, fully described native ISSUE response.
/// Legacy/low-detail log records remain replicated but cannot create a public
/// invoice with invented lines, timestamps, or content hashes.
/// </summary>
internal static class PublicInvoiceReplicationProjection
{
    public static void Apply(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid storeId,
        IEnumerable<LogEntry> replicatedEntries,
        LogEntry candidate)
    {
        if (candidate.OperationType != "invoice.issue" ||
            candidate.ResponseSnapshot.ValueKind != JsonValueKind.Object ||
            !candidate.ResponseSnapshot.TryGetProperty("status", out var status) ||
            status.ValueKind != JsonValueKind.String || status.GetString() != "SUCCEEDED" ||
            !candidate.ResponseSnapshot.TryGetProperty("response", out var response) ||
            response.ValueKind != JsonValueKind.Object ||
            !response.TryGetProperty("invoice", out var invoice) ||
            invoice.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (!invoice.TryGetProperty("status", out var lifecycle) ||
            lifecycle.ValueKind != JsonValueKind.String || lifecycle.GetString() != "ISSUED")
        {
            return;
        }

        var invoiceId = RequiredGuid(invoice, "invoice_id");
        if (!candidate.Payload.TryGetProperty("invoice_id", out var requestedInvoice) ||
            requestedInvoice.ValueKind != JsonValueKind.String || requestedInvoice.GetGuid() != invoiceId ||
            RequiredInt64(invoice, "display_number") is not > 0)
        {
            throw new InvalidOperationException("PUBLIC_INVOICE_PROJECTION_INVALID");
        }

        var displayNumber = RequiredInt64(invoice, "display_number");
        if (!invoice.TryGetProperty("issued_payload", out var issuedPayload) ||
            issuedPayload.ValueKind != JsonValueKind.Object ||
            !invoice.TryGetProperty("issued_content_sha256", out var hashElement) ||
            hashElement.ValueKind != JsonValueKind.String ||
            !invoice.TryGetProperty("issued_at", out var issuedAtElement) ||
            issuedAtElement.ValueKind != JsonValueKind.String ||
            !invoice.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("PUBLIC_INVOICE_PROJECTION_INCOMPLETE");
        }

        var issuedHash = hashElement.GetString()!;
        if (!string.Equals(CanonicalJson.Sha256Hex(issuedPayload), issuedHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("PUBLIC_INVOICE_HASH_MISMATCH");
        }

        var issuedAt = issuedAtElement.GetDateTimeOffset();
        var lines = new List<PublicInvoiceLine>();
        long payable = 0;
        foreach (var item in items.EnumerateArray())
        {
            var line = new PublicInvoiceLine
            {
                ProductName = RequiredString(item, "description"),
                QuantityMillis = RequiredInt64(item, "quantity_millis"),
                UnitPriceRials = RequiredInt64(item, "unit_price_rials"),
                LineTotalRials = RequiredInt64(item, "line_total_rials")
            };
            if (line.QuantityMillis <= 0 || line.UnitPriceRials < 0 || line.LineTotalRials < 0)
            {
                throw new InvalidOperationException("PUBLIC_INVOICE_LINE_INVALID");
            }

            payable = checked(payable + line.LineTotalRials);
            lines.Add(line);
        }

        if (lines.Count == 0)
        {
            throw new InvalidOperationException("PUBLIC_INVOICE_LINES_MISSING");
        }

        string? customerPhone = null;
        var storeName = $"Store {storeId:D}";
        var draft = replicatedEntries
            .OrderBy(entry => entry.OperationSeq)
            .FirstOrDefault(entry => IsDraftFor(entry, invoiceId));
        if (draft is not null)
        {
            if (draft.Payload.TryGetProperty("customer_phone", out var phone) && phone.ValueKind == JsonValueKind.String)
            {
                customerPhone = PhoneNormalizer.Normalize(phone.GetString()!);
            }

            if (draft.Payload.TryGetProperty("store_name", out var name) &&
                name.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(name.GetString()))
            {
                storeName = name.GetString()!.Trim();
            }
        }

        var token = PublicTokenGenerator.NewToken();
        using var command = new NpgsqlCommand("""
            INSERT INTO public_invoices
                (public_token, digital_invoice_id, invoice_id, store_id, store_name,
                 display_number, lifecycle_status, issued_at, issued_payload,
                 issued_content_sha256, payable_rials, line_items, customer_phone)
            VALUES
                (@token, @digital, @invoice, @store, @name, @display, 'ISSUED',
                 @issuedAt, @payload, @hash, @payable, @items, @phone)
            ON CONFLICT (store_id, invoice_id) DO NOTHING;
            """, connection, transaction);
        command.Parameters.AddWithValue("token", token);
        command.Parameters.AddWithValue("digital", Guid.NewGuid());
        command.Parameters.AddWithValue("invoice", invoiceId);
        command.Parameters.AddWithValue("store", storeId);
        command.Parameters.AddWithValue("name", storeName);
        command.Parameters.AddWithValue("display", displayNumber);
        command.Parameters.AddWithValue("issuedAt", issuedAt.ToUniversalTime());
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(issuedPayload, CloudJson.Options));
        command.Parameters.AddWithValue("hash", issuedHash);
        command.Parameters.AddWithValue("payable", payable);
        command.Parameters.AddWithValue("items", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(lines, CloudJson.Options));
        command.Parameters.AddWithValue("phone", NpgsqlDbType.Text, (object?)customerPhone ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static bool IsDraftFor(LogEntry entry, Guid invoiceId)
    {
        if (entry.OperationType != "invoice.draft_create" ||
            entry.ResponseSnapshot.ValueKind != JsonValueKind.Object ||
            !entry.ResponseSnapshot.TryGetProperty("status", out var status) ||
            status.ValueKind != JsonValueKind.String || status.GetString() != "SUCCEEDED" ||
            !entry.ResponseSnapshot.TryGetProperty("response", out var response) ||
            response.ValueKind != JsonValueKind.Object ||
            !response.TryGetProperty("invoice", out var invoice) ||
            invoice.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return invoice.TryGetProperty("invoice_id", out var id) &&
               id.ValueKind == JsonValueKind.String && id.GetGuid() == invoiceId;
    }

    private static Guid RequiredGuid(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetGuid() != Guid.Empty
            ? value.GetGuid()
            : throw new InvalidOperationException("PUBLIC_INVOICE_ID_INVALID");

    private static long RequiredInt64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : throw new InvalidOperationException("PUBLIC_INVOICE_NUMBER_INVALID");

    private static string RequiredString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new InvalidOperationException("PUBLIC_INVOICE_TEXT_INVALID");
}
