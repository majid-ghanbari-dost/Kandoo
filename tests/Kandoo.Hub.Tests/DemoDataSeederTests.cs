using System.Collections.Generic;
using System.IO;
using Kandoo.Core;
using Kandoo.Hub;
using Xunit;

namespace Kandoo.Hub.Tests;

public sealed class DemoDataSeederTests : IDisposable
{
    private readonly string _directory;
    private readonly SqliteHubStore _store;
    private readonly Guid _storeId = Guid.Parse("01010101-0101-0101-0101-010101010101");
    private readonly Guid _deviceId = Guid.Parse("02020202-0202-0202-0202-020202020202");

    public DemoDataSeederTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "kandoo-demo-seeder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        var connectionString = $"Data Source={Path.Combine(_directory, "hub.db")};Pooling=False";
        _store = new SqliteHubStore(connectionString);
    }

    public void Dispose()
    {
        _store.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }

    [Fact]
    public void C80_DemoDataSeederCreatesDemoProductsAndInvoices()
    {
        var catalog = new HubProductCatalogService(_store);
        var seeder = new DemoDataSeeder(
            _store, catalog, _storeId, _deviceId, new ManualHubClock());
        seeder.Seed();

        // About 40 demo products across groups/subgroups.
        var products = _store.LoadProductCatalog().Count(_storeId);
        Assert.InRange(products, 30, 45);

        // About 12 issued demo invoices.
        var invoices = _store.LoadInvoiceService().Count;
        Assert.Equal(12, invoices);

        // Every issued invoice carries a distinct demo customer.
        var customerIds = new System.Collections.Generic.HashSet<Guid>();
        foreach (var id in EnumerateIssuedInvoiceIds())
        {
            var invoice = _store.LoadInvoiceService().Get(_storeId, id);
            Assert.NotNull(invoice);
            Assert.NotNull(invoice!.CustomerId);
            Assert.NotEqual(Guid.Empty, invoice!.CustomerId!.Value);
            customerIds.Add(invoice.CustomerId!.Value);
        }
        Assert.True(customerIds.Count >= 10, "Expected about 20 demo customers across issued invoices.");
    }

    [Theory]
    [InlineData("http://192.168.1.50:8080", true)]
    [InlineData("http://10.0.0.9:5000", true)]
    [InlineData("http://172.20.1.2:80", true)]
    [InlineData("http://localhost:5000", true)]
    [InlineData("http://127.0.0.1:5000", true)]
    [InlineData("https://hub.example.com", true)]
    [InlineData("http://8.8.8.8:8080", false)]
    [InlineData("http://203.0.113.5:80", false)]
    public void C80_DemoBaseUrlAllowsOnlyPrivateLanCleartext(string baseUrl, bool expectedValid)
    {
        var options = new DemoHubOptions { Mode = true, BaseUrl = baseUrl };
        if (expectedValid)
        {
            var ex = Record.Exception(() => options.Validate());
            Assert.Null(ex);
        }
        else
        {
            var ex = Assert.Throws<ArgumentException>(() => options.Validate());
            Assert.Contains("private LAN", ex.Message);
        }
    }

    [Fact]
    public void C80_DemoBaseUrlRejectsNonHttpScheme()
    {
        var options = new DemoHubOptions { Mode = true, BaseUrl = "ftp://192.168.1.1" };
        var ex = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Contains("HTTP or HTTPS", ex.Message);
    }

    [Fact]
    public void C80_DemoBaseUrlNotRequiredWhenDemoModeOff()
    {
        var options = new DemoHubOptions { Mode = false, BaseUrl = null };
        var ex = Record.Exception(() => options.Validate());
        Assert.Null(ex);
    }

    private IEnumerable<Guid> EnumerateIssuedInvoiceIds()
    {
        // Re-seed is idempotent on product IDs; invoice IDs are deterministic, so
        // we can enumerate them directly.
        for (var i = 0; i < 12; i++)
            yield return DemoDataInvoiceId(i);
    }

    private static Guid DemoDataInvoiceId(int index)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var bytes = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes("demo-invoice-" + index));
        return new Guid(bytes);
    }
}
