using System.Buffers;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Kandoo.Core;

public static class CanonicalInvoiceErrorCodes
{
    public const string InvalidJson = "INVALID_JSON";
    public const string RequiredFieldMissing = "REQUIRED_FIELD_MISSING";
    public const string SchemaValidationFailed = "SCHEMA_VALIDATION_FAILED";
    public const string QuantityNotInteger = "QUANTITY_MILLIS_NOT_INTEGER";
    public const string AmountNotInteger = "AMOUNT_NOT_INTEGER";
    public const string AmountNegative = "AMOUNT_NEGATIVE";
    public const string NonIntegerNumber = "NON_INTEGER_JSON_NUMBER";
    public const string DisplayNumberBeforeIssue = "DISPLAY_NUMBER_BEFORE_ISSUE";
    public const string IssuedHashMismatch = "ISSUED_CONTENT_HASH_MISMATCH";
    public const string InvalidUtf16 = "INVALID_UTF16";
}

public sealed class CanonicalInvoiceException : InvalidOperationException
{
    public CanonicalInvoiceException(string code, string message) : base(message) => Code = code;
    public string Code { get; }
}

public sealed record CanonicalInvoiceLineItem(
    string ProductName,
    long QuantityMillis,
    long UnitPriceRials,
    long LineTotalRials);

/// <summary>A review item, never an issued invoice or a native sale/inventory effect.</summary>
public sealed record ReviewQueueEntity(
    string QueueStatus,
    string InvoiceId,
    string StoreId,
    string SourceState,
    string? DisplayNumber,
    string OperationId,
    string DeviceId,
    string PublicToken,
    long PayableRials,
    IReadOnlyList<CanonicalInvoiceLineItem> LineItems,
    JsonElement Source);

public sealed record CanonicalInvoiceConsumeResult(bool Accepted, ReviewQueueEntity? Review, string? ErrorCode);

/// <summary>Strict E-11 Canonical Invoice v0 consumer and Mode-B review bridge.</summary>
public static class CanonicalInvoiceConsumer
{
    private static readonly string[] RootRequired =
    {
        "schema_version", "mode", "invoice_id", "display_number", "operation_id", "device_id",
        "public_token", "store_id", "customer", "state", "state_history", "line_items", "summary",
        "attention_cells", "header", "provenance"
    };

    private static readonly string[] RootAllowed = RootRequired.Concat(new[] { "issued_content_sha256" }).ToArray();
    private static readonly string[] States = { "DRAFT", "EXTRACTED", "VALIDATED", "ISSUED", "REVOKED", "SUPERSEDED" };
    private static readonly string[] ProvenanceLabels = { "native", "captured", "derived", "untrusted", "unavailable" };

    public static ReviewQueueEntity ConsumeForReview(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 128
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Error(CanonicalInvoiceErrorCodes.SchemaValidationFailed, "The Canonical Invoice root must be an object.");
            }

            ValidateJsonTree(root, "$");
            ValidateRoot(root);
            return MapReview(root);
        }
        catch (CanonicalInvoiceException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            throw Error(CanonicalInvoiceErrorCodes.InvalidJson, "The Canonical Invoice is not valid strict JSON.");
        }
    }

    public static CanonicalInvoiceConsumeResult TryConsumeForReview(string json)
    {
        try
        {
            return new CanonicalInvoiceConsumeResult(true, ConsumeForReview(json), null);
        }
        catch (CanonicalInvoiceException exception)
        {
            return new CanonicalInvoiceConsumeResult(false, null, exception.Code);
        }
    }

    public static string ComputeIssuedContentSha256(JsonElement invoice) => CanonicalInvoiceJson.ComputeIssuedHash(invoice);

    private static void ValidateRoot(JsonElement root)
    {
        RejectUnknown(root, RootAllowed);
        foreach (var name in RootRequired)
        {
            Require(root, name);
        }

        if (RequireString(root, "schema_version") != "0")
        {
            throw Error(CanonicalInvoiceErrorCodes.SchemaValidationFailed, "Unsupported Canonical Invoice schema_version.");
        }

        var mode = RequireString(root, "mode");
        if (mode is not ("A" or "B"))
        {
            throw Error(CanonicalInvoiceErrorCodes.SchemaValidationFailed, "mode must be A or B.");
        }

        RequireString(root, "invoice_id");
        RequireString(root, "operation_id");
        RequireString(root, "device_id");
        RequireString(root, "store_id");
        ValidatePublicToken(RequireString(root, "public_token"));
        var state = RequireState(root, "state");
        ValidateCustomer(Require(root, "customer"));
        ValidateStateHistory(Require(root, "state_history"), state);
        ValidateLineItems(Require(root, "line_items"));
        ValidateSummary(Require(root, "summary"));
        ValidateAttentionCells(Require(root, "attention_cells"), state);
        ValidateHeader(Require(root, "header"));
        var provenanceFields = ValidateProvenance(Require(root, "provenance"));
        ValidateLifecycle(root, state, provenanceFields);
    }

    private static void ValidateLifecycle(JsonElement root, string state, JsonElement provenanceFields)
    {
        var display = Require(root, "display_number");
        var preIssue = state is "DRAFT" or "EXTRACTED" or "VALIDATED";
        var displayProvenance = RequireString(provenanceFields, "display_number");
        if (preIssue)
        {
            if (display.ValueKind != JsonValueKind.Null || displayProvenance != "unavailable")
            {
                throw Error(CanonicalInvoiceErrorCodes.DisplayNumberBeforeIssue, "display_number is unavailable before ISSUE.");
            }

            if (root.TryGetProperty("issued_content_sha256", out _))
            {
                throw Error(CanonicalInvoiceErrorCodes.SchemaValidationFailed, "Pre-ISSUE records cannot carry an issued-content hash.");
            }

            return;
        }

        if (display.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(display.GetString()) || displayProvenance != "native")
        {
            throw Error(CanonicalInvoiceErrorCodes.SchemaValidationFailed, "Issued and terminal records require a native display_number.");
        }

        var supplied = RequireString(root, "issued_content_sha256");
        ValidateHash(supplied);
        var computed = CanonicalInvoiceJson.ComputeIssuedHash(root);
        if (!string.Equals(supplied, computed, StringComparison.Ordinal))
        {
            throw Error(CanonicalInvoiceErrorCodes.IssuedHashMismatch, "The issued-content hash does not match the immutable projection.");
        }
    }

    private static void ValidateCustomer(JsonElement customer)
    {
        RequireObject(customer);
        RejectUnknown(customer, "phone", "phone_status");
        RequireString(customer, "phone");
        var status = RequireString(customer, "phone_status");
        if (status is not ("claimed" or "verified"))
        {
            throw Error(CanonicalInvoiceErrorCodes.SchemaValidationFailed, "Invalid customer phone_status.");
        }
    }

    private static void ValidateStateHistory(JsonElement history, string currentState)
    {
        RequireArray(history);
        JsonElement? last = null;
        foreach (var transition in history.EnumerateArray())
        {
            RequireObject(transition);
            RejectUnknown(transition, "from_state", "to_state", "operation_id", "device_id");
            RequireState(transition, "from_state");
            RequireState(transition, "to_state");
            RequireString(transition, "operation_id");
            RequireString(transition, "device_id");
            last = transition;
        }

        if (last is not null && RequireString(last.Value, "to_state") != currentState)
        {
            throw Error(CanonicalInvoiceErrorCodes.SchemaValidationFailed, "state_history must terminate at state.");
        }
    }

    private static void ValidateLineItems(JsonElement items)
    {
        RequireArray(items);
        if (items.GetArrayLength() == 0)
        {
            throw Error(CanonicalInvoiceErrorCodes.SchemaValidationFailed, "line_items must not be empty.");
        }

        foreach (var item in items.EnumerateArray())
        {
            RequireObject(item);
            RejectUnknown(item, "product_name", "quantity_millis", "unit_price", "line_total");
            RequireString(item, "product_name");
            RequireInteger(item, "quantity_millis", CanonicalInvoiceErrorCodes.QuantityNotInteger, false);
            RequireInteger(item, "unit_price", CanonicalInvoiceErrorCodes.AmountNotInteger, true);
            RequireInteger(item, "line_total", CanonicalInvoiceErrorCodes.AmountNotInteger, true);
        }
    }

    private static void ValidateSummary(JsonElement summary)
    {
        RequireObject(summary);
        RejectUnknown(summary, "subtotal", "extra_amounts", "payable", "tax_lines", "discount_lines", "rounding");
        RequireInteger(summary, "subtotal", CanonicalInvoiceErrorCodes.AmountNotInteger, true);
        RequireInteger(summary, "payable", CanonicalInvoiceErrorCodes.AmountNotInteger, true);
        var extras = Require(summary, "extra_amounts");
        RequireObject(extras);
        foreach (var extra in extras.EnumerateObject())
        {
            RequireIntegerValue(extra.Value, CanonicalInvoiceErrorCodes.AmountNotInteger, true, "extra_amounts");
        }

        ValidateOptionalAmountLines(summary, "tax_lines");
        ValidateOptionalAmountLines(summary, "discount_lines");
        if (summary.TryGetProperty("rounding", out var rounding) && rounding.ValueKind != JsonValueKind.Null)
        {
            // ACC-0: rounding is reserved structure; no accounting rule is inferred here.
            RequireIntegerValue(rounding, CanonicalInvoiceErrorCodes.AmountNotInteger, false, "rounding");
        }
    }

    private static void ValidateOptionalAmountLines(JsonElement summary, string name)
    {
        if (!summary.TryGetProperty(name, out var lines))
        {
            return;
        }

        RequireArray(lines);
        foreach (var line in lines.EnumerateArray())
        {
            RequireObject(line);
            RejectUnknown(line, "label", "amount", "provenance");
            Require(line, "label");
            if (line.GetProperty("label").ValueKind != JsonValueKind.String)
            {
                throw Error(CanonicalInvoiceErrorCodes.SchemaValidationFailed, "Amount-line labels must be strings.");
            }

            // ACC-0: optional accounting lines are structurally checked, not business-validated.
            RequireInteger(line, "amount", CanonicalInvoiceErrorCodes.AmountNotInteger, false);
            RequireLabel(line, "provenance");
        }
    }

    private static void ValidateAttentionCells(JsonElement cells, string state)
    {
        RequireArray(cells);
        if (state is "VALIDATED" or "ISSUED" or "REVOKED" or "SUPERSEDED" && cells.GetArrayLength() != 0)
        {
            throw Error(CanonicalInvoiceErrorCodes.SchemaValidationFailed, "Issued records cannot retain unresolved attention_cells.");
        }

        foreach (var cell in cells.EnumerateArray())
        {
            RequireObject(cell);
            RejectUnknown(cell, "field", "raw_value", "reason");
            RequireString(cell, "field");
            Require(cell, "raw_value");
            if (cell.GetProperty("raw_value").ValueKind != JsonValueKind.String)
            {
                throw Error(CanonicalInvoiceErrorCodes.SchemaValidationFailed, "attention_cells.raw_value must be a string.");
            }

            RequireString(cell, "reason");
        }
    }

    private static void ValidateHeader(JsonElement header)
    {
        RequireObject(header);
        RejectUnknown(header, "captured_at", "printed_date", "printed_time", "invoice_number");
        foreach (var property in header.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Null)
            {
                RequireStringToken(property.Value, property.Name);
            }
        }
    }

    private static JsonElement ValidateProvenance(JsonElement provenance)
    {
        RequireObject(provenance);
        RejectUnknown(provenance, "image_sha256", "template_library_id", "engine_version", "fields");
        foreach (var name in new[] { "image_sha256", "template_library_id", "engine_version", "fields" })
        {
            Require(provenance, name);
        }

        var image = provenance.GetProperty("image_sha256");
        if (image.ValueKind != JsonValueKind.Null)
        {
            ValidateHash(RequireStringValue(image, "image_sha256"));
        }

        RequireNullableString(provenance, "template_library_id");
        RequireNullableString(provenance, "engine_version");
        var fields = Require(provenance, "fields");
        RequireObject(fields);
        RejectUnknown(fields, "invoice_id", "display_number", "operation_id", "device_id", "public_token", "store_id", "customer", "line_items", "summary", "header");
        foreach (var name in new[] { "invoice_id", "display_number", "operation_id", "device_id", "public_token", "store_id", "customer", "line_items", "summary", "header" })
        {
            Require(fields, name);
        }

        foreach (var name in new[] { "invoice_id", "display_number", "operation_id", "device_id", "public_token", "store_id" })
        {
            RequireLabel(fields, name);
        }

        var customer = fields.GetProperty("customer");
        RequireObject(customer);
        RejectUnknown(customer, "phone", "phone_status");
        RequireLabel(customer, "phone");
        RequireLabel(customer, "phone_status");
        var items = fields.GetProperty("line_items");
        RequireArray(items);
        if (items.GetArrayLength() == 0)
        {
            throw Error(CanonicalInvoiceErrorCodes.SchemaValidationFailed, "Provenance line_items must not be empty.");
        }

        foreach (var item in items.EnumerateArray())
        {
            RequireObject(item);
            RejectUnknown(item, "product_name", "quantity_millis", "unit_price", "line_total");
            foreach (var name in new[] { "product_name", "quantity_millis", "unit_price", "line_total" })
            {
                RequireLabel(item, name);
            }
        }

        var summary = fields.GetProperty("summary");
        RequireObject(summary);
        RejectUnknown(summary, "subtotal", "extra_amounts", "payable");
        RequireLabel(summary, "subtotal");
        RequireLabel(summary, "payable");
        var extras = Require(summary, "extra_amounts");
        RequireObject(extras);
        foreach (var extra in extras.EnumerateObject())
        {
            RequireLabelValue(extra.Value, "extra_amounts");
        }

        var header = fields.GetProperty("header");
        RequireObject(header);
        RejectUnknown(header, "captured_at", "printed_date", "printed_time", "invoice_number");
        foreach (var name in new[] { "captured_at", "printed_date", "printed_time", "invoice_number" })
        {
            RequireLabel(header, name);
        }

        return fields;
    }

    private static ReviewQueueEntity MapReview(JsonElement root)
    {
        var items = root.GetProperty("line_items").EnumerateArray().Select(item => new CanonicalInvoiceLineItem(
            item.GetProperty("product_name").GetString()!,
            item.GetProperty("quantity_millis").GetInt64(),
            item.GetProperty("unit_price").GetInt64(),
            item.GetProperty("line_total").GetInt64())).ToArray();
        return new ReviewQueueEntity(
            "REVIEW",
            root.GetProperty("invoice_id").GetString()!,
            root.GetProperty("store_id").GetString()!,
            root.GetProperty("state").GetString()!,
            root.GetProperty("display_number").ValueKind == JsonValueKind.Null ? null : root.GetProperty("display_number").GetString(),
            root.GetProperty("operation_id").GetString()!,
            root.GetProperty("device_id").GetString()!,
            root.GetProperty("public_token").GetString()!,
            root.GetProperty("summary").GetProperty("payable").GetInt64(),
            items,
            root.Clone());
    }

    private static void ValidateJsonTree(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    ValidateUnicode(property.Name);
                    if (!names.Add(property.Name))
                    {
                        throw Error(CanonicalInvoiceErrorCodes.SchemaValidationFailed, "Duplicate JSON object keys are forbidden.");
                    }

                    ValidateJsonTree(property.Value, path + "/" + property.Name);
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var child in element.EnumerateArray())
                {
                    ValidateJsonTree(child, path + "/" + index++);
                }

                break;
            case JsonValueKind.Number:
                if (!IsIntegerToken(element.GetRawText()))
                {
                    var code = path.EndsWith("/quantity_millis", StringComparison.Ordinal)
                        ? CanonicalInvoiceErrorCodes.QuantityNotInteger
                        : path.Contains("amount", StringComparison.Ordinal) || path.EndsWith("/unit_price", StringComparison.Ordinal)
                            || path.EndsWith("/line_total", StringComparison.Ordinal) || path.EndsWith("/subtotal", StringComparison.Ordinal)
                            || path.EndsWith("/payable", StringComparison.Ordinal) || path.EndsWith("/rounding", StringComparison.Ordinal)
                            ? CanonicalInvoiceErrorCodes.AmountNotInteger
                            : CanonicalInvoiceErrorCodes.NonIntegerNumber;
                    throw Error(code, "Only base-10 JSON integer number tokens are accepted.");
                }

                break;
            case JsonValueKind.String:
                ValidateUnicode(element.GetString()!);
                break;
        }
    }

    private static bool IsIntegerToken(string raw)
    {
        if (string.IsNullOrEmpty(raw) || raw == "-0" || raw.Contains('.') || raw.Contains('e') || raw.Contains('E') || raw.StartsWith('+'))
        {
            return false;
        }

        var index = raw[0] == '-' ? 1 : 0;
        if (index >= raw.Length || (raw[index] == '0' && raw.Length - index > 1))
        {
            return false;
        }

        for (; index < raw.Length; index++)
        {
            if (raw[index] is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static long RequireInteger(JsonElement objectElement, string name, string code, bool nonNegative)
    {
        return RequireIntegerValue(Require(objectElement, name), code, nonNegative, name);
    }

    private static long RequireIntegerValue(JsonElement value, string code, bool nonNegative, string name)
    {
        if (value.ValueKind != JsonValueKind.Number || !IsIntegerToken(value.GetRawText()) || !value.TryGetInt64(out var result))
        {
            throw Error(code, $"{name} must be a JSON integer.");
        }

        if (nonNegative && result < 0)
        {
            throw Error(CanonicalInvoiceErrorCodes.AmountNegative, $"{name} must not be negative.");
        }

        return result;
    }

    private static string RequireState(JsonElement objectElement, string name)
    {
        var state = RequireString(objectElement, name);
        if (!States.Contains(state, StringComparer.Ordinal))
        {
            throw Error(CanonicalInvoiceErrorCodes.SchemaValidationFailed, "Invalid lifecycle state.");
        }

        return state;
    }

    private static void RequireLabel(JsonElement objectElement, string name) => RequireLabelValue(Require(objectElement, name), name);

    private static void RequireLabelValue(JsonElement value, string name)
    {
        var label = RequireStringValue(value, name);
        if (!ProvenanceLabels.Contains(label, StringComparer.Ordinal))
        {
            throw Error(CanonicalInvoiceErrorCodes.SchemaValidationFailed, "Invalid provenance label.");
        }
    }

    private static JsonElement Require(JsonElement objectElement, string name)
    {
        if (!objectElement.TryGetProperty(name, out var value))
        {
            throw Error(CanonicalInvoiceErrorCodes.RequiredFieldMissing, $"Required field is missing: {name}.");
        }

        return value;
    }

    private static string RequireString(JsonElement objectElement, string name) => RequireStringValue(Require(objectElement, name), name);

    private static string RequireStringValue(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(value.GetString()))
        {
            throw Error(CanonicalInvoiceErrorCodes.SchemaValidationFailed, $"{name} must be a non-empty string.");
        }

        return value.GetString()!;
    }

    private static string RequireStringToken(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw Error(CanonicalInvoiceErrorCodes.SchemaValidationFailed, $"{name} must be a string.");
        }

        return value.GetString()!;
    }

    private static void RequireNullableString(JsonElement objectElement, string name)
    {
        var value = Require(objectElement, name);
        if (value.ValueKind != JsonValueKind.Null)
        {
            RequireStringValue(value, name);
        }
    }

    private static void RequireObject(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Error(CanonicalInvoiceErrorCodes.SchemaValidationFailed, "Expected a JSON object.");
        }
    }

    private static void RequireArray(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Error(CanonicalInvoiceErrorCodes.SchemaValidationFailed, "Expected a JSON array.");
        }
    }

    private static void RejectUnknown(JsonElement objectElement, params string[] allowed)
    {
        var names = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (var property in objectElement.EnumerateObject())
        {
            if (!names.Contains(property.Name))
            {
                throw Error(CanonicalInvoiceErrorCodes.SchemaValidationFailed, $"Unexpected field: {property.Name}.");
            }
        }
    }

    private static void ValidatePublicToken(string token)
    {
        if (token.Length < 22 || token.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')))
        {
            throw Error(CanonicalInvoiceErrorCodes.SchemaValidationFailed, "public_token must be an unpadded base64url token of at least 22 characters.");
        }
    }

    private static void ValidateHash(string value)
    {
        if (value.Length != 64 || value.Any(character => !(character is >= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw Error(CanonicalInvoiceErrorCodes.SchemaValidationFailed, "Expected a lowercase SHA-256 hexadecimal digest.");
        }
    }

    private static void ValidateUnicode(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (char.IsHighSurrogate(current))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                {
                    throw Error(CanonicalInvoiceErrorCodes.InvalidUtf16, "Lone UTF-16 surrogates are forbidden.");
                }

                index++;
            }
            else if (char.IsLowSurrogate(current))
            {
                throw Error(CanonicalInvoiceErrorCodes.InvalidUtf16, "Lone UTF-16 surrogates are forbidden.");
            }
        }
    }

    private static CanonicalInvoiceException Error(string code, string message) => new(code, message);
}

internal static class CanonicalInvoiceJson
{
    private static readonly HashSet<string> ProjectionFields = new(StringComparer.Ordinal)
    {
        "schema_version", "mode", "invoice_id", "display_number", "operation_id", "device_id", "public_token",
        "store_id", "customer", "line_items", "summary", "attention_cells", "header", "provenance"
    };

    private static readonly JsonWriterOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
        SkipValidation = false
    };

    internal static string ComputeIssuedHash(JsonElement invoice)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, Options))
        {
            writer.WriteStartObject();
            foreach (var property in invoice.EnumerateObject().Where(property => ProjectionFields.Contains(property.Name)).OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteValue(writer, property.Value);
            }

            writer.WriteEndObject();
            writer.Flush();
        }

        return Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant();
    }

    private static void WriteValue(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteValue(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var child in value.EnumerateArray())
                {
                    WriteValue(writer, child);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(value.GetString()!);
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(value.GetRawText());
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidOperationException("Unsupported Canonical Invoice JSON value.");
        }
    }
}
