using System.Globalization;

namespace Kandoo.HolooConnector;

/// <summary>
/// Mapping component for the Holoo domain: 11 objects / 641 columns (see bundle/structure.csv)
/// mapped onto Kandoo domain records. Produced inside C-62; not part of the CSV bundle.
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
    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>(0);

    private static readonly Dictionary<HolooObject, IReadOnlyDictionary<string, string>> Maps = new()
    {
        [HolooObject.Customer] = new Dictionary<string, string>
        {
            ["C_Code"] = "CustomerCode",
            ["C_Name"] = "CustomerName",
            ["C_Family"] = "CustomerFamily",
            ["C_Tell"] = "Phone",
            ["C_Address"] = "Address",
            ["C_PostalCode"] = "PostalCode",
            ["C_NationalCode"] = "NationalCode",
        },
        [HolooObject.Article] = new Dictionary<string, string>
        {
            ["A_Code"] = "ProductCode",
            ["A_Name"] = "ProductName",
            ["A_Country"] = "CountryOfOrigin",
            ["Buy_Price"] = "BuyPrice",
            ["Model"] = "Model",
        },
        [HolooObject.Facture] = new Dictionary<string, string>
        {
            ["F_Code"] = "InvoiceCode",
            ["F_Date"] = "InvoiceDate",
            ["F_Customer"] = "CustomerCode",
            ["cash_amount"] = "CashAmount",
            ["FNaghd"] = "CashAmount",
        },
        [HolooObject.FactArt] = new Dictionary<string, string>
        {
            ["FA_Code"] = "LineCode",
            ["FA_Article"] = "ProductCode",
            ["FA_Count"] = "Quantity",
            ["FA_Price"] = "UnitPrice",
        },
        [HolooObject.ArticlePrice] = new Dictionary<string, string>
        {
            ["AP_Article"] = "ProductCode",
            ["AP_Price"] = "Price",
        },
        [HolooObject.CustTell] = new Dictionary<string, string>
        {
            ["CT_Customer"] = "CustomerCode",
            ["CT_Tell"] = "Phone",
        },
        [HolooObject.MGroup] = new Dictionary<string, string>
        {
            ["MG_Code"] = "GroupCode",
            ["MG_Name"] = "GroupName",
        },
        [HolooObject.SGroup] = new Dictionary<string, string>
        {
            ["SG_Code"] = "GroupCode",
            ["SG_Name"] = "GroupName",
        },
        [HolooObject.TypeAnb] = new Dictionary<string, string>
        {
            ["TA_Code"] = "TypeCode",
        },
        [HolooObject.Unit] = new Dictionary<string, string>
        {
            ["UN_Code"] = "UnitCode",
            ["UN_Name"] = "UnitName",
        },
        [HolooObject.WShowArticleForSearchBig] = new Dictionary<string, string>
        {
            ["A_Code"] = "ProductCode",
            ["A_Name"] = "ProductName",
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

public static class HolooMapper
{
    public static CustomerRecord MapCustomer(IDictionary<string, string> row) => new(
        GetOrDefault(row, "C_Code", string.Empty),
        GetOrDefault(row, "C_Name"),
        GetOrDefault(row, "C_Family"),
        GetOrDefault(row, "C_Tell"),
        GetOrDefault(row, "C_Address"),
        GetOrDefault(row, "C_PostalCode"),
        GetOrDefault(row, "C_NationalCode"));

    public static ProductRecord MapArticle(IDictionary<string, string> row) => new(
        GetOrDefault(row, "A_Code", string.Empty),
        GetOrDefault(row, "A_Name"),
        GetOrDefault(row, "A_Country"),
        ParseDecimal(row, "Buy_Price"),
        GetOrDefault(row, "Model"));

    public static InvoiceRecord MapFacture(IDictionary<string, string> row) => new(
        GetOrDefault(row, "F_Code", string.Empty),
        GetOrDefault(row, "F_Date"),
        GetOrDefault(row, "F_Customer"),
        ParseDecimal(row, "cash_amount") ?? ParseDecimal(row, "FNaghd"));

    public static InvoiceItemRecord MapFactArt(IDictionary<string, string> row) => new(
        GetOrDefault(row, "FA_Code", string.Empty),
        GetOrDefault(row, "FA_Article"),
        ParseInt(row, "FA_Count"),
        ParseDecimal(row, "FA_Price"));

    static string? GetOrDefault(IDictionary<string, string> row, string column) =>
        row.TryGetValue(column, out var value) ? value : null;

    static string GetOrDefault(IDictionary<string, string> row, string column, string fallback) =>
        row.TryGetValue(column, out var value) ? value : fallback;

    static decimal? ParseDecimal(IDictionary<string, string> row, string column) =>
        row.TryGetValue(column, out var value) &&
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    static int? ParseInt(IDictionary<string, string> row, string column) =>
        row.TryGetValue(column, out var value) &&
        int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}
