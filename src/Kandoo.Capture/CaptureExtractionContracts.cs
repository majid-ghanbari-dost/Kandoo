using System.Text.Json;

namespace Kandoo.Capture;

public enum CaptureExtractionDisposition
{
    Completed = 0,
    Review = 1,
    Blocked = 2,
    Error = 3,
}

public sealed record CaptureExtractionRunResult(
    CaptureExtractionDisposition Disposition,
    string Code,
    int? ExitCode,
    JsonElement? ResultDocument,
    string? Detail = null);

public interface ICaptureFolderExtractor
{
    Task<CaptureExtractionRunResult> ExtractAsync(
        string captureFolderPath,
        DateTimeOffset? capturedAt = null,
        CancellationToken cancellationToken = default);
}

public sealed record CaptureExtractedCell(
    int PageIndex,
    string Kind,
    string FieldName,
    int? LineIndex,
    string? RawValue,
    string Status,
    double? Confidence,
    string? Reason);

public sealed record CaptureExtractedLineItem(
    int LineIndex,
    int SourcePageIndex,
    int SourceLineIndex,
    CaptureExtractedCell? ProductName,
    CaptureExtractedCell? Quantity,
    CaptureExtractedCell? UnitPrice,
    CaptureExtractedCell? LineTotal);

public sealed record CaptureExtractedPage(
    int PageIndex,
    string FileName,
    int Width,
    int Height,
    string Sha256,
    string PageStatus,
    string? E02Status,
    string? E02Reason,
    string? E02LayoutStatus,
    string? GlyphDriftStatus,
    string? TemplateLibraryId,
    string? E05Status,
    string? E05TemplateId,
    double? E05ConfidenceScore);

public sealed record CaptureExtractionError(
    string Code,
    string Message,
    string? Path);

public sealed record CaptureExtractedDocument(
    string ResultVersion,
    string Status,
    string Verdict,
    string VerdictSource,
    string CaptureFolder,
    string? CapturedAt,
    string? SplSha256,
    string? SpoolJobId,
    string? TemplateLibraryId,
    string? CurrencyUnit,
    string? CurrencyUnitStatus,
    string? E06Verdict,
    IReadOnlyList<string> E06ReasonCodes,
    IReadOnlyList<string> E06Details,
    IReadOnlyList<CaptureExtractedLineItem> LineItems,
    IReadOnlyList<CaptureExtractedCell> PayableCandidates,
    IReadOnlyList<CaptureExtractedPage> Pages,
    IReadOnlyList<int> BlockedPages,
    IReadOnlyList<CaptureExtractionError> Errors);

/// <summary>
/// Parses pinned E-30 capture-folder-result-v1 JSON payloads cleanly into typed structures.
/// </summary>
public static class CaptureFolderResultParser
{
    public static CaptureExtractedDocument Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("capture-folder-result-v1 root must be a JSON object.");

        var resultVersion = GetString(root, "result_version") ?? string.Empty;
        if (!string.Equals(resultVersion, "capture-folder-result-v1", StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported result_version '{resultVersion}'.");

        var status = GetString(root, "status") ?? "ERROR";
        var verdict = GetString(root, "verdict") ?? "REVIEW";
        var verdictSource = GetString(root, "verdict_source") ?? string.Empty;
        var captureFolder = GetString(root, "capture_folder") ?? string.Empty;
        var capturedAt = GetString(root, "captured_at");

        string? splSha = null;
        string? spoolJobId = null;
        if (root.TryGetProperty("artifacts", out var artifacts) && artifacts.ValueKind == JsonValueKind.Object)
        {
            if (artifacts.TryGetProperty("spl", out var splObj) && splObj.ValueKind == JsonValueKind.Object)
                splSha = GetString(splObj, "sha256");
            if (artifacts.TryGetProperty("probe_metadata", out var probe) && probe.ValueKind == JsonValueKind.Object)
                spoolJobId = GetString(probe, "spool_job_id");
        }

        var pages = new List<CaptureExtractedPage>();
        string? firstTemplateLib = null;
        if (root.TryGetProperty("pages", out var pagesArr) && pagesArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in pagesArr.EnumerateArray())
            {
                if (p.ValueKind != JsonValueKind.Object)
                    continue;

                var pageIdx = GetInt(p, "page_index") ?? (pages.Count + 1);
                var file = GetString(p, "file") ?? string.Empty;
                var width = GetInt(p, "width") ?? 0;
                var height = GetInt(p, "height") ?? 0;
                var sha = GetString(p, "sha256") ?? string.Empty;
                var pageStatus = GetString(p, "status") ?? string.Empty;

                string? e02Status = null, e02Reason = null, e02Layout = null, glyphStatus = null, templateLib = null;
                if (p.TryGetProperty("e02", out var e02) && e02.ValueKind == JsonValueKind.Object)
                {
                    e02Status = GetString(e02, "status");
                    e02Reason = GetString(e02, "reason");
                    e02Layout = GetString(e02, "layout_status");
                    if (e02.TryGetProperty("glyph_drift", out var gd) && gd.ValueKind == JsonValueKind.Object)
                    {
                        glyphStatus = GetString(gd, "status");
                        templateLib = GetString(gd, "template_library_id");
                        firstTemplateLib ??= templateLib;
                    }
                }

                string? e05Status = null, e05Template = null;
                double? e05Score = null;
                if (p.TryGetProperty("e05", out var e05) && e05.ValueKind == JsonValueKind.Object)
                {
                    e05Status = GetString(e05, "status");
                    e05Template = GetString(e05, "template_id");
                    e05Score = GetDouble(e05, "confidence_score");
                }

                pages.Add(new CaptureExtractedPage(
                    pageIdx, file, width, height, sha, pageStatus,
                    e02Status, e02Reason, e02Layout, glyphStatus, templateLib,
                    e05Status, e05Template, e05Score));
            }
        }

        var lineItems = new List<CaptureExtractedLineItem>();
        var payables = new List<CaptureExtractedCell>();

        if (root.TryGetProperty("invoice_candidates", out var invCand) && invCand.ValueKind == JsonValueKind.Object)
        {
            if (invCand.TryGetProperty("line_items", out var liArr) && liArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var li in liArr.EnumerateArray())
                {
                    if (li.ValueKind != JsonValueKind.Object)
                        continue;

                    var idx = GetInt(li, "line_index") ?? lineItems.Count;
                    var srcPage = GetInt(li, "source_page_index") ?? 1;
                    var srcLine = GetInt(li, "source_line_index") ?? idx;

                    lineItems.Add(new CaptureExtractedLineItem(
                        idx,
                        srcPage,
                        srcLine,
                        li.TryGetProperty("product_name", out var pn) ? ParseCell(pn) : null,
                        li.TryGetProperty("quantity", out var qty) ? ParseCell(qty) : null,
                        li.TryGetProperty("unit_price", out var up) ? ParseCell(up) : null,
                        li.TryGetProperty("line_total", out var lt) ? ParseCell(lt) : null));
                }
            }

            if (invCand.TryGetProperty("payable_candidates", out var payArr) && payArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in payArr.EnumerateArray())
                {
                    var cell = ParseCell(p);
                    if (cell is not null)
                        payables.Add(cell);
                }
            }
        }

        if (payables.Count == 0 &&
            root.TryGetProperty("numeric_candidates", out var numCand) && numCand.ValueKind == JsonValueKind.Object &&
            numCand.TryGetProperty("payable", out var payFallback) && payFallback.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in payFallback.EnumerateArray())
            {
                var cell = ParseCell(p);
                if (cell is not null)
                    payables.Add(cell);
            }
        }

        string? currencyUnit = null, currencyStatus = null;
        if (root.TryGetProperty("e05_bank", out var bank) && bank.ValueKind == JsonValueKind.Object)
        {
            currencyUnit = GetString(bank, "currency_unit");
            currencyStatus = GetString(bank, "currency_unit_status");
        }

        string? e06Verdict = null;
        var e06Reasons = new List<string>();
        var e06Details = new List<string>();
        if (root.TryGetProperty("e06", out var e06) && e06.ValueKind == JsonValueKind.Object)
        {
            e06Verdict = GetString(e06, "verdict");
            if (e06.TryGetProperty("reason_codes", out var rcArr) && rcArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var rc in rcArr.EnumerateArray())
                {
                    if (rc.ValueKind == JsonValueKind.String && rc.GetString() is { Length: > 0 } s)
                        e06Reasons.Add(s);
                }
            }

            if (e06.TryGetProperty("details", out var detArr) && detArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in detArr.EnumerateArray())
                {
                    if (d.ValueKind == JsonValueKind.String && d.GetString() is { Length: > 0 } s)
                        e06Details.Add(s);
                }
            }
        }

        var blockedPages = new List<int>();
        if (root.TryGetProperty("blocked_pages", out var bpArr) && bpArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var bp in bpArr.EnumerateArray())
            {
                if (bp.ValueKind == JsonValueKind.Number && bp.TryGetInt32(out var pageNo))
                    blockedPages.Add(pageNo);
            }
        }

        var errors = new List<CaptureExtractionError>();
        if (root.TryGetProperty("errors", out var errArr) && errArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var err in errArr.EnumerateArray())
            {
                if (err.ValueKind != JsonValueKind.Object)
                    continue;
                errors.Add(new CaptureExtractionError(
                    GetString(err, "code") ?? "UNKNOWN_ERROR",
                    GetString(err, "message") ?? string.Empty,
                    GetString(err, "path")));
            }
        }

        return new CaptureExtractedDocument(
            resultVersion,
            status,
            verdict,
            verdictSource,
            captureFolder,
            capturedAt,
            splSha,
            spoolJobId,
            firstTemplateLib,
            currencyUnit,
            currencyStatus,
            e06Verdict,
            e06Reasons,
            e06Details,
            lineItems,
            payables,
            pages,
            blockedPages,
            errors);
    }

    private static CaptureExtractedCell? ParseCell(JsonElement elem)
    {
        if (elem.ValueKind != JsonValueKind.Object)
            return null;

        return new CaptureExtractedCell(
            GetInt(elem, "page_index") ?? 1,
            GetString(elem, "kind") ?? string.Empty,
            GetString(elem, "field_name") ?? string.Empty,
            GetInt(elem, "line_index"),
            GetString(elem, "raw_value"),
            GetString(elem, "status") ?? "UNAVAILABLE",
            GetDouble(elem, "confidence"),
            GetString(elem, "reason"));
    }

    private static string? GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static int? GetInt(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var val)
            ? val
            : null;

    private static double? GetDouble(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out var val)
            ? val
            : null;
}
