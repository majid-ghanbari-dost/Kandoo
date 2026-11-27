using System.Globalization;

namespace Kandoo.HolooConnector;

public enum HolooSyncMode
{
    Full = 1,
    Delta = 2,
    SkippedIncompatibleSchema = 3,
}

public sealed record HolooSyncSummary(
    HolooSyncMode SyncMode,
    string SchemaVersion,
    string SchemaFingerprint,
    string? PreviousSyncMarker,
    string NextSyncMarker,
    int ProductsImported,
    int CustomersImported,
    int InvoicesImported,
    int ProductsMissingPriceCount,
    int UnverifiedStockCount,
    DateTimeOffset CompletedAt,
    string? Reason = null);

public sealed record HolooCatalogImportCandidate(
    string Sku,
    string? LegacySku,
    string Name,
    decimal? BuyPrice,
    decimal? EffectiveSalePrice,
    string PriceSource,
    bool NeedsPriceConfirmation,
    decimal? HolooVisibleStock,
    bool StockUnverified,
    string? UnitName,
    string? CategoryLabel);

/// <summary>
/// Coordinates full and incremental sync against a Holoo data source.
/// Tracks both schema version and structural fingerprint: if either changes or is unknown,
/// delta sync is disallowed and a full re-read is forced.
/// </summary>
public sealed class HolooIncrementalSync
{
    private readonly HolooShadowStore _store;
    private string _lastSyncMarker = string.Empty;
    private string _lastSchemaVersion = string.Empty;
    private string _lastSchemaFingerprint = string.Empty;

    public HolooIncrementalSync(HolooShadowStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _lastSchemaVersion = store.SchemaVersion;
    }

    public string SchemaVersion => string.IsNullOrEmpty(_lastSchemaVersion) ? _store.SchemaVersion : _lastSchemaVersion;
    public string SchemaFingerprint => _lastSchemaFingerprint;
    public string LastSyncMarker => _lastSyncMarker;

    /// <summary>
    /// Detects schema version from the bundle. Returns Unknown if not found.
    /// Callers must check IsKnown before proceeding with delta sync.
    /// </summary>
    public HolooSchemaVersion DetectSchemaVersion(string bundleRoot) =>
        HolooSchemaVersion.Detect(bundleRoot);

    /// <summary>
    /// Sets the last sync marker after a successful sync.
    /// </summary>
    public void RecordSyncMarker(string marker) => _lastSyncMarker = marker ?? string.Empty;

    public void RecordSyncCheckpoint(HolooSchemaInspection schema, string marker)
    {
        ArgumentNullException.ThrowIfNull(schema);
        _lastSchemaVersion = schema.Version.Value;
        _lastSchemaFingerprint = schema.SchemaFingerprintSha256;
        _lastSyncMarker = marker ?? string.Empty;
    }

    /// <summary>
    /// Determines whether a delta sync is possible given the current schema version.
    /// Returns false when the schema version is unknown or has changed since last sync.
    /// </summary>
    public bool CanDeltaSync(HolooSchemaVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (!version.IsKnown || string.IsNullOrEmpty(_lastSyncMarker))
            return false;

        if (!string.IsNullOrEmpty(_lastSchemaVersion) &&
            !_lastSchemaVersion.Equals(version.Value, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    public bool CanDeltaSync(HolooSchemaInspection inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        if (!inspection.IsCompatible || !CanDeltaSync(inspection.Version))
            return false;

        if (!string.IsNullOrEmpty(_lastSchemaFingerprint) &&
            !_lastSchemaFingerprint.Equals(inspection.SchemaFingerprintSha256, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    public async Task<HolooSyncSummary> SyncAsync(
        IHolooDataSource source,
        SqliteHolooShadowStore? durableStore = null,
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var syncedAt = now ?? DateTimeOffset.UtcNow;

        if (durableStore is not null && string.IsNullOrEmpty(_lastSyncMarker))
        {
            var prior = durableStore.GetLatestCheckpoint(source.SourceKind);
            if (prior is not null)
            {
                _lastSchemaVersion = prior.SchemaVersion;
                _lastSchemaFingerprint = prior.SchemaFingerprint;
                _lastSyncMarker = prior.LastSyncMarker;
            }
        }

        var schema = await source.InspectSchemaAsync(cancellationToken).ConfigureAwait(false);
        if (!schema.Version.IsKnown || (!schema.IsCompatible && source.SourceKind != "csv_bundle"))
        {
            return new HolooSyncSummary(
                HolooSyncMode.SkippedIncompatibleSchema,
                schema.Version.Value,
                schema.SchemaFingerprintSha256,
                string.IsNullOrEmpty(_lastSyncMarker) ? null : _lastSyncMarker,
                _lastSyncMarker,
                0,
                0,
                0,
                0,
                0,
                syncedAt,
                $"INCOMPATIBLE_SCHEMA: Missing [{string.Join(", ", schema.MissingRequiredColumns)}]");
        }

        var canDelta = CanDeltaSync(schema);
        var previousMarker = string.IsNullOrEmpty(_lastSyncMarker) ? null : _lastSyncMarker;
        var dataset = await source.ReadDatasetAsync(
            schema,
            sinceMarker: canDelta ? previousMarker : null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var unitNames = dataset.Units
            .Where(static u => !string.IsNullOrWhiteSpace(u.UnitCode))
            .GroupBy(static u => u.UnitCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static g => g.Key, static g => g.First().UnitName, StringComparer.OrdinalIgnoreCase);

        var mainGroupNames = dataset.MainGroups
            .Where(static g => !string.IsNullOrWhiteSpace(g.GroupCode))
            .GroupBy(static g => g.GroupCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static g => g.Key, static g => g.First().GroupName, StringComparer.OrdinalIgnoreCase);

        var subGroupNames = dataset.SubGroups
            .Where(static g => !string.IsNullOrWhiteSpace(g.GroupCode))
            .GroupBy(static g => $"{g.MainGroupCode}:{g.GroupCode}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static g => g.Key, static g => g.First().GroupName, StringComparer.OrdinalIgnoreCase);

        var resolvedPrices = HolooPriceResolver.ResolveAll(
            dataset.Products,
            dataset.ArticlePrices,
            dataset.Invoices,
            dataset.InvoiceItems);

        var shadowProducts = new List<ShadowProduct>(dataset.Products.Count);
        foreach (var prod in dataset.Products)
        {
            resolvedPrices.TryGetValue(prod.ProductCode, out var priceInfo);
            var priorProduct = _store.GetProduct(prod.ProductCode) ?? durableStore?.GetProduct(prod.ProductCode);

            var explicitSale = priceInfo?.ExplicitSalePrice;
            var lastSold = priceInfo?.LastSoldPrice ?? priorProduct?.LastSoldPrice;
            var lastInvCode = priceInfo?.LastSoldInvoiceCode ?? priorProduct?.LastSoldInvoiceCode;
            var lastInvDate = priceInfo?.LastSoldInvoiceDate ?? priorProduct?.LastSoldInvoiceDate;

            string? unitName = prod.UnitCode is not null && unitNames.TryGetValue(prod.UnitCode, out var un)
                ? un
                : priorProduct?.UnitName;

            string? mainName = prod.MainGroupCode is not null && mainGroupNames.TryGetValue(prod.MainGroupCode, out var mn)
                ? mn
                : priorProduct?.MainGroupName;

            string? subName = prod.MainGroupCode is not null && prod.SubGroupCode is not null &&
                              subGroupNames.TryGetValue($"{prod.MainGroupCode}:{prod.SubGroupCode}", out var sn)
                ? sn
                : priorProduct?.SubGroupName;

            var shadow = new ShadowProduct
            {
                ProductCode = prod.ProductCode,
                LegacyCode = prod.LegacyCode,
                ProductName = prod.ProductName ?? priorProduct?.ProductName,
                CountryOfOrigin = prod.CountryOfOrigin,
                BuyPrice = prod.BuyPrice,
                ExplicitSalePrice = explicitSale,
                LastSoldPrice = lastSold,
                LastSoldInvoiceCode = lastInvCode,
                LastSoldInvoiceDate = lastInvDate,
                HolooVisibleStock = prod.StockQuantity,
                UnitCode = prod.UnitCode,
                UnitName = unitName,
                MainGroupCode = prod.MainGroupCode,
                MainGroupName = mainName,
                SubGroupCode = prod.SubGroupCode,
                SubGroupName = subName,
                Model = prod.Model,
                SourceEditedAt = prod.UpdatedAt,
                SchemaVersion = schema.Version.Value,
                ImportedAt = syncedAt,
                StockUnverified = true,
                IsDeleted = prod.IsDeleted,
                IsActive = prod.IsActive,
            };

            _store.UpsertProduct(shadow);
            shadowProducts.Add(shadow);
        }

        // Also update LastSoldPrice on already-stored products if a delta invoice sold them more recently.
        if (canDelta && dataset.InvoiceItems.Count > 0)
        {
            var deltaInvoiceSales = HolooPriceResolver.ResolveAll(
                dataset.InvoiceItems
                    .Select(static it => new HolooMappedProduct(
                        it.ProductCode, null, null, null, null, null, null, null, null, null, null, null, null, false, true))
                    .GroupBy(static p => p.ProductCode, StringComparer.OrdinalIgnoreCase)
                    .Select(static g => g.First()),
                dataset.ArticlePrices,
                dataset.Invoices,
                dataset.InvoiceItems);

            foreach (var (code, resolved) in deltaInvoiceSales)
            {
                if (shadowProducts.Any(p => p.ProductCode.Equals(code, StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (!resolved.LastSoldPrice.HasValue)
                    continue;

                var existing = _store.GetProduct(code) ?? durableStore?.GetProduct(code);
                if (existing is not null)
                {
                    var updated = existing with
                    {
                        LastSoldPrice = resolved.LastSoldPrice,
                        LastSoldInvoiceCode = resolved.LastSoldInvoiceCode,
                        LastSoldInvoiceDate = resolved.LastSoldInvoiceDate,
                        ImportedAt = syncedAt,
                    };
                    _store.UpsertProduct(updated);
                    shadowProducts.Add(updated);
                }
            }
        }

        var shadowCustomers = new List<ShadowCustomer>(dataset.Customers.Count);
        foreach (var cust in dataset.Customers)
        {
            var shadow = new ShadowCustomer
            {
                CustomerCode = cust.CustomerCode,
                LegacyCode = cust.LegacyCode,
                CustomerName = cust.CustomerName,
                CustomerFamily = cust.AliasName,
                Phone = cust.Phone,
                Mobile = cust.Mobile,
                AdditionalPhones = cust.AdditionalPhones,
                Address = cust.Address,
                PostalCode = cust.PostalCode,
                NationalCode = cust.NationalCode,
                SourceEditedAt = cust.UpdatedAt,
                IsDeleted = cust.IsDeleted,
                SchemaVersion = schema.Version.Value,
                ImportedAt = syncedAt,
            };
            _store.UpsertCustomer(shadow);
            shadowCustomers.Add(shadow);
        }

        var itemsByInvoice = dataset.InvoiceItems
            .GroupBy(static it => it.InvoiceCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static g => g.Key,
                static g => (IReadOnlyList<ShadowInvoiceItem>)g
                    .OrderBy(static it => it.LineIndex)
                    .Select(static it => new ShadowInvoiceItem
                    {
                        LineIndex = it.LineIndex,
                        ProductCode = it.ProductCode,
                        Quantity = it.Quantity,
                        UnitPrice = it.UnitPrice,
                        BuyPrice = it.BuyPrice,
                        DiscountPercent = it.DiscountPercent,
                        LineDiscountAmount = it.LineDiscountAmount,
                    })
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

        var shadowInvoices = new List<ShadowInvoice>(dataset.Invoices.Count);
        foreach (var inv in dataset.Invoices)
        {
            itemsByInvoice.TryGetValue(inv.InvoiceCode, out var lines);
            var shadow = new ShadowInvoice
            {
                InvoiceCode = inv.InvoiceCode,
                InvoiceType = inv.InvoiceType,
                InvoiceDate = inv.InvoiceDate,
                CustomerCode = inv.CustomerCode,
                TotalAmount = inv.TotalAmount,
                CashAmount = inv.CashAmount,
                ChequeAmount = inv.ChequeAmount,
                CreditAmount = inv.CreditAmount,
                CardAmount = inv.CardAmount,
                DiscountAmount = inv.DiscountAmount,
                IsDeleted = inv.IsDeleted,
                Items = lines ?? Array.Empty<ShadowInvoiceItem>(),
                SchemaVersion = schema.Version.Value,
                ImportedAt = syncedAt,
            };
            _store.UpsertInvoice(shadow);
            shadowInvoices.Add(shadow);
        }

        if (durableStore is not null)
        {
            if (shadowProducts.Count > 0)
                durableStore.UpsertProducts(shadowProducts);
            if (shadowCustomers.Count > 0)
                durableStore.UpsertCustomers(shadowCustomers);
            if (shadowInvoices.Count > 0)
                durableStore.UpsertInvoices(shadowInvoices);
        }

        var nextMarker = ComputeNextSyncMarker(dataset, syncedAt, previousMarker);
        var mode = canDelta ? HolooSyncMode.Delta : HolooSyncMode.Full;
        RecordSyncCheckpoint(schema, nextMarker);

        durableStore?.SaveCheckpoint(
            source.SourceKind,
            schema.Version.Value,
            schema.SchemaFingerprintSha256,
            nextMarker,
            mode == HolooSyncMode.Delta ? "delta" : "full",
            syncedAt,
            shadowProducts.Count,
            shadowCustomers.Count,
            shadowInvoices.Count);

        var allProducts = durableStore?.ListProducts() ?? _store.Products;
        var missingPrices = allProducts.Count(static p => p.EvaluatePriceAndStock().EffectivePrice is null);
        var unverifiedStock = allProducts.Count(static p => p.StockUnverified);

        return new HolooSyncSummary(
            mode,
            schema.Version.Value,
            schema.SchemaFingerprintSha256,
            previousMarker,
            nextMarker,
            shadowProducts.Count,
            shadowCustomers.Count,
            shadowInvoices.Count,
            missingPrices,
            unverifiedStock,
            syncedAt);
    }

    public static IReadOnlyList<HolooCatalogImportCandidate> ExportCatalogCandidates(IEnumerable<ShadowProduct> products)
    {
        ArgumentNullException.ThrowIfNull(products);
        var list = new List<HolooCatalogImportCandidate>();
        foreach (var p in products)
        {
            if (p.IsDeleted)
                continue;

            var eval = p.EvaluatePriceAndStock();
            var name = string.IsNullOrWhiteSpace(p.ProductName) ? p.ProductCode : p.ProductName.Trim();
            var categoryParts = new[] { p.MainGroupName, p.SubGroupName }
                .Where(static s => !string.IsNullOrWhiteSpace(s))
                .ToArray();

            list.Add(new HolooCatalogImportCandidate(
                p.ProductCode,
                p.LegacyCode,
                name,
                p.BuyPrice,
                eval.EffectivePrice,
                eval.PriceSource ?? "missing",
                NeedsPriceConfirmation: eval.EffectivePrice is null,
                p.HolooVisibleStock,
                p.StockUnverified,
                p.UnitName,
                categoryParts.Length > 0 ? string.Join(" / ", categoryParts) : null));
        }

        return list;
    }

    private static string ComputeNextSyncMarker(HolooDataset dataset, DateTimeOffset syncedAt, string? previousMarker)
    {
        var timestamps = dataset.Products.Select(static p => p.UpdatedAt)
            .Concat(dataset.Customers.Select(static c => c.UpdatedAt))
            .Concat(dataset.ArticlePrices.Select(static ap => ap.ModifyDate))
            .Concat(dataset.Invoices.Select(static inv => inv.InvoiceDate))
            .Where(static s => !string.IsNullOrWhiteSpace(s))
            .Select(static s => s!.Trim())
            .ToList();

        if (timestamps.Count == 0)
            return previousMarker ?? syncedAt.ToString("O", CultureInfo.InvariantCulture);

        return timestamps.Max(StringComparer.Ordinal) ?? syncedAt.ToString("O", CultureInfo.InvariantCulture);
    }
}
