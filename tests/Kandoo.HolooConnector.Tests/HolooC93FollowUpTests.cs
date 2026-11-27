using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kandoo.HolooConnector;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Kandoo.HolooConnector.Tests;

public class HolooC93FollowUpTests
{
    [Fact]
    public void SchemaVersion_Detects_Real_H16_Structure_Csv_And_Computes_Fingerprint()
    {
        var bundleRoot = Path.Combine(AppContext.BaseDirectory, "bundle");
        var inspection = HolooSchemaVersion.DetectDetailed(bundleRoot);

        Assert.True(inspection.Version.IsKnown);
        Assert.Equal("H1-6", inspection.Version.Value);
        Assert.True(inspection.IsCompatible);
        Assert.Equal(11, inspection.TableCount);
        Assert.Equal(641, inspection.ColumnCount);
        Assert.Empty(inspection.MissingRequiredColumns);
        Assert.Equal(64, inspection.SchemaFingerprintSha256.Length);
    }

    [Fact]
    public void SchemaVersion_Rejects_Incomplete_Columns_As_Unknown_Never_Guesses()
    {
        var partialColumns = new[]
        {
            new HolooSchemaColumn("ARTICLE", "A_Code", "nvarchar"),
            new HolooSchemaColumn("ARTICLE", "A_Name", "nvarchar"),
        };

        var inspection = HolooSchemaVersion.DetectFromColumns(partialColumns);
        Assert.False(inspection.Version.IsKnown);
        Assert.Equal("unknown", inspection.Version.Value);
        Assert.False(inspection.IsCompatible);
        Assert.NotEmpty(inspection.MissingRequiredColumns);
    }

    [Fact]
    public void Mapper_Maps_Real_H16_Columns_For_All_Core_Tables()
    {
        var customer = HolooMapper.MapCustomerFull(new Dictionary<string, string>
        {
            ["C_Code"] = "00015",
            ["C_Name"] = "فروشگاه نمونه",
            ["C_AliasName"] = "شعبه ۲",
            ["C_Tel"] = "02188001122",
            ["C_Mobile"] = "09121234567",
            ["Zip_Code"] = "1415512345",
            ["National_Code"] = "0012345678",
            ["Endeditdate"] = "2026-11-10T09:00:00Z",
        });

        Assert.Equal("00015", customer.CustomerCode);
        Assert.Equal("02188001122", customer.Phone);
        Assert.Equal("09121234567", customer.Mobile);
        Assert.Equal("1415512345", customer.PostalCode);
        Assert.Equal("0012345678", customer.NationalCode);

        var article = HolooMapper.MapArticleFull(new Dictionary<string, string>
        {
            ["A_Code"] = "0102003",
            ["A_Code_C"] = "SKU-42",
            ["A_Name"] = "کاغذ A4",
            ["Buy_Price"] = "420000",
            ["Sel_Price"] = "0",
            ["Exist"] = "15",
            ["VahedCode"] = "1",
            ["Endeditdate"] = "2026-11-12T10:30:00Z",
        });

        Assert.Equal("0102003", article.ProductCode);
        Assert.Equal("01", article.MainGroupCode);
        Assert.Equal("02", article.SubGroupCode);
        Assert.Equal(420000m, article.BuyPrice);
        Assert.Equal(0m, article.SalePrice);
        Assert.Equal(15m, article.StockQuantity);

        var item = HolooMapper.MapFactArtFull(new Dictionary<string, string>
        {
            ["Fac_Code"] = "9001",
            ["A_Index"] = "2",
            ["A_Code"] = "0102003",
            ["Few_Article"] = "3.5",
            ["Price_BS"] = "510000",
            ["Sel_Price"] = "520000",
        });

        Assert.Equal("9001", item.InvoiceCode);
        Assert.Equal(2, item.LineIndex);
        Assert.Equal(3.5m, item.Quantity);
        Assert.Equal(520000m, item.UnitPrice);
    }

    [Fact]
    public async Task Bundle_Import_Resolves_P0001_LastSoldPrice_And_Marks_All_Stock_Unverified()
    {
        var baseDir = AppContext.BaseDirectory;
        var manifestPath = Path.Combine(baseDir, "bundle-sha256.tsv");
        var bundleRoot = Path.Combine(baseDir, "bundle");
        var samplesDir = Path.Combine(bundleRoot, "samples-H1-6-corrected-20261107-092907");

        var source = new CsvBundleHolooSource(bundleRoot, samplesDir, manifestPath);
        var memoryStore = new HolooShadowStore();
        using var durableStore = SqliteHolooShadowStore.OpenInMemory();
        var sync = new HolooIncrementalSync(memoryStore);

        var summary = await sync.SyncAsync(source, durableStore, new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(HolooSyncMode.Full, summary.SyncMode);
        Assert.Equal("H1-6", summary.SchemaVersion);
        Assert.Equal(20, summary.ProductsImported);
        Assert.Equal(20, summary.CustomersImported);
        Assert.Equal(10, summary.InvoicesImported);
        Assert.Equal(19, summary.ProductsMissingPriceCount);
        Assert.Equal(20, summary.UnverifiedStockCount);

        // P-0001 has article_sell_price_candidate=0, so LastSoldPrice resolves from newest invoice F-0001 (5/11/2024 => 46,000,000)
        var p1 = durableStore.GetProduct("P-0001");
        Assert.NotNull(p1);
        Assert.Null(p1!.ExplicitSalePrice);
        Assert.Equal(46000000m, p1.LastSoldPrice);
        Assert.Equal("F-0001", p1.LastSoldInvoiceCode);
        Assert.True(p1.StockUnverified);
        Assert.Equal(46000000m, durableStore.EffectiveSalePrice("P-0001"));

        // P-0002 has inflated visible stock 1000000000 and no prices -> must stay unverified and missing price
        var p2 = durableStore.GetProduct("P-0002");
        Assert.NotNull(p2);
        Assert.Equal(1000000000m, p2!.HolooVisibleStock);
        Assert.True(p2.StockUnverified);
        Assert.Null(durableStore.EffectiveSalePrice("P-0002"));

        // P-0003 has BuyPrice 5030000 and no explicit/sold price -> BuyPrice must NEVER become sale price
        var p3 = durableStore.GetProduct("P-0003");
        Assert.NotNull(p3);
        Assert.Equal(5030000m, p3!.BuyPrice);
        Assert.Null(durableStore.EffectiveSalePrice("P-0003"));
    }

    [Fact]
    public void PriceResolver_Prefers_Explicit_Price_And_Ignores_Returns_Or_Deleted_Invoices()
    {
        var products = new[]
        {
            new HolooMappedProduct("A1", null, "ExplicitItem", null, 1000m, 1800m, 5m, null, null, null, null, null, null, false, true),
            new HolooMappedProduct("A2", null, "ArticlePriceItem", null, 1000m, 0m, 5m, null, null, null, null, null, null, false, true),
            new HolooMappedProduct("A3", null, "HistoricalItem", null, 1000m, null, 5m, null, null, null, null, null, null, false, true),
        };
        var articlePrices = new[]
        {
            new HolooMappedArticlePrice(1, "A2", "1", 2100m, "2026-10-01", null, null),
            new HolooMappedArticlePrice(2, "A2", "1", 2300m, "2026-11-01", null, null),
        };
        var invoices = new[]
        {
            new HolooMappedInvoice("INV-1", "1", "2026-10-01", "C1", 5000m, 5000m, 0m, 0m, 0m, 0m, false),
            new HolooMappedInvoice("INV-2", "1", "2026-11-05", "C1", 6000m, 6000m, 0m, 0m, 0m, 0m, IsDeleted: true),
            new HolooMappedInvoice("INV-3", "3", "2026-11-06", "C1", 7000m, 7000m, 0m, 0m, 0m, 0m, IsDeleted: false), // Fac_Type 3 = Return
        };
        var items = new[]
        {
            new HolooMappedInvoiceItem("INV-1", "1", 1, "A1", 1m, 1400m, 1000m, 0m, 0m),
            new HolooMappedInvoiceItem("INV-1", "1", 2, "A3", 1m, 2750m, 1000m, 0m, 0m),
            new HolooMappedInvoiceItem("INV-2", "1", 1, "A3", 1m, 9999m, 1000m, 0m, 0m),
            new HolooMappedInvoiceItem("INV-3", "3", 1, "A3", 1m, 8888m, 1000m, 0m, 0m),
        };

        var resolved = HolooPriceResolver.ResolveAll(products, articlePrices, invoices, items);

        Assert.Equal(1800m, resolved["A1"].EffectivePrice);
        Assert.Equal("sale_price", resolved["A1"].PriceSource);

        Assert.Equal(2300m, resolved["A2"].EffectivePrice);
        Assert.Equal("sale_price", resolved["A2"].PriceSource);

        Assert.Equal(2750m, resolved["A3"].EffectivePrice);
        Assert.Equal("last_sold", resolved["A3"].PriceSource);
        Assert.Equal("INV-1", resolved["A3"].LastSoldInvoiceCode);
    }

    [Theory]
    [InlineData("INSERT INTO ARTICLE VALUES ('1','2')")]
    [InlineData("UPDATE ARTICLE SET Sel_Price = 1")]
    [InlineData("DELETE FROM CUSTOMER")]
    [InlineData("DROP TABLE FACTURE")]
    [InlineData("SELECT * FROM ARTICLE; DROP TABLE ARTICLE")]
    [InlineData("SELECT /* ok */ * INTO temp_tbl FROM ARTICLE")]
    [InlineData("EXEC sp_who2")]
    [InlineData("")]
    public void QueryGuard_Blocks_Writes_DDL_And_MultiStatements(string unsafeSql)
    {
        Assert.Throws<HolooReadOnlyViolationException>(() => HolooQueryGuard.EnsureReadOnlySelect(unsafeSql));
    }

    [Fact]
    public void QueryGuard_Allows_Valid_ReadOnly_Select_With_String_Literals()
    {
        HolooQueryGuard.EnsureReadOnlySelect("SELECT A_Code, 'DELETE UPDATE' AS Label FROM ARTICLE WHERE Endeditdate > @since;");
    }

    [Fact]
    public async Task DmPolicy_Tier1_WindowsAdmin_Provisions_And_Uninstalls_ReadOnly_Login()
    {
        var transport = new FakeProvisioningTransport
        {
            HasWindowsAdmin = true,
            ReadOnlyProbeSucceeds = true,
        };
        using var store = SqliteHolooShadowStore.OpenInMemory();
        var coordinator = new HolooConnectionCoordinator(transport, store);

        var plan = await coordinator.EstablishAsync(
            new HolooConsentInput(true, "مدیر فروشگاه", @".\HOLOO", "HolooCopy"),
            oneTimeAdminPrompt: () => throw new InvalidOperationException("Tier 2 prompt must not run when Tier 1 succeeds."));

        Assert.Equal(HolooAccessTier.Tier1WindowsAdminProvisioning, plan.SelectedTier);
        Assert.True(plan.SqlConnected);
        Assert.False(plan.UsePrintCaptureFallback);
        Assert.NotNull(plan.ReadOnlyConnectionString);
        Assert.DoesNotContain(transport.LastGeneratedPassword!, plan.RedactedConnectionDescription!);
        Assert.NotNull(plan.Consent);

        var persistedConsent = store.GetConsent(plan.Consent!.ConsentId);
        Assert.NotNull(persistedConsent);
        Assert.False(persistedConsent!.Revoked);

        var uninstalled = await coordinator.UninstallAsync(plan.Consent!);
        Assert.True(uninstalled);
        Assert.True(transport.UninstallCalled);
        Assert.True(store.GetConsent(plan.Consent!.ConsentId)!.Revoked);
    }

    [Fact]
    public async Task DmPolicy_Tier2_Uses_OneTime_Password_Once_Clears_Secret_And_Never_Persists_It()
    {
        var transport = new FakeProvisioningTransport
        {
            HasWindowsAdmin = false,
            ReadOnlyProbeSucceeds = true,
        };
        var dbPath = Path.Combine(Path.GetTempPath(), $"holoo-dm-{Guid.NewGuid():N}.db");
        try
        {
            using var store = SqliteHolooShadowStore.OpenFile(dbPath);
            var coordinator = new HolooConnectionCoordinator(transport, store);
            var secret = "OneTimeSecret#2026!".ToCharArray();
            TransientAdminCredential? capturedCredential = null;

            var plan = await coordinator.EstablishAsync(
                new HolooConsentInput(true, "مجید", @"localhost\HOLOO", "HolooCopy"),
                oneTimeAdminPrompt: () =>
                {
                    capturedCredential = new TransientAdminCredential("sa", secret);
                    return capturedCredential;
                });

            Assert.Equal(HolooAccessTier.Tier2OneTimeAdminPassword, plan.SelectedTier);
            Assert.True(plan.SqlConnected);
            Assert.NotNull(capturedCredential);
            Assert.True(capturedCredential!.IsCleared);
            Assert.False(string.IsNullOrEmpty(plan.ReadOnlyConnectionString));

            // Confirm admin secret is never stored anywhere in SQLite database bytes
            store.Dispose();
            SqliteConnection.ClearAllPools();
            var rawBytes = File.ReadAllText(dbPath);
            Assert.DoesNotContain("OneTimeSecret#2026!", rawBytes, StringComparison.Ordinal);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task DmPolicy_Tier3_Falls_Back_To_Print_Capture_Without_Consent_Or_When_Sql_Unavailable()
    {
        var transport = new FakeProvisioningTransport
        {
            HasWindowsAdmin = false,
            ReadOnlyProbeSucceeds = false,
        };
        using var store = SqliteHolooShadowStore.OpenInMemory();
        var coordinator = new HolooConnectionCoordinator(transport, store);

        var noConsentPlan = await coordinator.EstablishAsync(
            new HolooConsentInput(false, "مدیر", @".\HOLOO", "HolooCopy"));
        Assert.Equal(HolooAccessTier.Tier3PrintCaptureFallback, noConsentPlan.SelectedTier);
        Assert.False(noConsentPlan.SqlConnected);
        Assert.True(noConsentPlan.UsePrintCaptureFallback);
        Assert.Equal(0, transport.Tier1Calls);
        Assert.Equal(0, transport.Tier2Calls);

        var unavailablePlan = await coordinator.EstablishAsync(
            new HolooConsentInput(true, "مدیر", @".\HOLOO", "HolooCopy"),
            oneTimeAdminPrompt: () => null);
        Assert.Equal(HolooAccessTier.Tier3PrintCaptureFallback, unavailablePlan.SelectedTier);
        Assert.False(unavailablePlan.SqlConnected);
        Assert.True(unavailablePlan.UsePrintCaptureFallback);
    }

    [Fact]
    public void DmPolicy_Rejects_Stored_Or_Guessed_Credential_Sources_And_Sql_Injection()
    {
        Assert.Throws<HolooCredentialPolicyException>(() =>
            new TransientAdminCredential("sa", "123".AsSpan(), HolooCredentialSource.GuessedDefaultForbidden));

        Assert.Throws<HolooCredentialPolicyException>(() =>
            new TransientAdminCredential("sa", "stored".AsSpan(), HolooCredentialSource.StoredFileOrRegistryForbidden));

        Assert.Throws<ArgumentException>(() =>
            HolooReadOnlyProvisioningScriptBuilder.BuildCreateReadOnlyLoginSql("HolooCopy; DROP TABLE ARTICLE", "kandoo_ro"));

        Assert.Throws<ArgumentException>(() =>
            HolooReadOnlyProvisioningScriptBuilder.BuildCreateReadOnlyLoginSql("HolooCopy", "bad'login"));

        var ro = HolooSqlConnectionStringFactory.BuildReadOnlyIntegrated(@".\HOLOO", "HolooCopy");
        var parsedRo = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(ro);
        Assert.Equal(Microsoft.Data.SqlClient.ApplicationIntent.ReadOnly, parsedRo.ApplicationIntent);
        Assert.True(parsedRo.IntegratedSecurity);
        Assert.False(parsedRo.PersistSecurityInfo);
        Assert.Contains("Application Intent=ReadOnly", ro, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Integrated Security=True", ro, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SyntheticSqliteHolooSource_Full_And_Delta_Sync_And_Schema_Drift_Forces_Full_Reread()
    {
        // Synthetic SQLite fixture (honesty-labeled) mirroring the 10 real Holoo H1-6 tables
        await using var holooDb = new SqliteConnection("Data Source=:memory:");
        await holooDb.OpenAsync();
        SqliteSyntheticHolooSource.CreateRealH16Schema(holooDb);

        await ExecuteNonQueryAsync(holooDb, """
            INSERT INTO UNIT (Unit_Code, Unit_Name, Endeditdate) VALUES ('1', 'عدد', '2026-11-01T08:00:00Z');
            INSERT INTO M_GROUP (M_groupcode, M_groupname, Type_Anbar_C, Endeditdate) VALUES ('01', 'لوازم تحریر', '1', '2026-11-01T08:00:00Z');
            INSERT INTO S_GROUP (M_groupcode, S_groupcode, S_groupname, Endeditdate) VALUES ('01', '02', 'کاغذ', '2026-11-01T08:00:00Z');
            INSERT INTO TYPE_ANB (Type_Anbar_C, Type_Anbar_N) VALUES ('1', 'انبار اصلی');

            INSERT INTO ARTICLE (A_Code, A_Code_C, A_Name, Buy_Price, Sel_Price, Exist, Type_Anbar_C, VahedCode, Endeditdate)
            VALUES
                ('0102001', 'A4-80', 'بسته کاغذ A4', 180000, 235000, 12, '1', '1', '2026-11-01T10:00:00Z'),
                ('0102002', 'PEN-01', 'خودکار آبی', 12000, 0, 50, '1', '1', '2026-11-01T10:00:00Z');

            INSERT INTO CUSTOMER (C_Code, C_Name, C_Tel, C_Mobile, Endeditdate)
            VALUES ('101', 'علی رضایی', '02144556677', '09123456789', '2026-11-01T10:00:00Z');
            INSERT INTO Cust_Tell (CCode, C_Mobile1) VALUES ('101', '09120001122');

            INSERT INTO FACTURE (Fac_Code, Fac_Type, Fac_Date, C_Code, Sum_Price, FNaghd, FCheck, FNesieh, Card)
            VALUES ('5001', '1', '2026-11-01T09:00:00Z', '101', 30000, 30000, 0, 0, 0);
            INSERT INTO FACTART (Fac_Code, Fac_Type, A_Index, A_Code, Few_Article, Price_BS, Sel_Price)
            VALUES ('5001', '1', 1, '0102002', 2, 15000, 15000);
            """);

        var source = new SqliteSyntheticHolooSource(holooDb);
        using var durable = SqliteHolooShadowStore.OpenInMemory();
        var mem = new HolooShadowStore();
        var sync = new HolooIncrementalSync(mem);

        var t0 = new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
        var run1 = await sync.SyncAsync(source, durable, t0);

        Assert.Equal(HolooSyncMode.Full, run1.SyncMode);
        Assert.Equal("H1-6", run1.SchemaVersion);
        Assert.Equal(2, run1.ProductsImported);
        Assert.Equal(1, run1.CustomersImported);
        Assert.Equal(1, run1.InvoicesImported);
        Assert.Equal(0, run1.ProductsMissingPriceCount);

        var p1 = durable.GetProduct("0102001")!;
        Assert.Equal(235000m, durable.EffectiveSalePrice("0102001"));
        Assert.Equal("عدد", p1.UnitName);
        Assert.Equal("لوازم تحریر", p1.MainGroupName);
        Assert.Equal("کاغذ", p1.SubGroupName);
        Assert.True(p1.StockUnverified);

        var p2 = durable.GetProduct("0102002")!;
        Assert.Null(p2.ExplicitSalePrice);
        Assert.Equal(15000m, p2.LastSoldPrice);
        Assert.Equal(15000m, durable.EffectiveSalePrice("0102002"));

        var c101 = durable.GetCustomer("101")!;
        Assert.Contains("09120001122", c101.AdditionalPhones);

        // Owner explicitly verifies stock on 0102001
        var verified = durable.ConfirmProductStock("0102001", 10m, "مدیر", t0.AddMinutes(5));
        Assert.False(verified.StockUnverified);
        Assert.Equal(10m, verified.VerifiedStock);

        // Mutate one product in Holoo (delta update after marker) — must run Delta mode and mark updated product unverified again
        await ExecuteNonQueryAsync(holooDb, """
            PRAGMA query_only = OFF;
            INSERT INTO ARTICLE (A_Code, A_Code_C, A_Name, Buy_Price, Sel_Price, Exist, Type_Anbar_C, VahedCode, Endeditdate)
            VALUES ('0102003', 'PENCIL', 'مداد مشکی', 8000, NULL, 30, '1', '1', '2026-11-03T12:00:00Z');
            """);

        var run2 = await sync.SyncAsync(source, durable, t0.AddDays(1));
        Assert.Equal(HolooSyncMode.Delta, run2.SyncMode);
        Assert.Equal(1, run2.ProductsImported);
        Assert.Equal(0, run2.CustomersImported);
        Assert.Equal(0, run2.InvoicesImported);
        Assert.Equal(1, run2.ProductsMissingPriceCount); // 0102003 has no explicit/sold price
        Assert.Equal(3, durable.ListProducts().Count);
        Assert.False(durable.GetProduct("0102001")!.StockUnverified); // untouched item keeps owner verification

        // Export Hub catalog candidates
        var candidates = HolooIncrementalSync.ExportCatalogCandidates(durable.ListProducts());
        Assert.Contains(candidates, c => c.Sku == "0102003" && c.NeedsPriceConfirmation && c.StockUnverified);
        Assert.Contains(candidates, c => c.Sku == "0102002" && c.EffectiveSalePrice == 15000m && c.PriceSource == "last_sold");

        // Schema change (new column added to ARTICLE) alters structural fingerprint -> MUST force Full re-read
        await ExecuteNonQueryAsync(holooDb, """
            PRAGMA query_only = OFF;
            ALTER TABLE ARTICLE ADD COLUMN CustomBarcode TEXT NULL;
            """);

        var run3 = await sync.SyncAsync(source, durable, t0.AddDays(2));
        Assert.Equal(HolooSyncMode.Full, run3.SyncMode);
        Assert.Equal(3, run3.ProductsImported);
        Assert.Equal(1, run3.InvoicesImported);
        Assert.True(durable.GetProduct("0102001")!.StockUnverified); // re-imported stock is marked unverified again
    }

    private static async Task ExecuteNonQueryAsync(SqliteConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private sealed class FakeProvisioningTransport : IHolooProvisioningTransport
    {
        public bool HasWindowsAdmin { get; init; }
        public bool ReadOnlyProbeSucceeds { get; init; }
        public int Tier1Calls { get; private set; }
        public int Tier2Calls { get; private set; }
        public bool UninstallCalled { get; private set; }
        public string? LastGeneratedPassword { get; private set; }

        public Task<bool> HasWindowsSqlAdminAsync(string instanceName, string databaseName, CancellationToken cancellationToken = default) =>
            Task.FromResult(HasWindowsAdmin);

        public Task<HolooProvisioningOutcome> ProvisionReadOnlyViaWindowsAdminAsync(
            string instanceName, string databaseName, string loginName, CancellationToken cancellationToken = default)
        {
            Tier1Calls++;
            LastGeneratedPassword = "RO-T1-Password!9";
            return Task.FromResult(new HolooProvisioningOutcome(true, loginName, LastGeneratedPassword, null));
        }

        public Task<HolooProvisioningOutcome> ProvisionReadOnlyViaOneTimeAdminAsync(
            string instanceName, string databaseName, string loginName, TransientAdminCredential adminCredential, CancellationToken cancellationToken = default)
        {
            Tier2Calls++;
            try
            {
                _ = adminCredential.ExposeOnceForConnection();
                LastGeneratedPassword = "RO-T2-Password!9";
                return Task.FromResult(new HolooProvisioningOutcome(true, loginName, LastGeneratedPassword, null));
            }
            finally
            {
                adminCredential.Dispose();
            }
        }

        public Task<bool> VerifyReadOnlySelectAsync(string readOnlyConnectionString, CancellationToken cancellationToken = default) =>
            Task.FromResult(ReadOnlyProbeSucceeds);

        public Task<bool> UninstallReadOnlyLoginAsync(
            string instanceName, string databaseName, string loginName, TransientAdminCredential? adminCredential = null, CancellationToken cancellationToken = default)
        {
            UninstallCalled = true;
            adminCredential?.Dispose();
            return Task.FromResult(true);
        }
    }
}
