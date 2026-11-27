using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Kandoo.App;

public enum ReturnRefundMethod
{
    Cash,
    Card,
    CreditDeduction
}

public sealed record InvoiceReturnLineCandidate(
    string LineKey,
    string Description,
    long SoldQuantityMillis,
    string? ProductId = null)
{
    public string DisplayLabel =>
        $"{Description} × {HubConnectionResolver.FormatStockMillis(SoldQuantityMillis)}";
}

public sealed record ReturnSubmissionInput(
    string InvoiceNumber,
    long ReturnQuantityMillis,
    string Reason,
    ReturnRefundMethod RefundMethod,
    string? ProductId = null,
    string? ProductName = null,
    long? CurrentProductStockMillis = null,
    long? ProductBaseVersion = null,
    string? OperationId = null);

public sealed record ReturnEventRecord(
    string EventId,
    Guid StoreId,
    string InvoiceNumber,
    string? ProductId,
    string? ProductName,
    long ReturnQuantityMillis,
    string Reason,
    string RefundMethodCode,
    DateTimeOffset RecordedAtUtc,
    bool SyncedToHubReturnEndpoint,
    bool StockAdjustedInHub)
{
    public string RefundDisplay => RefundMethodCode switch
    {
        "CARD" => "کارت",
        "CREDIT_DEDUCTION" => "کسر از طلب",
        _ => "نقدی"
    };

    public string QuantityDisplay => HubConnectionResolver.FormatStockMillis(ReturnQuantityMillis);
}

public sealed record ReturnSubmissionOutcome(
    ReturnEventRecord Event,
    string UserMessage);

/// <summary>
/// Data source for append-only invoice return/cancellation events (<c>Return.dc.html</c>, Decision 19).
/// Never deletes an issued invoice: attempts Hub return endpoint first, and if HTTP 404,
/// records the immutable return event locally and adjusts linked product stock on the Hub.
/// </summary>
public sealed class HubReturnsDataSource
{
    public const string LedgerFileName = "kandoo.return-events.v1.jsonl";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    private readonly HttpClient _client;
    private readonly HubConnectionSettings _settings;
    private readonly string _baseUrl;
    private readonly string _ledgerFolder;

    public HubReturnsDataSource(
        HttpClient client,
        HubConnectionSettings settings,
        string? localAppDataFolder = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _baseUrl = HubApiUtilities.ValidatedBaseUrl(settings);
        if (string.IsNullOrWhiteSpace(settings.SessionToken))
        {
            throw new ArgumentException("A Hub session token is required.", nameof(settings));
        }

        _ledgerFolder = string.IsNullOrWhiteSpace(localAppDataFolder)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localAppDataFolder;
    }

    public static bool TryValidateReturnForm(
        string? invoiceNumberText,
        string? quantityText,
        string? reasonText,
        long? maxSoldQuantityMillis,
        out string normalizedInvoiceNumber,
        out long returnQuantityMillis,
        out string persianError)
    {
        normalizedInvoiceNumber = string.Empty;
        returnQuantityMillis = 0;
        persianError = string.Empty;

        var rawInvoice = HubConnectionResolver.NormalizeAsciiDigits(invoiceNumberText);
        if (string.IsNullOrWhiteSpace(rawInvoice))
        {
            persianError = "شمارهٔ فاکتور الزامی است.";
            return false;
        }

        normalizedInvoiceNumber = rawInvoice;

        var rawQty = HubConnectionResolver.NormalizeAsciiDigits(quantityText);
        if (string.IsNullOrWhiteSpace(rawQty) ||
            !decimal.TryParse(rawQty, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var qtyUnits) ||
            qtyUnits <= 0)
        {
            persianError = "تعداد برگشتی باید عددی بزرگ‌تر از صفر باشد.";
            return false;
        }

        var scaled = decimal.Round(qtyUnits * 1000m, 0, MidpointRounding.AwayFromZero);
        if (scaled <= 0 || scaled > long.MaxValue)
        {
            persianError = "تعداد برگشتی خارج از محدوده مجاز است.";
            return false;
        }

        returnQuantityMillis = (long)scaled;

        if (maxSoldQuantityMillis is long soldMillis && soldMillis > 0 && returnQuantityMillis > soldMillis)
        {
            persianError = $"تعداد برگشتی نمی‌تواند بیشتر از تعداد فروخته‌شده ({HubConnectionResolver.FormatStockMillis(soldMillis)}) باشد.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(reasonText))
        {
            persianError = "دلیل برگشت الزامی است.";
            return false;
        }

        return true;
    }

    public async Task<IReadOnlyList<InvoiceReturnLineCandidate>> TryLoadInvoiceLinesAsync(
        string invoiceNumber,
        CancellationToken cancellationToken = default)
    {
        var normalized = HubConnectionResolver.NormalizeAsciiDigits(invoiceNumber);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Array.Empty<InvoiceReturnLineCandidate>();
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_baseUrl}/v1/stores/{_settings.StoreId:D}/invoices/{Uri.EscapeDataString(normalized)}");
        HubApiUtilities.Authorize(request, _settings);
        using var response = await _client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<InvoiceReturnLineCandidate>();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Hub invoice lookup failed with HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ParseInvoiceLines(doc.RootElement);
    }

    public async Task<ReturnSubmissionOutcome> SubmitReturnAsync(
        ReturnSubmissionInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.InvoiceNumber))
        {
            throw new ArgumentException("Invoice number is required.", nameof(input));
        }

        if (input.ReturnQuantityMillis <= 0)
        {
            throw new ArgumentException("Return quantity must be positive.", nameof(input));
        }

        if (string.IsNullOrWhiteSpace(input.Reason))
        {
            throw new ArgumentException("Return reason is required.", nameof(input));
        }

        var operationId = string.IsNullOrWhiteSpace(input.OperationId)
            ? $"inv-return-{Guid.NewGuid():N}"
            : input.OperationId.Trim();
        var refundCode = ToRefundCode(input.RefundMethod);

        var body = new Dictionary<string, object?>
        {
            ["operation_id"] = operationId,
            ["invoice_number"] = input.InvoiceNumber.Trim(),
            ["product_id"] = input.ProductId,
            ["quantity_millis"] = input.ReturnQuantityMillis,
            ["reason"] = input.Reason.Trim(),
            ["refund_method"] = refundCode
        };

        bool syncedToHubReturnEndpoint = false;
        bool stockAdjustedInHub = false;

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_baseUrl}/v1/stores/{_settings.StoreId:D}/invoices/{Uri.EscapeDataString(input.InvoiceNumber.Trim())}/returns");
        HubApiUtilities.Authorize(request, _settings);
        request.Content = HubApiUtilities.JsonBody(body);

        using var response = await _client.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            syncedToHubReturnEndpoint = true;
            stockAdjustedInHub = true;
        }
        else if (response.StatusCode != HttpStatusCode.NotFound)
        {
            var errText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Hub return submission failed with HTTP {(int)response.StatusCode}: {errText}.");
        }
        else if (!string.IsNullOrWhiteSpace(input.ProductId) &&
                 input.CurrentProductStockMillis is long currentStock &&
                 input.ProductBaseVersion is long baseVersion &&
                 baseVersion > 0)
        {
            var editor = new HubProductEditorDataSource(_client, _settings);
            var nextStock = checked(currentStock + input.ReturnQuantityMillis);
            await editor.UpdateProductDetailedAsync(
                new ProductEditorUpdateRequest(
                    ProductId: input.ProductId!,
                    BaseVersion: baseVersion,
                    StockMillis: nextStock,
                    Specification: $"برگشت فاکتور {input.InvoiceNumber.Trim()}: {input.Reason.Trim()}",
                    IncludeSpecification: true,
                    OperationId: $"{operationId}-stock"),
                cancellationToken);
            stockAdjustedInHub = true;
        }

        var record = new ReturnEventRecord(
            EventId: operationId,
            StoreId: _settings.StoreId,
            InvoiceNumber: input.InvoiceNumber.Trim(),
            ProductId: input.ProductId,
            ProductName: input.ProductName,
            ReturnQuantityMillis: input.ReturnQuantityMillis,
            Reason: input.Reason.Trim(),
            RefundMethodCode: refundCode,
            RecordedAtUtc: DateTimeOffset.UtcNow,
            SyncedToHubReturnEndpoint: syncedToHubReturnEndpoint,
            StockAdjustedInHub: stockAdjustedInHub);

        AppendLocalRecord(record);

        var message = syncedToHubReturnEndpoint
            ? $"رویداد برگشت برای فاکتور {record.InvoiceNumber} در هاب ثبت شد (بدون حذف فاکتور اصلی)."
            : stockAdjustedInHub
                ? $"رویداد برگشت فاکتور {record.InvoiceNumber} در دفتر رویداد ثبت شد و موجودی کالای انتخابی در هاب افزایش یافت (مسیر اختصاصی /returns در هاب هنوز فعال نیست)."
                : $"رویداد برگشت فاکتور {record.InvoiceNumber} در دفتر محلی ثبت شد (مسیر اختصاصی /returns در هاب هنوز HTTP 404 است؛ فاکتور اصلی دست‌نخورده باقی ماند).";

        return new ReturnSubmissionOutcome(record, message);
    }

    public IReadOnlyList<ReturnEventRecord> ReadLocalRecords()
    {
        return ReadLocalRecords(_ledgerFolder, _settings.StoreId);
    }

    public static IReadOnlyList<ReturnEventRecord> ReadLocalRecords(string folderPath, Guid? storeId = null)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return Array.Empty<ReturnEventRecord>();
        }

        var path = Path.Combine(folderPath, LedgerFileName);
        if (!File.Exists(path))
        {
            return Array.Empty<ReturnEventRecord>();
        }

        var results = new List<ReturnEventRecord>();
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                var item = JsonSerializer.Deserialize<ReturnEventRecord>(line, JsonOptions);
                if (item is not null && (storeId is null || item.StoreId == storeId.Value))
                {
                    results.Add(item);
                }
            }
            catch (JsonException)
            {
                // Skip malformed trailing line without losing valid append-only entries.
            }
        }

        results.Reverse();
        return results;
    }

    public static IReadOnlyList<InvoiceReturnLineCandidate> ParseInvoiceLines(JsonElement root)
    {
        JsonElement invoice = root;
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("invoice", out var nested) &&
            nested.ValueKind == JsonValueKind.Object)
        {
            invoice = nested;
        }

        if (invoice.ValueKind != JsonValueKind.Object ||
            !invoice.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<InvoiceReturnLineCandidate>();
        }

        var lines = new List<InvoiceReturnLineCandidate>();
        var idx = 0;
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            idx++;
            var key = OptionalString(item, "line_id") ?? OptionalString(item, "item_id") ?? $"line-{idx}";
            var desc = OptionalString(item, "description") ?? OptionalString(item, "name") ?? $"ردیف {idx}";
            var qty = OptionalInt64(item, "quantity_millis") ?? 1_000;
            var productId = OptionalString(item, "product_id");
            lines.Add(new InvoiceReturnLineCandidate(key, desc, qty, productId));
        }

        return lines;
    }

    public static string ToRefundCode(ReturnRefundMethod method) => method switch
    {
        ReturnRefundMethod.Card => "CARD",
        ReturnRefundMethod.CreditDeduction => "CREDIT_DEDUCTION",
        _ => "CASH"
    };

    private void AppendLocalRecord(ReturnEventRecord record)
    {
        Directory.CreateDirectory(_ledgerFolder);
        var path = Path.Combine(_ledgerFolder, LedgerFileName);
        var jsonLine = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;
        File.AppendAllText(path, jsonLine, Encoding.UTF8);
    }

    private static string? OptionalString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var val) || val.ValueKind != JsonValueKind.String)
            return null;
        var text = val.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static long? OptionalInt64(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var val) || val.ValueKind != JsonValueKind.Number)
            return null;
        return val.TryGetInt64(out var n) ? n : null;
    }
}
