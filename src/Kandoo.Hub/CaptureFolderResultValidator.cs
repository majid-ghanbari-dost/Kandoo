using System.Text.Json;

namespace Kandoo.Hub;

/// <summary>
/// Validator for the pinned capture-folder-result v1 schema. It intentionally
/// implements only that schema, not a general JSON Schema evaluator.
/// </summary>
public static class CaptureFolderResultValidator
{
    private static readonly string[] Required =
    {
        "result_version", "status", "verdict", "verdict_source", "capture_folder",
        "errors", "scoring_fields", "accuracy", "pages", "numeric_candidates", "e06"
    };

    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "result_version", "status", "verdict", "verdict_source", "capture_folder",
        "captured_at", "artifacts", "scoring_fields", "accuracy", "invoice_candidates",
        "numeric_candidates", "pages", "e05_bank", "e06", "blocked_pages", "errors"
    };

    public static bool TryValidate(JsonElement root, out string? detail)
    {
        detail = null;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Invalid(out detail, "ROOT_NOT_OBJECT");
        }

        foreach (var property in root.EnumerateObject())
        {
            if (!Allowed.Contains(property.Name))
            {
                return Invalid(out detail, "ROOT_ADDITIONAL_PROPERTY");
            }
        }

        if (Required.Any(name => !root.TryGetProperty(name, out _)))
        {
            return Invalid(out detail, "ROOT_REQUIRED_PROPERTY_MISSING");
        }

        if (!IsConst(root.GetProperty("result_version"), "capture-folder-result-v1") ||
            !IsEnum(root.GetProperty("status"), "ACCEPT", "REVIEW", "REJECT", "BLOCKED", "ERROR") ||
            !IsEnum(root.GetProperty("verdict"), "ACCEPT", "REVIEW", "REJECT") ||
            !IsNonEmptyString(root.GetProperty("verdict_source")) ||
            !IsNonEmptyString(root.GetProperty("capture_folder")))
        {
            return Invalid(out detail, "ROOT_SCALARS_INVALID");
        }

        if (root.TryGetProperty("captured_at", out var capturedAt) &&
            capturedAt.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
        {
            return Invalid(out detail, "CAPTURED_AT_INVALID");
        }

        foreach (var name in new[] { "artifacts", "invoice_candidates", "e05_bank" })
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Object)
            {
                return Invalid(out detail, "OPTIONAL_OBJECT_INVALID");
            }
        }

        if (!IsScoringArray(root.GetProperty("scoring_fields")) ||
            !IsObjectArray(root.GetProperty("pages")) ||
            root.GetProperty("e06").ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
        {
            return Invalid(out detail, "ROOT_ARRAYS_INVALID");
        }

        if (root.TryGetProperty("blocked_pages", out var blockedPages) && !IsBlockedPages(blockedPages))
        {
            return Invalid(out detail, "BLOCKED_PAGES_INVALID");
        }

        return IsErrors(root.GetProperty("errors"), out detail) &&
               IsAccuracy(root.GetProperty("accuracy"), out detail) &&
               IsNumericCandidates(root.GetProperty("numeric_candidates"), out detail);
    }

    private static bool IsErrors(JsonElement errors, out string? detail)
    {
        detail = null;
        if (errors.ValueKind != JsonValueKind.Array)
        {
            return Invalid(out detail, "ERRORS_NOT_ARRAY");
        }

        foreach (var error in errors.EnumerateArray())
        {
            if (error.ValueKind != JsonValueKind.Object ||
                !error.TryGetProperty("code", out var code) || !IsNonEmptyString(code) ||
                !error.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.String ||
                (error.TryGetProperty("path", out var path) && path.ValueKind != JsonValueKind.String))
            {
                return Invalid(out detail, "ERROR_ENTRY_INVALID");
            }
        }

        return true;
    }

    private static bool IsAccuracy(JsonElement accuracy, out string? detail)
    {
        detail = null;
        if (accuracy.ValueKind != JsonValueKind.Object ||
            !accuracy.TryGetProperty("status", out var status) || !IsConst(status, "NOT_SCORED") ||
            !accuracy.TryGetProperty("expected_values_available", out var expected) ||
            expected.ValueKind != JsonValueKind.False ||
            !accuracy.TryGetProperty("fields", out var fields) || !IsScoringArray(fields) ||
            !accuracy.TryGetProperty("reason", out var reason) || !IsNonEmptyString(reason))
        {
            return Invalid(out detail, "ACCURACY_INVALID");
        }

        return true;
    }

    private static bool IsNumericCandidates(JsonElement numeric, out string? detail)
    {
        detail = null;
        if (numeric.ValueKind != JsonValueKind.Object)
        {
            return Invalid(out detail, "NUMERIC_NOT_OBJECT");
        }

        foreach (var property in numeric.EnumerateObject())
        {
            if (property.Name is not ("quantity" or "line_total" or "payable"))
            {
                return Invalid(out detail, "NUMERIC_ADDITIONAL_PROPERTY");
            }
        }

        foreach (var name in new[] { "quantity", "line_total", "payable" })
        {
            if (!numeric.TryGetProperty(name, out var values) || values.ValueKind != JsonValueKind.Array)
            {
                return Invalid(out detail, "NUMERIC_ARRAY_MISSING");
            }
        }

        return true;
    }

    private static bool IsBlockedPages(JsonElement pages)
    {
        if (pages.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return pages.EnumerateArray().All(page =>
            page.ValueKind == JsonValueKind.Number && page.TryGetInt32(out var number) && number >= 1);
    }

    private static bool IsObjectArray(JsonElement array) =>
        array.ValueKind == JsonValueKind.Array && array.EnumerateArray().All(item => item.ValueKind == JsonValueKind.Object);

    private static bool IsScoringArray(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in array.EnumerateArray())
        {
            if (!IsEnum(item, "quantity", "line_total", "payable") || !seen.Add(item.GetString()!))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsConst(JsonElement value, string expected) =>
        value.ValueKind == JsonValueKind.String && string.Equals(value.GetString(), expected, StringComparison.Ordinal);

    private static bool IsEnum(JsonElement value, params string[] allowed) =>
        value.ValueKind == JsonValueKind.String && value.GetString() is string text &&
        allowed.Contains(text, StringComparer.Ordinal);

    private static bool IsNonEmptyString(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && value.GetString()!.Length > 0;

    private static bool Invalid(out string? detail, string code)
    {
        detail = code;
        return false;
    }
}
