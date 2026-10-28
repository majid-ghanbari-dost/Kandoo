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
    string? Specification = null)
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
            Specification = Optional(Specification)
        };
    }

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
