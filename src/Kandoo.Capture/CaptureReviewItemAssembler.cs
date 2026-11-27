using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kandoo.Core;

namespace Kandoo.Capture;

public sealed record CaptureProducerContext(
    Guid StoreId,
    Guid DeviceId,
    string? OperationId = null,
    string? PublicToken = null,
    string? InvoiceId = null,
    string? PrintedInvoiceNumber = null,
    string? PrintedDate = null,
    string? PrintedTime = null,
    string? XpsText = null,
    IReadOnlyList<string>? LineProductNames = null,
    string EngineVersion = "e30.extraction-bridge.v1");

public sealed record CaptureWhatIReadLine(
    int LineIndex,
    string Label,
    string QuantityText,
    long? QuantityMillis,
    string UnitPriceText,
    long? UnitPriceRials,
    string LineTotalText,
    long? LineTotalRials,
    string Status,
    double? Confidence,
    bool NeedsAttention);

public sealed record CapturePageEvidence(
    int PageIndex,
    string FileName,
    string? FullPath,
    int Width,
    int Height,
    string Sha256,
    string? E02Status,
    string? E02Reason,
    string? E05Status,
    string? E05TemplateId,
    double? E05Confidence);

public sealed record CaptureReviewEvidence(
    string ReviewId,
    string QueueStatus,
    string SourceState,
    string CaptureFolderPath,
    string? SpoolJobId,
    string? SplFileName,
    string? SplSha256,
    string CapturedAt,
    string? ExtractionStatus,
    string? ExtractionVerdict,
    string? CurrencyUnit,
    string? CurrencyUnitStatus,
    IReadOnlyList<string> E06ReasonCodes,
    IReadOnlyList<CapturePageEvidence> Pages,
    IReadOnlyList<CaptureWhatIReadLine> WhatIReadLines,
    string? PayableRawText,
    long PayableRials,
    long SubtotalRials,
    IReadOnlyList<string> XpsTextLines,
    IReadOnlyList<CaptureIntakeProblem> IntakeProblems,
    IReadOnlyList<CaptureExtractionError> ExtractionErrors,
    IReadOnlyList<string> AttentionSummaries);

public sealed record CaptureReviewPackage(
    string ReviewId,
    string CanonicalInvoiceJson,
    ReviewQueueEntity ConsumedReview,
    CaptureReviewEvidence Evidence);

/// <summary>
/// Fail-closed Mode-B assembler bridging capture folders and E-30 extraction results
/// into strict E-11 canonical_invoice_v0 review items plus "what I read" operator evidence.
/// Never issues invoices or creates customer, sale, or inventory effects.
/// </summary>
public static class CaptureReviewItemAssembler
{
    private static readonly JsonSerializerOptions CompactOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    public static CaptureReviewPackage Assemble(
        CaptureProducerContext context,
        CaptureIntakeInspection? inspection,
        CaptureExtractionRunResult? extractionRun,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.StoreId == Guid.Empty)
            throw new ArgumentException("StoreId is required.", nameof(context));
        if (context.DeviceId == Guid.Empty)
            throw new ArgumentException("DeviceId is required.", nameof(context));

        CaptureExtractedDocument? extracted = null;
        var extractionErrors = new List<CaptureExtractionError>();
        if (extractionRun?.ResultDocument is { ValueKind: JsonValueKind.Object } doc)
        {
            try
            {
                extracted = CaptureFolderResultParser.Parse(doc);
                extractionErrors.AddRange(extracted.Errors);
            }
            catch (InvalidDataException ex)
            {
                extractionErrors.Add(new CaptureExtractionError("EXTRACTION_PARSE_ERROR", ex.Message, inspection?.CaptureFolderPath));
            }
        }
        else if (extractionRun is not null && extractionRun.Disposition != CaptureExtractionDisposition.Completed)
        {
            extractionErrors.Add(new CaptureExtractionError(
                extractionRun.Code,
                extractionRun.Detail ?? "Extractor did not emit a valid capture-folder-result-v1 document.",
                inspection?.CaptureFolderPath));
        }

        var identitySeed = ResolveIdentitySeed(context, inspection, extracted);
        var invoiceId = string.IsNullOrWhiteSpace(context.InvoiceId)
            ? DeterministicGuidString($"invoice:{context.StoreId:N}:{identitySeed}")
            : context.InvoiceId.Trim();
        var operationId = string.IsNullOrWhiteSpace(context.OperationId)
            ? $"capture:{identitySeed}"
            : context.OperationId.Trim();
        var publicToken = string.IsNullOrWhiteSpace(context.PublicToken)
            ? DeterministicPublicToken($"{context.StoreId:N}:{identitySeed}")
            : context.PublicToken.Trim();
        var deviceIdStr = context.DeviceId.ToString("D");
        var storeIdStr = context.StoreId.ToString("D");

        var capturedAtRaw = inspection?.Report?.CapturedAtIso
                            ?? extracted?.CapturedAt
                            ?? (now ?? DateTimeOffset.UtcNow).ToString("O", CultureInfo.InvariantCulture);
        var capturedAtProvenance = inspection?.Report?.CapturedAtIso is not null || extracted?.CapturedAt is not null
            ? "captured"
            : "derived";

        var intakeValid = inspection is null || inspection.IsValid;
        var hasCandidateLines = extracted is { LineItems.Count: > 0 };
        var state = intakeValid && hasCandidateLines ? "EXTRACTED" : "DRAFT";

        var attentionMap = new Dictionary<string, (string RawValue, List<string> Reasons)>(StringComparer.Ordinal);
        AddAttention(attentionMap, "customer.phone", "", "OPERATOR_PHONE_REQUIRED");

        var lineItemsNode = new JsonArray();
        var lineProvenanceNode = new JsonArray();
        var whatIReadLines = new List<CaptureWhatIReadLine>();
        long subtotal = 0;
        var hasAnyCapturedTotal = false;

        var pageStatusByIndex = extracted?.Pages
            .GroupBy(static p => p.PageIndex)
            .ToDictionary(static g => g.Key, static g => g.First().E02Status)
            ?? new Dictionary<int, string?>();

        if (hasCandidateLines)
        {
            var items = extracted!.LineItems;
            for (var i = 0; i < items.Count; i++)
            {
                var li = items[i];
                pageStatusByIndex.TryGetValue(li.SourcePageIndex, out var pageE02Status);
                var pageNeedsReview = string.Equals(pageE02Status, "REVIEW", StringComparison.Ordinal) ||
                                      string.Equals(pageE02Status, "BLOCKED", StringComparison.Ordinal);

                // Product name
                string productName;
                string productProv;
                var providedName = context.LineProductNames is not null && i < context.LineProductNames.Count
                    ? context.LineProductNames[i]
                    : null;

                if (li.ProductName is { RawValue: { } rawProdName, Status: "EXTRACTED" } &&
                    !string.IsNullOrWhiteSpace(rawProdName))
                {
                    productName = rawProdName.Trim();
                    productProv = pageNeedsReview ? "untrusted" : "captured";
                    if (pageNeedsReview)
                        AddAttention(attentionMap, $"line_items/{i}/product_name", productName, $"PAGE_{pageE02Status}_REVIEW_REQUIRED");
                }
                else if (!string.IsNullOrWhiteSpace(providedName))
                {
                    productName = providedName.Trim();
                    productProv = "untrusted";
                    AddAttention(attentionMap, $"line_items/{i}/product_name", productName, "PRODUCT_NAME_CONFIRMATION_REQUIRED");
                }
                else
                {
                    productName = $"ردیف {i + 1} (نیاز به نام کالا)";
                    productProv = "unavailable";
                    AddAttention(attentionMap, $"line_items/{i}/product_name", "", "PRODUCT_NAME_REQUIRED");
                }

                // Quantity millis
                long qtyMillis;
                string qtyProv;
                var qtyCell = li.Quantity;
                var qtyParsed = qtyCell is not null && PersianNumberParser.TryParsePositiveQuantityMillis(qtyCell.RawValue, out qtyMillis);
                if (!qtyParsed)
                    qtyMillis = 1000L;

                if (qtyParsed && string.Equals(qtyCell?.Status, "EXTRACTED", StringComparison.Ordinal) && !pageNeedsReview)
                {
                    qtyProv = "captured";
                }
                else if (qtyParsed)
                {
                    qtyProv = "untrusted";
                    AddAttention(
                        attentionMap,
                        $"line_items/{i}/quantity_millis",
                        qtyCell?.RawValue ?? string.Empty,
                        pageNeedsReview ? $"PAGE_{pageE02Status}_REVIEW_REQUIRED" : $"CELL_STATUS_{qtyCell?.Status ?? "UNAVAILABLE"}");
                }
                else
                {
                    qtyProv = "unavailable";
                    AddAttention(
                        attentionMap,
                        $"line_items/{i}/quantity_millis",
                        qtyCell?.RawValue ?? string.Empty,
                        "QUANTITY_UNAVAILABLE_OR_INVALID");
                }

                // Line total
                long lineTotal;
                string lineTotalProv;
                var totalCell = li.LineTotal;
                var totalParsed = totalCell is not null && PersianNumberParser.TryParseNonNegativeInt64(totalCell.RawValue, out lineTotal);
                if (!totalParsed)
                    lineTotal = 0L;

                if (totalParsed && string.Equals(totalCell?.Status, "EXTRACTED", StringComparison.Ordinal) && !pageNeedsReview)
                {
                    lineTotalProv = "captured";
                    hasAnyCapturedTotal = true;
                }
                else if (totalParsed)
                {
                    lineTotalProv = "untrusted";
                    hasAnyCapturedTotal = true;
                    AddAttention(
                        attentionMap,
                        $"line_items/{i}/line_total",
                        totalCell?.RawValue ?? string.Empty,
                        pageNeedsReview ? $"PAGE_{pageE02Status}_REVIEW_REQUIRED" : $"CELL_STATUS_{totalCell?.Status ?? "UNAVAILABLE"}");
                }
                else
                {
                    lineTotalProv = "unavailable";
                    AddAttention(
                        attentionMap,
                        $"line_items/{i}/line_total",
                        totalCell?.RawValue ?? string.Empty,
                        "LINE_TOTAL_UNAVAILABLE_OR_INVALID");
                }

                // Unit price
                long unitPrice;
                string unitPriceProv;
                var upCell = li.UnitPrice;
                if (upCell is not null &&
                    string.Equals(upCell.Status, "EXTRACTED", StringComparison.Ordinal) &&
                    PersianNumberParser.TryParseNonNegativeInt64(upCell.RawValue, out var explicitUnitPrice) &&
                    !pageNeedsReview)
                {
                    unitPrice = explicitUnitPrice;
                    unitPriceProv = "captured";
                }
                else if (qtyParsed && totalParsed && qtyMillis > 0 && TryDeriveExactUnitPrice(lineTotal, qtyMillis, out var derivedUnitPrice))
                {
                    unitPrice = derivedUnitPrice;
                    unitPriceProv = "derived";
                    AddAttention(
                        attentionMap,
                        $"line_items/{i}/unit_price",
                        derivedUnitPrice.ToString(CultureInfo.InvariantCulture),
                        "DERIVED_UNIT_PRICE_CONFIRMATION_REQUIRED");
                }
                else
                {
                    unitPrice = 0L;
                    unitPriceProv = "unavailable";
                    AddAttention(
                        attentionMap,
                        $"line_items/{i}/unit_price",
                        upCell?.RawValue ?? string.Empty,
                        "UNIT_PRICE_UNAVAILABLE");
                }

                subtotal = checked(subtotal + lineTotal);

                lineItemsNode.Add(new JsonObject
                {
                    ["product_name"] = productName,
                    ["quantity_millis"] = qtyMillis,
                    ["unit_price"] = unitPrice,
                    ["line_total"] = lineTotal,
                });

                lineProvenanceNode.Add(new JsonObject
                {
                    ["product_name"] = productProv,
                    ["quantity_millis"] = qtyProv,
                    ["unit_price"] = unitPriceProv,
                    ["line_total"] = lineTotalProv,
                });

                var rowNeedsAttention = attentionMap.Keys.Any(k => k.StartsWith($"line_items/{i}/", StringComparison.Ordinal));
                var rowStatus = totalCell?.Status ?? qtyCell?.Status ?? "UNAVAILABLE";
                var rowConf = MinConfidence(qtyCell?.Confidence, totalCell?.Confidence);
                whatIReadLines.Add(new CaptureWhatIReadLine(
                    i,
                    productName,
                    qtyCell?.RawValue ?? "—",
                    qtyParsed ? qtyMillis : null,
                    upCell?.RawValue ?? (unitPriceProv == "derived" ? unitPrice.ToString("N0", CultureInfo.InvariantCulture) : "—"),
                    unitPrice > 0 || unitPriceProv == "captured" ? unitPrice : null,
                    totalCell?.RawValue ?? "—",
                    totalParsed ? lineTotal : null,
                    rowStatus,
                    rowConf,
                    rowNeedsAttention));
            }
        }
        else
        {
            // Fail-closed placeholder draft line when intake or extraction yielded zero lines
            const string placeholderName = "ردیف ۱ (بررسی دستی تصویر چاپ)";
            lineItemsNode.Add(new JsonObject
            {
                ["product_name"] = placeholderName,
                ["quantity_millis"] = 1000L,
                ["unit_price"] = 0L,
                ["line_total"] = 0L,
            });
            lineProvenanceNode.Add(new JsonObject
            {
                ["product_name"] = "unavailable",
                ["quantity_millis"] = "unavailable",
                ["unit_price"] = "unavailable",
                ["line_total"] = "unavailable",
            });

            var intakeReason = !intakeValid && inspection is { Problems.Count: > 0 }
                ? $"INTAKE_INVALID:{inspection.Problems[0].Code}"
                : extractionErrors.Count > 0
                    ? $"EXTRACTION_BLOCKED:{extractionErrors[0].Code}"
                    : "NO_LINE_ITEMS_EXTRACTED";

            AddAttention(attentionMap, "line_items/0/product_name", "", intakeReason);
            AddAttention(attentionMap, "line_items/0/quantity_millis", "", "MANUAL_ENTRY_REQUIRED");
            AddAttention(attentionMap, "line_items/0/unit_price", "", "MANUAL_ENTRY_REQUIRED");
            AddAttention(attentionMap, "line_items/0/line_total", "", "MANUAL_ENTRY_REQUIRED");

            whatIReadLines.Add(new CaptureWhatIReadLine(
                0,
                placeholderName,
                "—",
                null,
                "—",
                null,
                "—",
                null,
                extracted?.Status ?? "DRAFT",
                null,
                NeedsAttention: true));
        }

        // Payable resolution
        long payable = subtotal;
        var payableProv = "unavailable";
        string? payableRawText = null;
        if (extracted is { PayableCandidates.Count: == 1 } &&
            extracted.PayableCandidates[0] is var payableCell &&
            PersianNumberParser.TryParseNonNegativeInt64(payableCell.RawValue, out var parsedPayable))
        {
            payableRawText = payableCell.RawValue;
            payable = parsedPayable;
            payableProv = string.Equals(payableCell.Status, "EXTRACTED", StringComparison.Ordinal)
                ? "captured"
                : "untrusted";

            if (!string.Equals(payableCell.Status, "EXTRACTED", StringComparison.Ordinal))
                AddAttention(attentionMap, "summary/payable", payableRawText ?? string.Empty, $"CELL_STATUS_{payableCell.Status}");

            if (payable != subtotal)
            {
                AddAttention(
                    attentionMap,
                    "summary/payable",
                    payableRawText ?? payable.ToString(CultureInfo.InvariantCulture),
                    $"PAYABLE_SUBTOTAL_MISMATCH: expected {subtotal.ToString(CultureInfo.InvariantCulture)}");
            }
        }
        else
        {
            if (extracted is { PayableCandidates.Count: > 1 })
            {
                payableRawText = string.Join(" | ", extracted.PayableCandidates.Select(static c => c.RawValue ?? "?"));
                AddAttention(attentionMap, "summary/payable", payableRawText, "MULTIPLE_PAYABLE_CANDIDATES");
            }
            else if (!hasCandidateLines || !intakeValid)
            {
                AddAttention(attentionMap, "summary/payable", "", "PAYABLE_UNAVAILABLE");
            }
            else
            {
                AddAttention(attentionMap, "summary/payable", "", "PAYABLE_CANDIDATE_MISSING");
            }
        }

        if (extracted is not null &&
            (extracted.E06ReasonCodes.Contains("CURRENCY_UNIT_UNCONFIRMED", StringComparer.Ordinal) ||
             string.Equals(extracted.CurrencyUnitStatus, "UNCONFIRMED", StringComparison.OrdinalIgnoreCase)))
        {
            AddAttention(
                attentionMap,
                "summary/payable",
                payableRawText ?? payable.ToString(CultureInfo.InvariantCulture),
                "CURRENCY_UNIT_UNCONFIRMED");
        }

        var attentionArray = new JsonArray();
        foreach (var (field, entry) in attentionMap)
        {
            attentionArray.Add(new JsonObject
            {
                ["field"] = field,
                ["raw_value"] = entry.RawValue,
                ["reason"] = string.Join("; ", entry.Reasons.Distinct(StringComparer.Ordinal)),
            });
        }

        var stateHistory = new JsonArray();
        if (state == "EXTRACTED")
        {
            stateHistory.Add(new JsonObject
            {
                ["from_state"] = "DRAFT",
                ["to_state"] = "EXTRACTED",
                ["operation_id"] = operationId,
                ["device_id"] = deviceIdStr,
            });
        }

        var imageSha256 = SelectValidSha256(
            extracted?.Pages.FirstOrDefault()?.Sha256,
            inspection?.Pages.FirstOrDefault()?.Sha256,
            inspection?.Spl?.Sha256);

        var root = new JsonObject
        {
            ["schema_version"] = "0",
            ["mode"] = "B",
            ["invoice_id"] = invoiceId,
            ["display_number"] = null,
            ["operation_id"] = operationId,
            ["device_id"] = deviceIdStr,
            ["public_token"] = publicToken,
            ["store_id"] = storeIdStr,
            ["customer"] = new JsonObject
            {
                ["phone"] = "+980000000000",
                ["phone_status"] = "claimed",
            },
            ["state"] = state,
            ["state_history"] = stateHistory,
            ["line_items"] = lineItemsNode,
            ["summary"] = new JsonObject
            {
                ["subtotal"] = subtotal,
                ["extra_amounts"] = new JsonObject(),
                ["payable"] = payable,
            },
            ["attention_cells"] = attentionArray,
            ["header"] = new JsonObject
            {
                ["captured_at"] = capturedAtRaw,
                ["printed_date"] = string.IsNullOrWhiteSpace(context.PrintedDate) ? null : context.PrintedDate.Trim(),
                ["printed_time"] = string.IsNullOrWhiteSpace(context.PrintedTime) ? null : context.PrintedTime.Trim(),
                ["invoice_number"] = string.IsNullOrWhiteSpace(context.PrintedInvoiceNumber) ? null : context.PrintedInvoiceNumber.Trim(),
            },
            ["provenance"] = new JsonObject
            {
                ["image_sha256"] = imageSha256,
                ["template_library_id"] = extracted?.TemplateLibraryId is { } tpl && !string.IsNullOrWhiteSpace(tpl) ? tpl : null,
                ["engine_version"] = string.IsNullOrWhiteSpace(context.EngineVersion) ? "kandoo-capture-v1" : context.EngineVersion.Trim(),
                ["fields"] = new JsonObject
                {
                    ["invoice_id"] = "derived",
                    ["display_number"] = "unavailable",
                    ["operation_id"] = "derived",
                    ["device_id"] = "native",
                    ["public_token"] = "derived",
                    ["store_id"] = "native",
                    ["customer"] = new JsonObject
                    {
                        ["phone"] = "unavailable",
                        ["phone_status"] = "unavailable",
                    },
                    ["line_items"] = lineProvenanceNode,
                    ["summary"] = new JsonObject
                    {
                        ["subtotal"] = hasAnyCapturedTotal ? "derived" : "unavailable",
                        ["extra_amounts"] = new JsonObject(),
                        ["payable"] = payableProv,
                    },
                    ["header"] = new JsonObject
                    {
                        ["captured_at"] = capturedAtProvenance,
                        ["printed_date"] = string.IsNullOrWhiteSpace(context.PrintedDate) ? "unavailable" : "untrusted",
                        ["printed_time"] = string.IsNullOrWhiteSpace(context.PrintedTime) ? "unavailable" : "untrusted",
                        ["invoice_number"] = string.IsNullOrWhiteSpace(context.PrintedInvoiceNumber) ? "unavailable" : "untrusted",
                    },
                },
            },
        };

        var canonicalJson = root.ToJsonString(CompactOptions);
        var consumed = CanonicalInvoiceConsumer.ConsumeForReview(canonicalJson);
        var reviewId = CanonicalJson.Sha256Hex(consumed.Source);

        var pageEvidences = BuildPageEvidence(inspection, extracted);
        var xpsLines = string.IsNullOrWhiteSpace(context.XpsText)
            ? Array.Empty<string>()
            : context.XpsText.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var attentionSummaries = FormatAttentionSummaries(attentionMap, inspection?.Problems ?? Array.Empty<CaptureIntakeProblem>());

        var evidence = new CaptureReviewEvidence(
            reviewId,
            consumed.QueueStatus,
            consumed.SourceState,
            inspection?.CaptureFolderPath ?? extracted?.CaptureFolder ?? string.Empty,
            inspection?.Report?.SpoolJobId ?? extracted?.SpoolJobId,
            inspection?.Spl?.FileName,
            inspection?.Spl?.Sha256 ?? extracted?.SplSha256,
            capturedAtRaw,
            extracted?.Status,
            extracted?.Verdict,
            extracted?.CurrencyUnit,
            extracted?.CurrencyUnitStatus,
            extracted?.E06ReasonCodes ?? Array.Empty<string>(),
            pageEvidences,
            whatIReadLines,
            payableRawText,
            payable,
            subtotal,
            xpsLines,
            inspection?.Problems ?? Array.Empty<CaptureIntakeProblem>(),
            extractionErrors,
            attentionSummaries);

        return new CaptureReviewPackage(reviewId, canonicalJson, consumed, evidence);
    }

    private static IReadOnlyList<CapturePageEvidence> BuildPageEvidence(
        CaptureIntakeInspection? inspection,
        CaptureExtractedDocument? extracted)
    {
        var inspByIndex = inspection?.Pages.ToDictionary(static p => p.PageIndex)
                          ?? new Dictionary<int, CapturePngPageArtifact>();

        if (extracted is { Pages.Count: > 0 })
        {
            return extracted.Pages.Select(p =>
            {
                inspByIndex.TryGetValue(p.PageIndex, out var localPage);
                return new CapturePageEvidence(
                    p.PageIndex,
                    p.FileName,
                    localPage?.FullPath,
                    p.Width,
                    p.Height,
                    p.Sha256,
                    p.E02Status,
                    p.E02Reason,
                    p.E05Status,
                    p.E05TemplateId,
                    p.E05ConfidenceScore);
            }).ToList();
        }

        return inspection?.Pages.Select(static p => new CapturePageEvidence(
            p.PageIndex,
            p.FileName,
            p.FullPath,
            p.Width,
            p.Height,
            p.Sha256,
            null,
            null,
            null,
            null,
            null)).ToList() ?? [];
    }

    private static IReadOnlyList<string> FormatAttentionSummaries(
        IReadOnlyDictionary<string, (string RawValue, List<string> Reasons)> attentionMap,
        IReadOnlyList<CaptureIntakeProblem> intakeProblems)
    {
        var summaries = new List<string>();
        foreach (var (field, entry) in attentionMap)
        {
            if (field == "customer.phone")
                summaries.Add("شماره تماس مشتری باید توسط اپراتور وارد شود.");
            else if (field.EndsWith("/product_name", StringComparison.Ordinal))
                summaries.Add($"نام کالا در {field} نیاز به انتخاب یا تأیید دارد.");
            else if (field.EndsWith("/unit_price", StringComparison.Ordinal))
                summaries.Add($"قیمت واحد در {field} نیاز به تأیید دارد.");
            else if (field == "summary/payable" && entry.Reasons.Contains("CURRENCY_UNIT_UNCONFIRMED", StringComparer.Ordinal))
                summaries.Add("واحد پولی فاکتور (ریال/تومان) تأیید نشده است؛ مبلغ قابل پرداخت را بررسی کنید.");
            else
                summaries.Add($"فیلد {field} نیاز به بررسی دارد ({string.Join(", ", entry.Reasons)}).");
        }

        summaries.AddRange(CaptureIntakeValidator.FormatPlainTriageReasons(intakeProblems));
        return summaries.Distinct(StringComparer.Ordinal).ToList();
    }

    private static void AddAttention(
        Dictionary<string, (string RawValue, List<string> Reasons)> map,
        string field,
        string rawValue,
        string reason)
    {
        if (!map.TryGetValue(field, out var existing))
        {
            map[field] = (rawValue, new List<string> { reason });
            return;
        }

        if (string.IsNullOrEmpty(existing.RawValue) && !string.IsNullOrEmpty(rawValue))
            map[field] = (rawValue, existing.Reasons);

        existing.Reasons.Add(reason);
    }

    private static bool TryDeriveExactUnitPrice(long lineTotal, long quantityMillis, out long unitPrice)
    {
        unitPrice = 0;
        if (quantityMillis <= 0 || lineTotal < 0)
            return false;

        try
        {
            var scaled = checked(lineTotal * 1000L);
            if (scaled % quantityMillis != 0)
                return false;

            unitPrice = scaled / quantityMillis;
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static double? MinConfidence(double? left, double? right)
    {
        if (left.HasValue && right.HasValue)
            return Math.Min(left.Value, right.Value);
        return left ?? right;
    }

    private static string? SelectValidSha256(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;
            var lower = candidate.Trim().ToLowerInvariant();
            if (lower.Length == 64 && lower.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
                return lower;
        }

        return null;
    }

    private static string ResolveIdentitySeed(
        CaptureProducerContext context,
        CaptureIntakeInspection? inspection,
        CaptureExtractedDocument? extracted)
    {
        if (inspection?.Spl?.Sha256 is { } splSha && !string.IsNullOrWhiteSpace(splSha))
            return splSha;
        if (extracted?.SplSha256 is { } extSha && !string.IsNullOrWhiteSpace(extSha))
            return extSha;
        if (inspection?.Report?.SpoolJobId is { } repJob && !string.IsNullOrWhiteSpace(repJob))
            return repJob;
        if (extracted?.SpoolJobId is { } extJob && !string.IsNullOrWhiteSpace(extJob))
            return extJob;
        if (inspection?.CaptureFolderPath is { } folderPath && !string.IsNullOrWhiteSpace(folderPath))
            return folderPath;
        return context.OperationId ?? Guid.NewGuid().ToString("N");
    }

    private static string DeterministicGuidString(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        return new Guid(guidBytes).ToString("D");
    }

    private static string DeterministicPublicToken(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("kandoo:modeb:public_token:" + seed));
        return Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
