using Kandoo.Core;
using Xunit;

namespace Kandoo.Core.Tests;

public sealed class ProductCatalogTests
{
    [Fact]
    public void TC_D9_R2_NormalizesArabicPersianVariantsWhitespaceAndLatinCase()
    {
        Assert.Equal("شیر کاکائو", ProductNameNormalizer.Normalize("  شیر‌كاکائو  "));
        Assert.Equal("shir kakao", ProductNameNormalizer.Normalize("SHIR  Kakao"));
        Assert.Equal("شیرکاکائو", ProductNameNormalizer.SimilarityKey("  شیر‌كاکائو  "));
        Assert.Equal("shirkakao", ProductNameNormalizer.SimilarityKey("SHIR‐Kakao"));
    }

    [Fact]
    public void TC_D9_R1_ProductIdsAreStoreScopedAndNeverOverwritten()
    {
        var catalog = new ProductCatalog();
        var first = catalog.Register(TestData.S1, "prod-1", "Alpha", 1000);
        var duplicate = catalog.Register(TestData.S1, "prod-1", "Beta", 2000);
        var otherStore = catalog.Register(TestData.S2, "prod-1", "Beta", 2000);

        Assert.Equal(ProductRegistrationStatus.Created, first.Status);
        Assert.Equal(ProductRegistrationStatus.DuplicateProductId, duplicate.Status);
        Assert.Equal(ProductRegistrationStatus.Created, otherStore.Status);
        Assert.Equal("Alpha", catalog.Get(TestData.S1, "prod-1")!.Name);
        Assert.Equal("Beta", catalog.Get(TestData.S2, "prod-1")!.Name);
    }

    [Fact]
    public void TC_D9_R3_ExactDuplicateFailsWithoutChangingCatalog()
    {
        var catalog = new ProductCatalog();
        var existing = catalog.Register(TestData.S1, "prod-1", "شیر کاکائو", 1000);
        var result = catalog.Register(TestData.S1, "prod-2", " شیر‌كاکائو ", 2000);

        Assert.Equal(ProductRegistrationStatus.Created, existing.Status);
        Assert.Equal(ProductRegistrationStatus.ExactDuplicate, result.Status);
        Assert.Equal(1, catalog.Count(TestData.S1));
        Assert.Equal(1000, catalog.Get(TestData.S1, "prod-1")!.UnitPriceRials);
    }

    [Fact]
    public void TC_D9_R4_NearDuplicateRequiresCandidateSpecificAcknowledgement()
    {
        var catalog = new ProductCatalog();
        catalog.Register(TestData.S1, "prod-1", "شیرکاکائو", 1000);
        var warning = catalog.Register(TestData.S1, "prod-2", "شیر کاکائو", 1000);
        var created = catalog.Register(TestData.S1, "prod-2", "شیر کاکائو", 1000, "prod-1");

        Assert.Equal(ProductRegistrationStatus.NearDuplicateWarning, warning.Status);
        Assert.Equal("prod-1", warning.CandidateProductId);
        Assert.Equal(ProductRegistrationStatus.Created, created.Status);
        Assert.Equal(2, catalog.Count(TestData.S1));
        Assert.NotEqual(catalog.Get(TestData.S1, "prod-1")!.NormalizedName, catalog.Get(TestData.S1, "prod-2")!.NormalizedName);
    }

    [Fact]
    public void TC_D9_R5_MatchingNeverCrossesStoreBoundary()
    {
        var catalog = new ProductCatalog();
        catalog.Register(TestData.S1, "prod-1", "شیر کاکائو", 1000);
        var result = catalog.Register(TestData.S2, "prod-2", "شیر کاکائو", 1000);

        Assert.Equal(ProductRegistrationStatus.Created, result.Status);
        Assert.Equal(1, catalog.Count(TestData.S1));
        Assert.Equal(1, catalog.Count(TestData.S2));
    }

    [Fact]
    public void TC_D10_R4_MD_R7_StaleProductEditCannotOverwriteWinner()
    {
        var catalog = new ProductCatalog();
        catalog.Register(TestData.S1, "prod-1", "Alpha", 1000);
        var winner = catalog.Update(TestData.S1, "prod-1", 1, 1100);
        var stale = catalog.Update(TestData.S1, "prod-1", 1, specification: "loser");

        Assert.Equal(ProductUpdateStatus.Updated, winner.Status);
        Assert.Equal(ProductUpdateStatus.VersionConflict, stale.Status);
        Assert.Equal(2, stale.CurrentVersion);
        Assert.Equal(1100, catalog.Get(TestData.S1, "prod-1")!.UnitPriceRials);
        Assert.Null(catalog.Get(TestData.S1, "prod-1")!.Specification);
    }

    [Fact]
    public void C32_ProfitRequiresExplicitPurchasePriceAndIsNeverPersisted()
    {
        var catalog = new ProductCatalog();
        var registered = catalog.Register(
            TestData.S1, "prod-catalog-cost", "Catalog cost", 1_000, purchasePriceRials: 400);
        Assert.Equal(600L, registered.Product!.ProfitRials);
        Assert.Throws<ArgumentException>(() => catalog.Register(
            TestData.S1, "prod-invalid-cost", "Invalid cost", 1_000, purchasePriceRials: -1));

        var missing = new Product(TestData.S1, "prod-profit", "Profit", "profit", "profit", 1_000, 1);
        Assert.Null(missing.PurchasePriceRials);
        Assert.Null(missing.ProfitRials);

        var zero = missing with { PurchasePriceRials = 0 };
        Assert.Equal(0L, zero.PurchasePriceRials);
        Assert.Equal(1_000L, zero.ProfitRials);
        var costed = missing with { PurchasePriceRials = 400 };
        Assert.Equal(600L, costed.ProfitRials);
        var loss = missing with { PurchasePriceRials = 1_200 };
        Assert.Equal(-200L, loss.ProfitRials);

        var missingJson = System.Text.Json.JsonSerializer.Serialize(missing);
        Assert.DoesNotContain("PurchasePriceRials", missingJson, StringComparison.Ordinal);
        Assert.DoesNotContain("ProfitRials", missingJson, StringComparison.Ordinal);
        var zeroJson = System.Text.Json.JsonSerializer.Serialize(zero);
        Assert.Contains("PurchasePriceRials", zeroJson, StringComparison.Ordinal);
        Assert.DoesNotContain("ProfitRials", zeroJson, StringComparison.Ordinal);
    }
}
