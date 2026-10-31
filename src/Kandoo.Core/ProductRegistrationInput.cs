namespace Kandoo.Core;

public sealed record ProductRegistrationInput(
    string? Name,
    long? UnitPriceRials,
    long? InitialStockMillis,
    string? ProductId = null,
    string? AcknowledgedCandidateId = null,
    string? Category = null,
    string? Color = null,
    string? Size = null,
    string? Material = null,
    string? Brand = null,
    string? Barcode = null,
    string? Specification = null,
    long? ProductTagNumber = null,
    IReadOnlyList<ProductOperatorField>? OperatorFields = null,
    long? PurchasePriceRials = null)
{
    public ProductRegistrationInput Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("Product name is required.", nameof(Name));
        }

        if (UnitPriceRials is null or < 0)
        {
            throw new ArgumentException("A non-negative integer Rial price is required.", nameof(UnitPriceRials));
        }

        if (InitialStockMillis is null or < 0)
        {
            throw new ArgumentException("A non-negative initial warehouse quantity is required.", nameof(InitialStockMillis));
        }

        if (ProductTagNumber is <= 0)
        {
            throw new ArgumentException("A supplied product tag must be a positive integer.", nameof(ProductTagNumber));
        }

        if (PurchasePriceRials is < 0)
        {
            throw new ArgumentException("Purchase price must be a non-negative integer number of Rials when supplied.", nameof(PurchasePriceRials));
        }

        ProductNameNormalizer.Normalize(Name);
        return this with
        {
            Name = Name.Trim(),
            ProductId = Optional(ProductId),
            AcknowledgedCandidateId = Optional(AcknowledgedCandidateId),
            Category = Optional(Category),
            Color = Optional(Color),
            Size = Optional(Size),
            Material = Optional(Material),
            Brand = Optional(Brand),
            Barcode = Optional(Barcode),
            Specification = Optional(Specification),
            OperatorFields = ValidateOperatorFields(OperatorFields)
        };
    }

    public static IReadOnlyList<ProductOperatorField> ValidateOperatorFields(IReadOnlyList<ProductOperatorField>? fields)
    {
        if (fields is null || fields.Count == 0)
        {
            return Array.Empty<ProductOperatorField>();
        }

        if (fields.Count > 32)
        {
            throw new ArgumentException("At most 32 operator-defined fields are allowed.", nameof(fields));
        }

        var normalized = new List<ProductOperatorField>(fields.Count);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (field is null || string.IsNullOrWhiteSpace(field.Key) || field.Key.Length > 64 || field.Value is null || field.Value.Length > 512)
            {
                throw new ArgumentException("Operator-defined fields require a bounded key and string value.", nameof(fields));
            }

            var key = field.Key.Trim();
            if (key.Length == 0 || !keys.Add(key))
            {
                throw new ArgumentException("Operator-defined field keys must be non-empty and unique.", nameof(fields));
            }

            normalized.Add(new ProductOperatorField(key, field.Value));
        }

        return normalized;
    }

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
