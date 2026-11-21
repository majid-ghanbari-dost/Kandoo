using System;
using System.IO;
using Xunit;
using Kandoo.HolooConnector;

namespace Kandoo.HolooConnector.Tests;

public class HolooConnectorTests
{
    [Fact]
    public void Schema_version_unknown_when_no_structure_csv()
    {
        var version = HolooSchemaVersion.Detect(Path.Combine(Directory.GetCurrentDirectory(), "nonexistent"));
        Assert.False(version.IsKnown);
        Assert.Equal("unknown", version.Value);
    }

    [Fact]
    public void Schema_version_reads_marker_from_structure_csv()
    {
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        File.WriteAllText(Path.Combine(tmp, "structure.csv"), "schema_version,H1-6\n");
        try
        {
            var version = HolooSchemaVersion.Detect(tmp);
            Assert.True(version.IsKnown);
            Assert.Equal("H1-6", version.Value);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public void Shadow_store_upsert_and_get_product()
    {
        var store = new HolooShadowStore { SchemaVersion = "H1-6" };
        store.UpsertProduct(new ShadowProduct
        {
            ProductCode = "P1",
            ProductName = "Test Product",
            BuyPrice = 1000m,
            SchemaVersion = "H1-6",
            ImportedAt = DateTimeOffset.UtcNow,
        });

        var p = store.GetProduct("P1");
        Assert.NotNull(p);
        Assert.Equal("P1", p.ProductCode);
        Assert.Equal("Test Product", p.ProductName);
        Assert.Equal(1000m, p.BuyPrice);
        Assert.True(p.StockUnverified);
    }

    [Fact]
    public void Shadow_store_stock_unverified_by_default()
    {
        var store = new HolooShadowStore();
        store.UpsertProduct(new ShadowProduct
        {
            ProductCode = "P2",
            SchemaVersion = "H1-6",
            ImportedAt = DateTimeOffset.UtcNow,
        });

        Assert.True(store.GetProduct("P2")!.StockUnverified);
    }

    [Fact]
    public void Shadow_store_effective_price_last_sold_when_no_sale_price()
    {
        var store = new HolooShadowStore();
        store.UpsertProduct(new ShadowProduct
        {
            ProductCode = "P3",
            BuyPrice = 500m,
            LastSoldPrice = 750m,
            SchemaVersion = "H1-6",
            ImportedAt = DateTimeOffset.UtcNow,
        });

        Assert.Equal(750m, store.EffectiveSalePrice("P3"));
    }

    [Fact]
    public void Shadow_store_effective_price_null_when_no_price_data()
    {
        var store = new HolooShadowStore();
        store.UpsertProduct(new ShadowProduct
        {
            ProductCode = "P4",
            BuyPrice = 500m,
            SchemaVersion = "H1-6",
            ImportedAt = DateTimeOffset.UtcNow,
        });

        Assert.Null(store.EffectiveSalePrice("P4"));
    }

    [Fact]
    public void Stock_verifier_marks_unverified_with_sale_price()
    {
        var result = HolooStockVerifier.MarkUnverified("P5", 2000m, null);
        Assert.False(result.IsVerified);
        Assert.Equal(2000m, result.EffectivePrice);
        Assert.Equal("sale_price", result.PriceSource);
    }

    [Fact]
    public void Stock_verifier_marks_unverified_with_last_sold_only()
    {
        var result = HolooStockVerifier.MarkUnverified("P6", null, 1500m);
        Assert.False(result.IsVerified);
        Assert.Equal(1500m, result.EffectivePrice);
        Assert.Equal("last_sold", result.PriceSource);
    }

    [Fact]
    public void Stock_verifier_marks_unverified_with_no_price()
    {
        var result = HolooStockVerifier.MarkUnverified("P7", null, null);
        Assert.False(result.IsVerified);
        Assert.Null(result.EffectivePrice);
        Assert.Null(result.PriceSource);
    }

    [Fact]
    public void Incremental_sync_cannot_delta_when_unknown_schema()
    {
        var store = new HolooShadowStore();
        var sync = new HolooIncrementalSync(store);
        Assert.False(sync.CanDeltaSync(HolooSchemaVersion.Unknown));
    }

    [Fact]
    public void Incremental_sync_cannot_delta_without_marker()
    {
        var store = new HolooShadowStore();
        var sync = new HolooIncrementalSync(store);
        Assert.False(sync.CanDeltaSync(new HolooSchemaVersion("H1-6", true)));
    }

    [Fact]
    public void Bundle_passes_per_file_sha256_acceptance_table()
    {
        var baseDir = AppContext.BaseDirectory;
        var manifestPath = Path.Combine(baseDir, "bundle-sha256.tsv");
        var bundleRoot = Path.Combine(baseDir, "bundle");
        var result = HolooBundleVerifier.Verify(manifestPath, bundleRoot);
        Assert.True(result.AllPass, string.Join("; ", result.Failures.Select(f => $"{f.RelativePath}: {f.Reason}")));
    }

    [Fact]
    public void Mapper_maps_customer_row()
    {
        var row = new Dictionary<string, string>
        {
            ["C_Code"] = "C100",
            ["C_Name"] = "Reza",
            ["C_Family"] = "Ahmadi",
            ["C_Tell"] = "0912",
            ["C_Address"] = "Tehran",
            ["C_PostalCode"] = "12345",
            ["C_NationalCode"] = "0012345678",
        };
        var c = HolooMapper.MapCustomer(row);
        Assert.Equal("C100", c.CustomerCode);
        Assert.Equal("Reza", c.CustomerName);
        Assert.Equal("Ahmadi", c.CustomerFamily);
        Assert.Equal("0912", c.Phone);
    }

    [Fact]
    public void Mapper_maps_invoice_with_either_cash_column()
    {
        var rowPersian = new Dictionary<string, string> { ["F_Code"] = "INV-1", ["FNaghd"] = "250000" };
        var invPersian = HolooMapper.MapFacture(rowPersian);
        Assert.True(invPersian.CashAmount.HasValue);
        Assert.Equal(250000m, invPersian.CashAmount.Value);

        var rowEnglish = new Dictionary<string, string> { ["F_Code"] = "INV-2", ["cash_amount"] = "12000.50" };
        var invEnglish = HolooMapper.MapFacture(rowEnglish);
        Assert.True(invEnglish.CashAmount.HasValue);
        Assert.Equal(12000.50m, invEnglish.CashAmount.Value);
    }

    [Fact]
    public void Column_map_covers_all_eleven_objects()
    {
        var objects = Enum.GetValues<HolooObject>();
        Assert.Equal(11, objects.Length);
        Assert.All(objects, o => Assert.NotEmpty(HolooColumnMap.For(o)));
    }
}
