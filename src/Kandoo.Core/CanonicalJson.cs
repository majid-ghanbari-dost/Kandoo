using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kandoo.Core;

/// <summary>P-02 canonical JSON and SHA-256, with no file, network, or database dependency.</summary>
public static class CanonicalJson
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
        SkipValidation = false
    };

    private static readonly IReadOnlyDictionary<string, string> StateCollectionIds =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["devices"] = "device_id",
            ["memberships"] = "membership_id",
            ["customers"] = "customer_id",
            ["products"] = "product_id",
            ["inventory_events"] = "inventory_event_id",
            ["sales"] = "sale_id",
            ["invoices"] = "invoice_id",
            ["invoice_items"] = "invoice_item_id",
            ["digital_invoices"] = "digital_invoice_id",
            ["locks"] = "lock_id"
        };

    public static string Serialize(JsonElement element) => SerializeCore(element, null, false);

    public static string SerializeEntryWithoutHash(JsonElement entry) => SerializeCore(entry, "entry_hash", false);

    public static string SerializeReplayedState(JsonElement state) => SerializeCore(state, null, true);

    public static string Sha256Hex(JsonElement element) => Sha256Hex(Serialize(element));

    public static string EntryHash(JsonElement entry) => Sha256Hex(SerializeEntryWithoutHash(entry));

    public static string ReplayedStateHash(JsonElement state) => Sha256Hex(SerializeReplayedState(state));

    public static string RequestHash(JsonElement request)
    {
        return RequestHash(
            Guid.Parse(request.GetProperty("store_id").GetString()!),
            Guid.Parse(request.GetProperty("device_id").GetString()!),
            request.GetProperty("operation_id").GetString()!,
            request.GetProperty("operation_type").GetString()!,
            NullableInt64(request.GetProperty("base_version")),
            request.GetProperty("payload"));
    }

    public static string RequestHash(
        Guid storeId,
        Guid deviceId,
        string operationId,
        string operationType,
        long? baseVersion,
        JsonElement payload)
    {
        var input = new RequestHashInput(storeId, deviceId, operationId, operationType, baseVersion, payload);
        return Sha256Hex(JsonSerializer.SerializeToElement(input));
    }

    private static string SerializeCore(JsonElement element, string? omittedRootProperty, bool sortState)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            WriteElement(writer, element, omittedRootProperty, sortState, true);
            writer.Flush();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteElement(Utf8JsonWriter writer, JsonElement element, string? omittedRootProperty, bool sortState, bool isRoot)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                WriteObject(writer, element, omittedRootProperty, sortState, isRoot);
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var child in element.EnumerateArray())
                {
                    WriteElement(writer, child, null, sortState, false);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                if (!element.TryGetInt64(out var number))
                {
                    throw new InvalidOperationException("P-02 hash-bearing JSON permits signed integers only.");
                }

                writer.WriteNumberValue(number);
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
                throw new InvalidOperationException($"Unsupported JSON value kind: {element.ValueKind}.");
        }
    }

    private static void WriteObject(Utf8JsonWriter writer, JsonElement element, string? omittedRootProperty, bool sortState, bool isRoot)
    {
        var properties = element.EnumerateObject().ToList();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            if (!names.Add(property.Name))
            {
                throw new InvalidOperationException($"Duplicate JSON object key is not canonical: {property.Name}.");
            }
        }

        properties.Sort((left, right) => CompareCodePoints(left.Name, right.Name));
        writer.WriteStartObject();
        foreach (var property in properties)
        {
            if (isRoot && omittedRootProperty is not null && property.Name == omittedRootProperty)
            {
                continue;
            }

            writer.WritePropertyName(property.Name);
            if (isRoot && sortState && property.Value.ValueKind == JsonValueKind.Array)
            {
                if (property.Name == "operations")
                {
                    WriteOperationArray(writer, property.Value, sortState);
                    continue;
                }

                if (StateCollectionIds.TryGetValue(property.Name, out var idField))
                {
                    WriteEntityArray(writer, property.Value, idField, sortState);
                    continue;
                }
            }

            WriteElement(writer, property.Value, null, sortState, false);
        }

        writer.WriteEndObject();
    }

    private static void WriteEntityArray(Utf8JsonWriter writer, JsonElement array, string idField, bool sortState)
    {
        var entities = array.EnumerateArray().ToArray();
        Array.Sort(entities, (left, right) => CompareCodePoints(
            left.GetProperty(idField).GetString()!,
            right.GetProperty(idField).GetString()!));
        writer.WriteStartArray();
        foreach (var entity in entities)
        {
            WriteElement(writer, entity, null, sortState, false);
        }

        writer.WriteEndArray();
    }

    private static void WriteOperationArray(Utf8JsonWriter writer, JsonElement array, bool sortState)
    {
        var operations = array.EnumerateArray().ToArray();
        Array.Sort(operations, (left, right) => OperationSequence(left).CompareTo(OperationSequence(right)));
        writer.WriteStartArray();
        foreach (var operation in operations)
        {
            WriteElement(writer, operation, null, sortState, false);
        }

        writer.WriteEndArray();
    }

    private static long OperationSequence(JsonElement operation)
    {
        if (operation.TryGetProperty("operation_seq", out var direct))
        {
            return direct.GetInt64();
        }

        // P-02 replayed Operation fixtures carry the sequence in the response snapshot.
        return operation.GetProperty("response_snapshot").GetProperty("operation_seq").GetInt64();
    }

    private static long? NullableInt64(JsonElement element) => element.ValueKind == JsonValueKind.Null ? null : element.GetInt64();

    private static string Sha256Hex(string canonical)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static int CompareCodePoints(string left, string right)
    {
        var leftRunes = left.EnumerateRunes().ToArray();
        var rightRunes = right.EnumerateRunes().ToArray();
        var length = Math.Min(leftRunes.Length, rightRunes.Length);
        for (var index = 0; index < length; index++)
        {
            var comparison = leftRunes[index].Value.CompareTo(rightRunes[index].Value);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return leftRunes.Length.CompareTo(rightRunes.Length);
    }

    private sealed record RequestHashInput(
        [property: JsonPropertyName("store_id")] Guid StoreId,
        [property: JsonPropertyName("device_id")] Guid DeviceId,
        [property: JsonPropertyName("operation_id")] string OperationId,
        [property: JsonPropertyName("operation_type")] string OperationType,
        [property: JsonPropertyName("base_version")] long? BaseVersion,
        [property: JsonPropertyName("payload")] JsonElement Payload);
}
