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

    [Fact]
    public void SchemaVersion_known_trusted_versions_return_instance()
    {
        var v1 = HolooSchemaVersion.Known("H1-6");
        var v2 = HolooSchemaVersion.Known("H1-7");
        var v3 = HolooSchemaVersion.Known("H1-8");

        Assert.NotNull(v1);
        Assert.NotNull(v2);
        Assert.NotNull(v3);
        Assert.Equal("H1-6", v1!.Value);
        Assert.Equal("H1-7", v2!.Value);
        Assert.Equal("H1-8", v3!.Value);
    }

    [Fact]
    public void SchemaVersion_unknown_or_empty_returns_null()
    {
        Assert.Null(HolooSchemaVersion.Known(null));
        Assert.Null(HolooSchemaVersion.Known(""));
        Assert.Null(HolooSchemaVersion.Known("   "));
        Assert.Null(HolooSchemaVersion.Known("H9-99"));
        Assert.Null(HolooSchemaVersion.Known("unknown"));
    }

    [Fact]
    public void SchemaVersion_tryparse_matches_known()
    {
        Assert.True(HolooSchemaVersion.TryParse("H1-6", out var v));
        Assert.NotNull(v);
        Assert.Equal("H1-6", v!.Value);

        Assert.False(HolooSchemaVersion.TryParse("H9-99", out var v2));
        Assert.Null(v2);
    }

    [Fact]
    public void IncrementalSync_create_if_changed_returns_new_when_version_differs()
    {
        var prev = HolooSchemaVersion.Known("H1-6");
        var curr = HolooSchemaVersion.Known("H1-7");
        Assert.NotNull(prev);
        Assert.NotNull(curr);

        var sync = HolooIncrementalSync.CreateIfChanged(prev!, curr!, 100);

        Assert.NotNull(sync);
        Assert.Equal("H1-7", sync!.Version.Value);
        Assert.Equal(100, sync.LastRowVersion);
        Assert.True(sync.NeedsSync);
    }

    [Fact]
    public void IncrementalSync_create_if_changed_returns_null_when_version_same()
    {
        var prev = HolooSchemaVersion.Known("H1-6");
        var curr = HolooSchemaVersion.Known("H1-6");
        Assert.NotNull(prev);
        Assert.NotNull(curr);

        var sync = HolooIncrementalSync.CreateIfChanged(prev!, curr!, 100);

        Assert.Null(sync);
    }

    [Fact]
    public void IncrementalSync_create_if_changed_returns_new_when_previous_null()
    {
        var curr = HolooSchemaVersion.Known("H1-6");
        Assert.NotNull(curr);

        var sync = HolooIncrementalSync.CreateIfChanged(null, curr!, 0);

        Assert.NotNull(sync);
        Assert.Equal("H1-6", sync!.Version.Value);
        Assert.Equal(0, sync.LastRowVersion);
        Assert.False(sync.NeedsSync);
    }
}