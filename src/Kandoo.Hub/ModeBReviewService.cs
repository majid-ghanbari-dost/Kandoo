using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kandoo.Core;

namespace Kandoo.Hub;

/// <summary>
/// Fail-closed Mode-B queue. Captures remain REVIEW data until an operator
/// resolves every cell, explicitly supplies the phone, and the native Hub
/// issuance path succeeds. No customer, sale, or inventory is created on accept.
/// </summary>
public sealed class ModeBReviewService
{
    private readonly object _gate = new();
    private readonly SqliteHubStore _store;
    private readonly HubOperationService _operations;
    private readonly IClock _clock;

    public ModeBReviewService(SqliteHubStore store, HubOperationService operations, IClock clock)
    {
        _store = store;
        _operations = operations;
        _clock = clock;
    }

    public ModeBReviewActionResult Accept(Guid nativeStoreId, string canonicalJson)
    {
        if (nativeStoreId == Guid.Empty || string.IsNullOrWhiteSpace(canonicalJson))
        {
            return new ModeBReviewActionResult("VALIDATION_ERROR");
        }

        ReviewQueueEntity consumed;
        try
        {
            consumed = CanonicalInvoiceConsumer.ConsumeForReview(canonicalJson);
        }
        catch (CanonicalInvoiceException exception)
        {
            return new ModeBReviewActionResult(exception.Code);
        }

        if (consumed.Source.GetProperty("mode").GetString() != "B")
        {
            return new ModeBReviewActionResult("CAPTURE_MODE_INVALID");
        }

        if (consumed.SourceState is not ("DRAFT" or "EXTRACTED" or "VALIDATED"))
        {
            return new ModeBReviewActionResult("CAPTURE_ALREADY_ISSUED");
        }

        var reviewId = CanonicalJson.Sha256Hex(consumed.Source);
        lock (_gate)
        {
            var existing = _store.GetModeBReview(reviewId);
            if (existing is not null)
            {
                return existing.NativeStoreId == nativeStoreId
                    ? new ModeBReviewActionResult(existing.QueueStatus, existing, true, existing.IssuedInvoiceId)
                    : new ModeBReviewActionResult("REVIEW_STORE_CONFLICT");
            }

            var review = new ModeBReviewRecord(reviewId, nativeStoreId, "REVIEW", consumed.Source, null, null);
            _store.SaveModeBReview(review);
            return new ModeBReviewActionResult("REVIEW", review);
        }
    }

    public IReadOnlyList<ModeBReviewRecord> List(Guid nativeStoreId) => _store.ListModeBReviews(nativeStoreId);

    public IReadOnlyList<ModeBAttentionCell> ListAttention(string reviewId)
    {
        var review = RequireReview(reviewId);
        var cells = review.Source.GetProperty("attention_cells").EnumerateArray()
            .Select(cell => new ModeBAttentionCell(
                cell.GetProperty("field").GetString()!,
                cell.GetProperty("raw_value").GetString()!,
                cell.GetProperty("reason").GetString()!))
            .ToList();
        if (review.OperatorPhone is null)
        {
            cells.Add(new ModeBAttentionCell(
                "customer.phone",
                review.Source.GetProperty("customer").GetProperty("phone").GetString()!,
                "OPERATOR_PHONE_REQUIRED"));
        }

        if (!TryComputePayable(review.Source, out var expected))
        {
            cells.Add(new ModeBAttentionCell(
                "summary.payable",
                review.Source.GetProperty("summary").GetProperty("payable").GetRawText(),
                $"PAYABLE_SUM_MISMATCH: expected {expected.ToString(CultureInfo.InvariantCulture)}"));
        }

        return cells;
    }

    public ModeBReviewActionResult Correct(string reviewId, string fieldPath, JsonElement value, string operatorId)
    {
        if (string.IsNullOrWhiteSpace(operatorId))
        {
            return new ModeBReviewActionResult("OPERATOR_REQUIRED");
        }

        lock (_gate)
        {
            var review = RequireReview(reviewId);
            if (review.QueueStatus != "REVIEW")
            {
                return new ModeBReviewActionResult("REVIEW_ALREADY_ISSUED", review);
            }

            var root = JsonNode.Parse(review.Source.GetRawText())!.AsObject();
            string? oldValue;
            try
            {
                oldValue = ApplyCorrection(root, fieldPath, value);
                RemoveAttention(root, fieldPath);
                CanonicalInvoiceConsumer.ConsumeForReview(root.ToJsonString());
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or FormatException)
            {
                return new ModeBReviewActionResult("CORRECTION_INVALID", review);
            }

            var corrected = review with { Source = Element(root) };
            var audit = new ModeBReviewCorrection(
                reviewId, fieldPath, oldValue, value.GetRawText(), operatorId, _clock.UtcNow);
            _store.SaveModeBReview(corrected, audit);
            return new ModeBReviewActionResult("REVIEW", corrected);
        }
    }

    public ModeBReviewActionResult SetCustomerPhone(string reviewId, string phone, string operatorId)
    {
        if (string.IsNullOrWhiteSpace(operatorId))
        {
            return new ModeBReviewActionResult("OPERATOR_REQUIRED");
        }

        string normalized;
        try
        {
            normalized = PhoneNormalizer.Normalize(phone);
        }
        catch (ArgumentException)
        {
            return new ModeBReviewActionResult("CUSTOMER_PHONE_INVALID");
        }

        lock (_gate)
        {
            var review = RequireReview(reviewId);
            if (review.QueueStatus != "REVIEW")
            {
                return new ModeBReviewActionResult("REVIEW_ALREADY_ISSUED", review);
            }

            var root = JsonNode.Parse(review.Source.GetRawText())!.AsObject();
            var oldValue = root["customer"]!["phone"]!.ToJsonString();
            root["customer"]!["phone"] = normalized;
            root["provenance"]!["fields"]!["customer"]!["phone"] = "operator_confirmed";
            RemoveAttention(root, "customer.phone");
            var updated = review with { Source = Element(root), OperatorPhone = normalized };
            var audit = new ModeBReviewCorrection(
                reviewId, "customer.phone", oldValue, JsonSerializer.Serialize(normalized), operatorId, _clock.UtcNow);
            _store.SaveModeBReview(updated, audit);
            return new ModeBReviewActionResult("REVIEW", updated);
        }
    }

    public ModeBReviewActionResult Issue(string reviewId, HubSession session)
    {
        lock (_gate)
        {
            var review = RequireReview(reviewId);
            if (review.IssuedInvoiceId is not null || review.QueueStatus == "ISSUED")
            {
                return new ModeBReviewActionResult("ISSUED", review, true, review.IssuedInvoiceId);
            }

            if (session.StoreId != review.NativeStoreId)
            {
                return new ModeBReviewActionResult("STORE_ACCESS_DENIED", review);
            }

            if (review.Source.GetProperty("attention_cells").GetArrayLength() != 0)
            {
                return new ModeBReviewActionResult("UNRESOLVED_ATTENTION_CELLS", review);
            }

            if (review.OperatorPhone is null)
            {
                return new ModeBReviewActionResult("OPERATOR_PHONE_REQUIRED", review);
            }

            if (!TryComputePayable(review.Source, out _))
            {
                return new ModeBReviewActionResult("PAYABLE_SUM_MISMATCH", review);
            }

            var source = review.Source;
            var draft = Submit(
                session,
                $"modeb:{reviewId}:draft",
                "invoice.draft_create",
                new
                {
                    customer_phone = review.OperatorPhone,
                    external_invoice_number = source.GetProperty("invoice_id").GetString(),
                    capture_review_id = reviewId
                });
            if (draft.Body.Status != "SUCCEEDED")
            {
                return new ModeBReviewActionResult(draft.Body.Status, review);
            }

            var invoiceId = draft.Body.Response.GetProperty("invoice").GetProperty("invoice_id").GetGuid();
            var items = source.GetProperty("line_items").EnumerateArray().ToArray();
            for (var index = 0; index < items.Length; index++)
            {
                var item = items[index];
                var added = Submit(
                    session,
                    $"modeb:{reviewId}:item:{index}",
                    "invoice.item_add",
                    new
                    {
                        invoice_id = invoiceId,
                        description = item.GetProperty("product_name").GetString(),
                        quantity_millis = item.GetProperty("quantity_millis").GetInt64(),
                        unit_price_rials = item.GetProperty("unit_price").GetInt64(),
                        line_total_rials = item.GetProperty("line_total").GetInt64()
                    });
                if (added.Body.Status != "SUCCEEDED")
                {
                    return new ModeBReviewActionResult(added.Body.Status, review);
                }
            }

            var issued = Submit(session, $"modeb:{reviewId}:issue", "invoice.issue", new { invoice_id = invoiceId });
            if (issued.Body.Status != "SUCCEEDED")
            {
                return new ModeBReviewActionResult(issued.Body.Status, review);
            }

            var displayNumber = issued.Body.Response.GetProperty("invoice").GetProperty("display_number").GetInt64();
            var completed = review with { QueueStatus = "ISSUED", IssuedInvoiceId = invoiceId };
            _store.SaveModeBReview(completed);
            return new ModeBReviewActionResult("ISSUED", completed, false, invoiceId, displayNumber);
        }
    }

    public static bool TryComputePayable(JsonElement source, out long expected)
    {
        expected = 0;
        try
        {
            foreach (var item in source.GetProperty("line_items").EnumerateArray())
            {
                expected = checked(expected + item.GetProperty("line_total").GetInt64());
            }

            foreach (var extra in source.GetProperty("summary").GetProperty("extra_amounts").EnumerateObject())
            {
                // ACC-0: only declared extra_amounts are known here. No tax,
                // discount, or rounding accounting rule is inferred.
                expected = checked(expected + extra.Value.GetInt64());
            }

            return expected == source.GetProperty("summary").GetProperty("payable").GetInt64();
        }
        catch (Exception exception) when (exception is InvalidOperationException or OverflowException or FormatException)
        {
            return false;
        }
    }

    private HubOperationResult Submit(HubSession session, string operationId, string operationType, object payload)
    {
        var element = HubJson.Element(payload);
        var operation = new OperationRequest(session.StoreId, session.DeviceId, operationId, operationType, null, element);
        var request = new HubOperationRequest(
            "0", session.SessionId, session.StoreId, session.DeviceId, operationId, operationType,
            null, element, operation.RequestHash, _clock.UtcNow);
        return _operations.Submit(session.StoreId, session, request);
    }

    private ModeBReviewRecord RequireReview(string reviewId) =>
        _store.GetModeBReview(reviewId) ?? throw new InvalidOperationException("MODE_B_REVIEW_NOT_FOUND");

    private static string? ApplyCorrection(JsonObject root, string fieldPath, JsonElement value)
    {
        var parts = fieldPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        JsonNode? target;
        JsonNode provenance;
        if (parts.Length == 3 && parts[0] == "line_items" &&
            int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var index))
        {
            var items = root["line_items"]!.AsArray();
            if (index < 0 || index >= items.Count || parts[2] is not ("product_name" or "quantity_millis" or "unit_price" or "line_total"))
            {
                throw new InvalidOperationException("CORRECTION_PATH_INVALID");
            }

            ValidateCorrectionValue(parts[2], value);
            target = items[index]![parts[2]];
            provenance = root["provenance"]!["fields"]!["line_items"]!.AsArray()[index]![parts[2]]!;
        }
        else if (parts.Length == 2 && parts[0] == "summary" && parts[1] is ("subtotal" or "payable"))
        {
            ValidateCorrectionValue(parts[1], value);
            target = root["summary"]![parts[1]];
            provenance = root["provenance"]!["fields"]!["summary"]![parts[1]]!;
        }
        else if (parts.Length == 3 && parts[0] == "summary" && parts[1] == "extra_amounts" &&
                 root["summary"]!["extra_amounts"]![parts[2]] is not null)
        {
            ValidateCorrectionValue("extra_amount", value);
            target = root["summary"]!["extra_amounts"]![parts[2]];
            provenance = root["provenance"]!["fields"]!["summary"]!["extra_amounts"]![parts[2]]
                ?? throw new InvalidOperationException("CORRECTION_PROVENANCE_MISSING");
        }
        else
        {
            throw new InvalidOperationException("CORRECTION_PATH_INVALID");
        }

        var oldValue = target?.ToJsonString();
        var parent = target?.Parent ?? throw new InvalidOperationException("CORRECTION_TARGET_MISSING");
        var propertyName = parts[^1];
        ((JsonObject)parent)[propertyName] = JsonNode.Parse(value.GetRawText());
        ((JsonObject)provenance.Parent!)[parts[^1]] = "operator_confirmed";
        return oldValue;
    }

    private static void ValidateCorrectionValue(string field, JsonElement value)
    {
        if (field == "product_name")
        {
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            {
                throw new InvalidOperationException("CORRECTION_VALUE_INVALID");
            }

            return;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number) ||
            number < 0 || (field == "quantity_millis" && number == 0))
        {
            throw new InvalidOperationException("CORRECTION_VALUE_INVALID");
        }
    }

    private static void RemoveAttention(JsonObject root, string fieldPath)
    {
        var cells = root["attention_cells"]!.AsArray();
        for (var index = cells.Count - 1; index >= 0; index--)
        {
            var field = cells[index]!["field"]!.GetValue<string>();
            if (string.Equals(field, fieldPath, StringComparison.Ordinal) ||
                string.Equals(field.Replace('.', '/'), fieldPath, StringComparison.Ordinal))
            {
                cells.RemoveAt(index);
            }
        }
    }

    private static JsonElement Element(JsonNode root)
    {
        using var document = JsonDocument.Parse(root.ToJsonString());
        return document.RootElement.Clone();
    }
}
