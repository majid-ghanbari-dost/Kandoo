using System.Globalization;

namespace Kandoo.HolooConnector;

/// <summary>
/// Mapping component for the Holoo domain: 11 objects / 641 columns (see bundle/structure.csv)
/// mapped onto Kandoo domain records. Produced inside C-62; extended in C-93 / C-HOLOO to map
/// real Holoo H1-6 columns as well as anonymized CSV bundle samples.
/// </summary>
public enum HolooObject
{
    Article,
    ArticlePrice,
    CustTell,
    Customer,
    FactArt,
    Facture,
    MGroup,
    SGroup,
    TypeAnb,
    Unit,
    WShowArticleForSearchBig
}

public static class HolooColumnMap
{
    private static readonly IReadOnlyDictionary<string, string> Empty =
        new Dictionary<string, string>(0, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<HolooObject, IReadOnlyDictionary<string, string>> Maps = new()
    {
        [HolooObject.Customer] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["C_Code"] = "CustomerCode",
            ["C_Code_C"] = "CustomerLegacyCode",
            ["C_Name"] = "CustomerName",
            ["C_AliasName"] = "CustomerFamily",
            ["C_Family"] = "CustomerFamily",
            ["C_Tel"] = "Phone",
            ["C_Tell"] = "Phone",
            ["C_Mobile"] = "Mobile",
            ["C_Address"] = "Address",
            ["Zip_Code"] = "PostalCode",
            ["C_PostalCode"] = "PostalCode",
            ["National_Code"] = "NationalCode",
            ["C_NationalCode"] = "NationalCode",
            ["Endeditdate"] = "UpdatedAt",
            ["Delete"] = "IsDeleted",
        },
        [HolooObject.Article] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["A_Code"] = "ProductCode",
            ["A_Code_C"] = "LegacyProductCode",
            ["A_Name"] = "ProductName",
            ["A_Country"] = "CountryOfOrigin",
            ["Buy_Price"] = "BuyPrice",
            ["EndBuy_Price"] = "EndBuyPrice",
            ["Sel_Price"] = "SalePrice",
            ["Exist"] = "StockQuantity",
            ["Type_Anbar_C"] = "WarehouseTypeCode",
            ["VahedCode"] = "UnitCode",
            ["Model"] = "Model",
            ["Endeditdate"] = "UpdatedAt",
            ["Delete"] = "IsDeleted",
            ["IsActive"] = "IsActive",
        },
        [HolooObject.Facture] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Fac_Code"] = "InvoiceCode",
            ["F_Code"] = "InvoiceCode",
            ["Fac_Type"] = "InvoiceType",
            ["Fac_Date"] = "InvoiceDate",
            ["F_Date"] = "InvoiceDate",
            ["C_Code"] = "CustomerCode",
            ["F_Customer"] = "CustomerCode",
            ["Sum_Price"] = "TotalAmount",
            ["cash_amount"] = "CashAmount",
            ["FNaghd"] = "CashAmount",
            ["FCheck"] = "ChequeAmount",
            ["FNesieh"] = "CreditAmount",
            ["Card"] = "CardAmount",
            ["Takhfif"] = "DiscountAmount",
            ["Delete"] = "IsDeleted",
        },
        [HolooObject.FactArt] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Fac_Code"] = "InvoiceCode",
            ["Fac_Type"] = "InvoiceType",
            ["A_Index"] = "LineCode",
            ["FA_Code"] = "LineCode",
            ["A_Code"] = "ProductCode",
            ["FA_Article"] = "ProductCode",
            ["Few_Article"] = "Quantity",
            ["FA_Count"] = "Quantity",
            ["Price_BS"] = "UnitPrice",
            ["Sel_Price"] = "SalePrice",
            ["FA_Price"] = "UnitPrice",
            ["Buy_Price"] = "BuyPrice",
            ["DarsadTakhfif"] = "DiscountPercent",
            ["TakhfifSatriR"] = "LineDiscountAmount",
        },
        [HolooObject.ArticlePrice] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ID"] = "PriceRowId",
            ["A_Code"] = "ProductCode",
            ["AP_Article"] = "ProductCode",
            ["SelID"] = "PriceTierId",
            ["Sel_Price"] = "Price",
            ["AP_Price"] = "Price",
            ["ModifyDate"] = "UpdatedAt",
            ["Min_Price"] = "MinPrice",
            ["Max_Price"] = "MaxPrice",
        },
        [HolooObject.CustTell] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CCode"] = "CustomerCode",
            ["CT_Customer"] = "CustomerCode",
            ["C_Tell1"] = "Phone",
            ["C_Mobile1"] = "Mobile",
            ["CT_Tell"] = "Phone",
        },
        [HolooObject.MGroup] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["M_groupcode"] = "GroupCode",
            ["MG_Code"] = "GroupCode",
            ["M_groupname"] = "GroupName",
            ["MG_Name"] = "GroupName",
            ["Endeditdate"] = "UpdatedAt",
        },
        [HolooObject.SGroup] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["M_groupcode"] = "MainGroupCode",
            ["S_groupcode"] = "GroupCode",
            ["SG_Code"] = "GroupCode",
            ["S_groupname"] = "GroupName",
            ["SG_Name"] = "GroupName",
            ["Endeditdate"] = "UpdatedAt",
        },
        [HolooObject.TypeAnb] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Type_Anbar_C"] = "TypeCode",
            ["TA_Code"] = "TypeCode",
            ["Type_Anbar_N"] = "TypeName",
        },
        [HolooObject.Unit] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Unit_Code"] = "UnitCode",
            ["UN_Code"] = "UnitCode",
            ["Unit_Name"] = "UnitName",
            ["UN_Name"] = "UnitName",
            ["Endeditdate"] = "UpdatedAt",
        },
        [HolooObject.WShowArticleForSearchBig] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["A_Code"] = "ProductCode",
            ["A_Code_C"] = "LegacyProductCode",
            ["A_Name"] = "ProductName",
            ["Exist"] = "StockQuantity",
            ["Buy_Price"] = "BuyPrice",
            ["Sel_Price"] = "SalePrice",
            ["VahedCode"] = "UnitCode",
            ["M_GroupCode"] = "MainGroupCode",
            ["S_GroupCode"] = "SubGroupCode",
            ["M_groupname"] = "MainGroupName",
            ["S_groupname"] = "SubGroupName",
        },
    };

    public static IReadOnlyDictionary<string, string> For(HolooObject obj) =>
        Maps.TryGetValue(obj, out var map) ? map : Empty;
}

public sealed record CustomerRecord(
    string CustomerCode,
    string? CustomerName,
    string? CustomerFamily,
    string? Phone,
    string? Address,
    string? PostalCode,
    string? NationalCode);

public sealed record ProductRecord(
    string ProductCode,
    string? ProductName,
    string? CountryOfOrigin,
    decimal? BuyPrice,
    string? Model);

public sealed record InvoiceRecord(
    string InvoiceCode,
    string? InvoiceDate,
    string? CustomerCode,
    decimal? CashAmount);

public sealed record InvoiceItemRecord(
    string LineCode,
    string? ProductCode,
    int? Quantity,
    decimal? UnitPrice);

public sealed record HolooMappedCustomer(
    string CustomerCode,
    string? LegacyCode,
    string? CustomerName,
    string? AliasName,
    string? Phone,
    string? Mobile,
    string? Address,
    string? PostalCode,
    string? NationalCode,
    string? UpdatedAt,
    bool IsDeleted,
    IReadOnlyList<string> AdditionalPhones);

public sealed record HolooMappedProduct(
    string ProductCode,
    string? LegacyCode,
    string? ProductName,
    string? CountryOfOrigin,
    decimal? BuyPrice,
    decimal? SalePrice,
    decimal? StockQuantity,
    string? WarehouseTypeCode,
    string? UnitCode,
    string? MainGroupCode,
    string? SubGroupCode,
    string? Model,
    string? UpdatedAt,
    bool IsDeleted,
    bool IsActive);

public sealed record HolooMappedArticlePrice(
    long? PriceRowId,
    string ProductCode,
    string? PriceTierId,
    decimal? Price,
    string? ModifyDate,
    decimal? MinPrice,
    decimal? MaxPrice);

public sealed record HolooMappedInvoice(
    string InvoiceCode,
    string? InvoiceType,
    string? InvoiceDate,
    string? CustomerCode,
    decimal? TotalAmount,
    decimal? CashAmount,
    decimal? ChequeAmount,
    decimal? CreditAmount,
    decimal? CardAmount,
    decimal? DiscountAmount,
    bool IsDeleted);

public sealed record HolooMappedInvoiceItem(
    string InvoiceCode,
    string? InvoiceType,
    int LineIndex,
    string ProductCode,
    decimal? Quantity,
    decimal? UnitPrice,
    decimal? BuyPrice,
    decimal? DiscountPercent,
    decimal? LineDiscountAmount);

public sealed record HolooMappedMainGroup(
    string GroupCode,
    string? GroupName,
    string? WarehouseTypeCode,
    string? UpdatedAt);

public sealed record HolooMappedSubGroup(
    string MainGroupCode,
    string GroupCode,
    string? GroupName,
    string? UpdatedAt);

public sealed record HolooMappedUnit(
    string UnitCode,
    string? UnitName,
    string? UpdatedAt);

public sealed record HolooMappedWarehouseType(
    string TypeCode,
    string? TypeName);

public sealed record HolooDataset(
    IReadOnlyList<HolooMappedProduct> Products,
    IReadOnlyList<HolooMappedArticlePrice> ArticlePrices,
    IReadOnlyList<HolooMappedCustomer> Customers,
    IReadOnlyList<HolooMappedInvoice> Invoices,
    IReadOnlyList<HolooMappedInvoiceItem> InvoiceItems,
    IReadOnlyList<HolooMappedMainGroup> MainGroups,
    IReadOnlyList<HolooMappedSubGroup> SubGroups,
    IReadOnlyList<HolooMappedUnit> Units,
    IReadOnlyList<HolooMappedWarehouseType> WarehouseTypes);

public static class HolooMapper
{
    public static CustomerRecord MapCustomer(IDictionary<string, string> row)
    {
        var full = MapCustomerFull(row);
        return new CustomerRecord(
            full.CustomerCode,
            full.CustomerName,
            full.AliasName,
            full.Phone ?? full.Mobile,
            full.Address,
            full.PostalCode,
            full.NationalCode);
    }

    public static ProductRecord MapArticle(IDictionary<string, string> row)
    {
        var full = MapArticleFull(row);
        return new ProductRecord(
            full.ProductCode,
            full.ProductName,
            full.CountryOfOrigin,
            full.BuyPrice,
            full.Model);
    }

    public static InvoiceRecord MapFacture(IDictionary<string, string> row)
    {
        var full = MapFactureFull(row);
        return new InvoiceRecord(
            full.InvoiceCode,
            full.InvoiceDate,
            full.CustomerCode,
            full.CashAmount);
    }

    public static InvoiceItemRecord MapFactArt(IDictionary<string, string> row)
    {
        var full = MapFactArtFull(row);
        var lineCode = FirstNonEmpty(row, "FA_Code", "A_Index", "line_index") ?? string.Empty;
        int? intQuantity = full.Quantity.HasValue
            ? decimal.ToInt32(decimal.Truncate(full.Quantity.Value))
            : null;
        return new InvoiceItemRecord(
            lineCode,
            string.IsNullOrEmpty(full.ProductCode) ? null : full.ProductCode,
            intQuantity,
            full.UnitPrice);
    }

    public static HolooMappedCustomer MapCustomerFull(IDictionary<string, string> row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var code = FirstNonEmpty(row, "C_Code", "customer_id") ?? string.Empty;
        var extraPhones = SplitPhones(FirstNonEmpty(row, "masked_phones", "phones"));
        var tel = FirstNonEmpty(row, "C_Tell", "C_Tel");
        var mobile = FirstNonEmpty(row, "C_Mobile");

        if (tel is null && mobile is null && extraPhones.Count > 0)
            tel = extraPhones[0];

        return new HolooMappedCustomer(
            code,
            FirstNonEmpty(row, "C_Code_C"),
            FirstNonEmpty(row, "C_Name", "customer_name"),
            FirstNonEmpty(row, "C_Family", "C_AliasName"),
            tel,
            mobile,
            FirstNonEmpty(row, "C_Address"),
            FirstNonEmpty(row, "Zip_Code", "C_PostalCode"),
            FirstNonEmpty(row, "National_Code", "C_NationalCode"),
            FirstNonEmpty(row, "Endeditdate"),
            ParseBoolFlag(row, "Delete", defaultValue: false),
            extraPhones);
    }

    public static HolooMappedProduct MapArticleFull(IDictionary<string, string> row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var code = FirstNonEmpty(row, "A_Code", "product_id") ?? string.Empty;

        string? mainGroup = FirstNonEmpty(row, "M_GroupCode");
        string? subGroup = FirstNonEmpty(row, "S_GroupCode");
        if (mainGroup is null && subGroup is null && code.Length >= 4 && !code.Contains('-', StringComparison.Ordinal))
        {
            // Standard 7-character Holoo A_Code layout: MMMSSAA
            mainGroup = code[..2];
            subGroup = code.Substring(2, 2);
        }

        return new HolooMappedProduct(
            code,
            FirstNonEmpty(row, "A_Code_C"),
            FirstNonEmpty(row, "A_Name", "product_name"),
            FirstNonEmpty(row, "A_Country"),
            ParseFirstDecimal(row, "Buy_Price", "EndBuy_Price", "article_buy_price_candidate"),
            ParseFirstDecimal(row, "Sel_Price", "article_sell_price_candidate"),
            ParseFirstDecimal(row, "Exist", "holoo_visible_stock"),
            FirstNonEmpty(row, "Type_Anbar_C", "holoo_type_code"),
            FirstNonEmpty(row, "VahedCode"),
            mainGroup,
            subGroup,
            FirstNonEmpty(row, "Model"),
            FirstNonEmpty(row, "Endeditdate"),
            ParseBoolFlag(row, "Delete", defaultValue: false),
            ParseBoolFlag(row, "IsActive", defaultValue: true));
    }

    public static HolooMappedArticlePrice MapArticlePrice(IDictionary<string, string> row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new HolooMappedArticlePrice(
            ParseLong(row, "ID"),
            FirstNonEmpty(row, "A_Code", "AP_Article", "product_id") ?? string.Empty,
            FirstNonEmpty(row, "SelID"),
            ParseFirstDecimal(row, "Sel_Price", "AP_Price"),
            FirstNonEmpty(row, "ModifyDate"),
            ParseFirstDecimal(row, "Min_Price"),
            ParseFirstDecimal(row, "Max_Price"));
    }

    public static HolooMappedInvoice MapFactureFull(IDictionary<string, string> row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new HolooMappedInvoice(
            FirstNonEmpty(row, "Fac_Code", "F_Code", "invoice_id") ?? string.Empty,
            FirstNonEmpty(row, "Fac_Type"),
            FirstNonEmpty(row, "Fac_Date", "F_Date", "invoice_date"),
            FirstNonEmpty(row, "C_Code", "F_Customer", "customer_id"),
            ParseFirstDecimal(row, "Sum_Price", "total_amount"),
            ParseFirstDecimal(row, "cash_amount", "FNaghd"),
            ParseFirstDecimal(row, "FCheck", "cheque_amount"),
            ParseFirstDecimal(row, "FNesieh", "credit_amount"),
            ParseFirstDecimal(row, "Card", "card_amount"),
            ParseFirstDecimal(row, "Takhfif", "discount_amount"),
            ParseBoolFlag(row, "Delete", defaultValue: false));
    }

    public static HolooMappedInvoiceItem MapFactArtFull(IDictionary<string, string> row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var lineIndex = ParseInt(row, "A_Index", "line_index", "FA_Code") ?? 0;

        // Price_BS or Sel_Price in Holoo FACTART; historical_price in CSV sample
        var unitPrice = ParseFirstDecimal(row, "Sel_Price", "Price_BS", "FA_Price", "historical_price", "unit_price");

        return new HolooMappedInvoiceItem(
            FirstNonEmpty(row, "Fac_Code", "F_Code", "invoice_id") ?? string.Empty,
            FirstNonEmpty(row, "Fac_Type"),
            lineIndex,
            FirstNonEmpty(row, "A_Code", "FA_Article", "product_id") ?? string.Empty,
            ParseFirstDecimal(row, "Few_Article", "FA_Count", "quantity"),
            unitPrice,
            ParseFirstDecimal(row, "Buy_Price"),
            ParseFirstDecimal(row, "DarsadTakhfif", "discount_percent"),
            ParseFirstDecimal(row, "TakhfifSatriR", "line_discount"));
    }

    public static HolooMappedMainGroup MapMainGroup(IDictionary<string, string> row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new HolooMappedMainGroup(
            FirstNonEmpty(row, "M_groupcode", "MG_Code") ?? string.Empty,
            FirstNonEmpty(row, "M_groupname", "MG_Name"),
            FirstNonEmpty(row, "Type_Anbar_C"),
            FirstNonEmpty(row, "Endeditdate"));
    }

    public static HolooMappedSubGroup MapSubGroup(IDictionary<string, string> row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new HolooMappedSubGroup(
            FirstNonEmpty(row, "M_groupcode") ?? string.Empty,
            FirstNonEmpty(row, "S_groupcode", "SG_Code") ?? string.Empty,
            FirstNonEmpty(row, "S_groupname", "SG_Name"),
            FirstNonEmpty(row, "Endeditdate"));
    }

    public static HolooMappedUnit MapUnit(IDictionary<string, string> row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new HolooMappedUnit(
            FirstNonEmpty(row, "Unit_Code", "UN_Code") ?? string.Empty,
            FirstNonEmpty(row, "Unit_Name", "UN_Name"),
            FirstNonEmpty(row, "Endeditdate"));
    }

    public static HolooMappedWarehouseType MapWarehouseType(IDictionary<string, string> row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new HolooMappedWarehouseType(
            FirstNonEmpty(row, "Type_Anbar_C", "TA_Code") ?? string.Empty,
            FirstNonEmpty(row, "Type_Anbar_N"));
    }

    public static IReadOnlyList<string> ExtractCustTellPhones(IDictionary<string, string> row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var phones = new List<string>();
        for (var i = 1; i <= 5; i++)
        {
            var mob = FirstNonEmpty(row, $"C_Mobile{i}");
            if (mob is not null)
                phones.Add(mob);
            var tel = FirstNonEmpty(row, $"C_Tell{i}");
            if (tel is not null)
                phones.Add(tel);
        }

        var legacy = FirstNonEmpty(row, "CT_Tell");
        if (legacy is not null)
            phones.Add(legacy);

        return phones.Distinct(StringComparer.Ordinal).ToList();
    }

    private static string? FirstNonEmpty(IDictionary<string, string> row, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (TryGetCaseInsensitive(row, key, out var raw) && !string.IsNullOrWhiteSpace(raw))
                return raw.Trim();
        }

        return null;
    }

    private static decimal? ParseFirstDecimal(IDictionary<string, string> row, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (TryGetCaseInsensitive(row, key, out var raw) &&
                !string.IsNullOrWhiteSpace(raw) &&
                decimal.TryParse(raw.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static int? ParseInt(IDictionary<string, string> row, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (TryGetCaseInsensitive(row, key, out var raw) &&
                !string.IsNullOrWhiteSpace(raw) &&
                int.TryParse(raw.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static long? ParseLong(IDictionary<string, string> row, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (TryGetCaseInsensitive(row, key, out var raw) &&
                !string.IsNullOrWhiteSpace(raw) &&
                long.TryParse(raw.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static bool ParseBoolFlag(IDictionary<string, string> row, string key, bool defaultValue)
    {
        if (!TryGetCaseInsensitive(row, key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return defaultValue;

        var trimmed = raw.Trim();
        if (trimmed is "1" || trimmed.Equals("true", StringComparison.OrdinalIgnoreCase))
            return true;
        if (trimmed is "0" || trimmed.Equals("false", StringComparison.OrdinalIgnoreCase))
            return false;

        return defaultValue;
    }

    private static bool TryGetCaseInsensitive(IDictionary<string, string> row, string key, out string value)
    {
        if (row.TryGetValue(key, out var direct))
        {
            value = direct;
            return true;
        }

        foreach (var kv in row)
        {
            if (kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                value = kv.Value;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static IReadOnlyList<string> SplitPhones(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();

        return raw.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
