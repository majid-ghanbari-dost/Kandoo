using System.Net;
using System.Text.Json;

namespace Kandoo.App;

public enum CustomerDataOrigin
{
    HubCustomersEndpoint,
    HubInvoicesProjection,
    HubEndpointUnavailable,
    SyntheticPreview
}

public sealed record CustomerInvoiceSummary(
    string InvoiceNumber,
    long AmountRials,
    string? Status = null)
{
    public string AmountDisplay => FormattableString.Invariant($"{AmountRials:N0} ریال");
}

/// <summary>Customer row for display in the master table and detail side panel.</summary>
public sealed record CustomerRow(
    string CustomerId,
    string Name,
    string Phone,
    long TotalPurchasesRials,
    long OutstandingRials = 0,
    IReadOnlyList<CustomerInvoiceSummary>? RecentInvoices = null,
    bool IsSyntheticDemo = false)
{
    public string TotalDisplay => FormattableString.Invariant($"{TotalPurchasesRials:N0} ریال");
    public string OutstandingDisplay => FormattableString.Invariant($"{OutstandingRials:N0} ریال");
    public IReadOnlyList<CustomerInvoiceSummary> Invoices =>
        RecentInvoices ?? Array.Empty<CustomerInvoiceSummary>();
}

public sealed record CustomerDirectoryResult(
    IReadOnlyList<CustomerRow> Customers,
    CustomerDataOrigin Origin,
    string? StatusNote = null);

/// <summary>Data source for customer directory operations against the Hub.</summary>
public sealed class HubCustomersDataSource
{
    private readonly HttpClient _client;
    private readonly HubConnectionSettings _settings;
    private readonly string _baseUrl;

    public HubCustomersDataSource(HttpClient client, HubConnectionSettings settings)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _baseUrl = HubApiUtilities.ValidatedBaseUrl(settings);
    }

    public async Task<IReadOnlyList<CustomerRow>> LoadCustomersAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{_baseUrl}/v1/stores/{_settings.StoreId:D}/customers");
        HubApiUtilities.Authorize(request, _settings);
        using var response = await _client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Hub customers request failed with HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return CustomerMapper.Map(document.RootElement);
    }

    /// <summary>
    /// Queries <c>/customers</c> first; if that route is not registered on the Hub (HTTP 404),
    /// falls back to aggregating customers from <c>/invoices</c> (C-99). If neither read route
    /// is available yet, returns <see cref="CustomerDataOrigin.HubEndpointUnavailable"/> honestly
    /// without inventing data.
    /// </summary>
    public async Task<CustomerDirectoryResult> LoadDirectoryAsync(
        CancellationToken cancellationToken = default)
    {
        using var customersRequest = new HttpRequestMessage(
            HttpMethod.Get, $"{_baseUrl}/v1/stores/{_settings.StoreId:D}/customers");
        HubApiUtilities.Authorize(customersRequest, _settings);
        using var customersResponse = await _client.SendAsync(customersRequest, cancellationToken);
        if (customersResponse.IsSuccessStatusCode)
        {
            await using var stream = await customersResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return new CustomerDirectoryResult(
                CustomerMapper.Map(document.RootElement),
                CustomerDataOrigin.HubCustomersEndpoint);
        }

        if (customersResponse.StatusCode != HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                $"Hub customers request failed with HTTP {(int)customersResponse.StatusCode}.");
        }

        using var invoicesRequest = new HttpRequestMessage(
            HttpMethod.Get, $"{_baseUrl}/v1/stores/{_settings.StoreId:D}/invoices?limit=100");
        HubApiUtilities.Authorize(invoicesRequest, _settings);
        using var invoicesResponse = await _client.SendAsync(invoicesRequest, cancellationToken);
        if (invoicesResponse.IsSuccessStatusCode)
        {
            await using var stream = await invoicesResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var aggregated = CustomerMapper.AggregateFromInvoices(document.RootElement);
            return new CustomerDirectoryResult(
                aggregated,
                CustomerDataOrigin.HubInvoicesProjection,
                "فهرست مشتری‌ها از فاکتورهای ثبت‌شده در هاب استخراج شد.");
        }

        if (invoicesResponse.StatusCode == HttpStatusCode.NotFound)
        {
            return new CustomerDirectoryResult(
                Array.Empty<CustomerRow>(),
                CustomerDataOrigin.HubEndpointUnavailable,
                "مسیر خواندن مشتری‌ها در هاب هنوز فعال نیست (HTTP 404). برای بررسی چیدمان می‌توانید پیش‌نمایش داده ساختگی را روشن کنید.");
        }

        throw new InvalidOperationException(
            $"Hub invoices fallback request failed with HTTP {(int)invoicesResponse.StatusCode}.");
    }

    /// <summary>
    /// Explicit synthetic/fictional sample rows for UI layout preview only (Decision 30).
    /// Clearly labeled synthetic; never mixed with real Hub responses.
    /// </summary>
    public static IReadOnlyList<CustomerRow> CreateSyntheticPreviewRows() => new[]
    {
        new CustomerRow(
            "synthetic-walk-in",
            "مشتری عمومی بی‌نام (نمونه ساختگی)",
            "—",
            4_850_000,
            0,
            new[]
            {
                new CustomerInvoiceSummary("فاکتور ۱۰۴ (ساختگی)", 2_350_000, "ISSUED"),
                new CustomerInvoiceSummary("فاکتور ۱۰۱ (ساختگی)", 2_500_000, "ISSUED")
            },
            IsSyntheticDemo: true),
        new CustomerRow(
            "synthetic-cust-1",
            "مریم رضایی (نمونه ساختگی)",
            "0912***0101",
            3_200_000,
            450_000,
            new[]
            {
                new CustomerInvoiceSummary("فاکتور ۱۰۵ (ساختگی)", 1_750_000, "ISSUED"),
                new CustomerInvoiceSummary("فاکتور ۹۸ (ساختگی)", 1_450_000, "ISSUED")
            },
            IsSyntheticDemo: true),
        new CustomerRow(
            "synthetic-cust-2",
            "علی محمدی (نمونه ساختگی)",
            "0912***0102",
            1_120_000,
            0,
            new[]
            {
                new CustomerInvoiceSummary("فاکتور ۱۰۲ (ساختگی)", 1_120_000, "ISSUED")
            },
            IsSyntheticDemo: true)
    };
}

public static class CustomerDirectoryFilter
{
    public static IReadOnlyList<CustomerRow> Apply(
        IReadOnlyList<CustomerRow> customers,
        string? searchQuery)
    {
        ArgumentNullException.ThrowIfNull(customers);
        var normalized = HubConnectionResolver.NormalizeSearchDigits(searchQuery);
        if (string.IsNullOrEmpty(normalized))
        {
            return customers;
        }

        return customers
            .Where(row =>
                HubConnectionResolver.NormalizeSearchDigits(row.Name).Contains(normalized, StringComparison.Ordinal) ||
                HubConnectionResolver.NormalizeSearchDigits(row.Phone).Contains(normalized, StringComparison.Ordinal))
            .ToArray();
    }
}

public static class CustomerMapper
{
    public static IReadOnlyList<CustomerRow> Map(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Hub customer response must contain an items array.");
        }

        var rows = new List<CustomerRow>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var customerId = RequiredString(item, "customer_id");
            var name = OptionalString(item, "name") ?? "مشتری عمومی";
            var phone = OptionalString(item, "phone") ?? "—";
            var totalPurchases = OptionalInt64(item, "total_purchases_rials") ?? 0;
            var outstanding = OptionalInt64(item, "outstanding_rials")
                ?? OptionalInt64(item, "receivable_rials")
                ?? 0;
            var recent = ReadRecentInvoices(item);

            rows.Add(new CustomerRow(customerId, name, phone, totalPurchases, outstanding, recent));
        }

        return rows;
    }

    public static IReadOnlyList<CustomerRow> AggregateFromInvoices(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Hub invoices response must contain an items array.");
        }

        var groups = new Dictionary<string, CustomerAccumulator>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var status = (OptionalString(item, "status") ?? "ISSUED").ToUpperInvariant();
            if (status is "CANCELLED" or "CANCELED" or "REVOKED" or "SUPERSEDED" or "DRAFT")
                continue;

            var (customerKey, customerName, customerPhone) = ExtractCustomerIdentity(item);
            var amount = OptionalInt64(item, "amount_rials")
                ?? OptionalInt64(item, "total_rials")
                ?? OptionalInt64(item, "payable_rials")
                ?? 0;
            var remaining = OptionalInt64(item, "remaining_balance_rials")
                ?? OptionalInt64(item, "remaining_balance")
                ?? 0;
            var number = FormatInvoiceNumber(item);

            if (!groups.TryGetValue(customerKey, out var acc))
            {
                acc = new CustomerAccumulator(customerKey, customerName, customerPhone);
                groups[customerKey] = acc;
            }
            else if (acc.Name == "مشتری عمومی بی‌نام" && customerName != "مشتری عمومی بی‌نام")
            {
                acc.Name = customerName;
            }

            acc.TotalPurchasesRials += Math.Max(0, amount);
            acc.OutstandingRials += Math.Max(0, remaining);
            if (acc.RecentInvoices.Count < 5)
            {
                acc.RecentInvoices.Add(new CustomerInvoiceSummary(number, Math.Max(0, amount), status));
            }
        }

        return groups.Values
            .OrderByDescending(acc => acc.TotalPurchasesRials)
            .Select(acc => new CustomerRow(
                acc.Key,
                acc.Name,
                acc.Phone,
                acc.TotalPurchasesRials,
                acc.OutstandingRials,
                acc.RecentInvoices.ToArray()))
            .ToArray();
    }

    private static (string Key, string Name, string Phone) ExtractCustomerIdentity(JsonElement item)
    {
        string? name = OptionalString(item, "customer_name");
        string? phone = OptionalString(item, "customer_phone") ?? OptionalString(item, "masked_mobile");

        if (item.TryGetProperty("customer", out var customer))
        {
            if (customer.ValueKind == JsonValueKind.Object)
            {
                name ??= OptionalString(customer, "name");
                phone ??= OptionalString(customer, "masked_mobile")
                    ?? OptionalString(customer, "phone");
            }
            else if (customer.ValueKind == JsonValueKind.String)
            {
                phone ??= customer.GetString();
            }
        }

        if (string.IsNullOrWhiteSpace(phone) && string.IsNullOrWhiteSpace(name))
        {
            return ("walk-in", "مشتری عمومی بی‌نام", "—");
        }

        var resolvedPhone = string.IsNullOrWhiteSpace(phone) ? "—" : phone.Trim();
        var resolvedName = string.IsNullOrWhiteSpace(name)
            ? (resolvedPhone == "—" ? "مشتری عمومی بی‌نام" : $"مشتری ({resolvedPhone})")
            : name.Trim();
        var key = resolvedPhone != "—" ? $"phone:{resolvedPhone}" : $"name:{resolvedName}";
        return (key, resolvedName, resolvedPhone);
    }

    private static string FormatInvoiceNumber(JsonElement item)
    {
        if (OptionalInt64(item, "display_number") is long displayNumber)
        {
            return FormattableString.Invariant($"فاکتور {displayNumber}");
        }

        if (OptionalInt64(item, "number") is long number)
        {
            return FormattableString.Invariant($"فاکتور {number}");
        }

        var textNumber = OptionalString(item, "number")
            ?? OptionalString(item, "invoice_number")
            ?? OptionalString(item, "invoice_id")
            ?? "—";
        return textNumber.StartsWith("فاکتور", StringComparison.Ordinal)
            ? textNumber
            : $"فاکتور {textNumber}";
    }

    private static IReadOnlyList<CustomerInvoiceSummary> ReadRecentInvoices(JsonElement item)
    {
        if (!item.TryGetProperty("recent_invoices", out var invoices) ||
            invoices.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<CustomerInvoiceSummary>();
        }

        var list = new List<CustomerInvoiceSummary>();
        foreach (var entry in invoices.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                continue;
            var number = FormatInvoiceNumber(entry);
            var amount = OptionalInt64(entry, "amount_rials")
                ?? OptionalInt64(entry, "total_rials")
                ?? 0;
            list.Add(new CustomerInvoiceSummary(number, amount, OptionalString(entry, "status")));
        }

        return list;
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        var value = OptionalString(parent, name);
        return value ?? throw new JsonException($"Customer field {name} must be a non-empty string.");
    }

    private static string? OptionalString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            return null;
        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static long? OptionalInt64(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var result))
            return null;
        return result;
    }

    private sealed class CustomerAccumulator(string key, string name, string phone)
    {
        public string Key { get; } = key;
        public string Name { get; set; } = name;
        public string Phone { get; } = phone;
        public long TotalPurchasesRials { get; set; }
        public long OutstandingRials { get; set; }
        public List<CustomerInvoiceSummary> RecentInvoices { get; } = new();
    }
}
