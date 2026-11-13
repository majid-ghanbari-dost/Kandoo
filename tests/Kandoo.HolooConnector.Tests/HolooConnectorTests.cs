using System.IO;
using Xunit;
using Kandoo.HolooConnector;

namespace Kandoo.HolooConnector.Tests;

public class HolooConnectorTests
{
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
