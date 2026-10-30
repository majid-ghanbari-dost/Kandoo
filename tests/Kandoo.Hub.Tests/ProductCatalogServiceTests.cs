using Kandoo.Core;
using Kandoo.Hub;
using Xunit;

namespace Kandoo.Hub.Tests;

public sealed class ProductCatalogServiceTests
{
    private static readonly Guid S1 = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid S2 = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid D1 = Guid.Parse("33333333-3333-4333-8333-333333333333");

    [Fact]
    public void C13_RequiredFieldsStockAndStructuredMetadataAreEnforced()
    {
        using var store = new SqliteHubStore("Data Source=:memory:;Pooling=False");
        var catalog = new HubProductCatalogService(store);
        Assert.Throws<ArgumentException>(() => catalog.Register(S1, D1, "missing-name", new ProductRegistrationInput(null, 1000, 1000)));
        Assert.Throws<ArgumentException>(() => catalog.Register(S1, D1, "missing-price", new ProductRegistrationInput("Name", null, 1000)));
        Assert.Throws<ArgumentException>(() => catalog.Register(S1, D1, "missing-stock", new ProductRegistrationInput("Name", 1000, null)));
        Assert.Equal(0L, catalog.GetTagHighWatermark(S1));
        Assert.Equal(0, store.LoadOperationLog().Count(S1));

        var input = new ProductRegistrationInput(
            "  شیر‌كاکائو  ", 12_500, 2_500, "prod-metadata",
            Category: "  dairy ", Color: " white ", Size: " 1L ", Material: " plastic ",
            Brand: " kandoo ", Barcode: " 123456 ", Specification: "   ");
        var result = catalog.Register(S1, D1, "create-metadata", input);

        Assert.Equal(ProductRegistrationStatus.Created, result.Status);
        var product = Assert.IsType<Product>(result.Product);
        Assert.Equal(1L, product.ProductTagNumber);
        Assert.Equal("شیر کاکائو", product.NormalizedName);
        Assert.Equal("شیرکاکائو", product.SimilarityKey);
        Assert.Equal("dairy", product.Category);
        Assert.Equal("white", product.Color);
        Assert.Equal("1L", product.Size);
        Assert.Equal("plastic", product.Material);
        Assert.Equal("kandoo", product.Brand);
        Assert.Equal("123456", product.Barcode);
        Assert.Null(product.Specification);
        Assert.Equal(2_500L, result.StockMillis);
        Assert.Equal(2_500L, catalog.GetStockMillis(S1, product.ProductId));
        Assert.Equal(product, catalog.GetByTag(S1, 1));
        Assert.Null(catalog.GetByTag(S2, 1));
        var replay = catalog.Register(S1, D1, "create-metadata", input);
        Assert.True(replay.IdempotentReplay);
        Assert.Equal(result.OperationSeq, replay.OperationSeq);
        Assert.Equal(1L, replay.Product!.ProductTagNumber);
        Assert.Equal(2_500L, catalog.GetStockMillis(S1, product.ProductId));
        Assert.Equal(1L, catalog.GetTagHighWatermark(S1));
        Assert.Equal(1, store.LoadOperationLog().Count(S1));
    }

    [Fact]
    public void C13_DuplicateWarningsDoNotAllocateAndStoresAllocateIndependentTags()
    {
        using var store = new SqliteHubStore("Data Source=:memory:;Pooling=False");
        var catalog = new HubProductCatalogService(store);
        var first = catalog.Register(S1, D1, "create-1", Input("شیرکاکائو", "prod-1"));
        Assert.Equal(1L, first.Product!.ProductTagNumber);

        var warningInput = Input("شیر کاکائو", "prod-2");
        var warning = catalog.Register(S1, D1, "create-warning", warningInput);
        Assert.Equal(ProductRegistrationStatus.NearDuplicateWarning, warning.Status);
        Assert.Equal("prod-1", warning.CandidateProductId);
        Assert.Null(warning.Product);
        Assert.Equal(1L, catalog.GetTagHighWatermark(S1));
        var replay = catalog.Register(S1, D1, "create-warning", warningInput);
        Assert.True(replay.IdempotentReplay);
        Assert.Equal(warning.OperationSeq, replay.OperationSeq);
        Assert.Equal(1L, catalog.GetTagHighWatermark(S1));

        var acknowledged = catalog.Register(S1, D1, "create-acknowledged", Input("شیر کاکائو", "prod-2", AcknowledgedCandidateId: "prod-1"));
        Assert.Equal(ProductRegistrationStatus.Created, acknowledged.Status);
        Assert.Equal(2L, acknowledged.Product!.ProductTagNumber);
        var exact = catalog.Register(S1, D1, "create-exact", Input(" شیر‌كاکائو ", "prod-3"));
        Assert.Equal(ProductRegistrationStatus.ExactDuplicate, exact.Status);
        Assert.Equal(2L, catalog.GetTagHighWatermark(S1));

        var other = catalog.Register(S2, D1, "create-other", Input("شیر کاکائو", "prod-other"));
        Assert.Equal(ProductRegistrationStatus.Created, other.Status);
        Assert.Equal(1L, other.Product!.ProductTagNumber);
        Assert.Equal(2L, catalog.GetTagHighWatermark(S1));
        Assert.Equal(1L, catalog.GetTagHighWatermark(S2));
        Assert.Equal("prod-1", catalog.GetByTag(S1, 1)!.ProductId);
        Assert.Equal("prod-other", catalog.GetByTag(S2, 1)!.ProductId);
    }

    [Fact]
    public void C13_RetirementRestartAndRestoreNeverReuseTags()
    {
        using var database = new TempDatabase();
        JsonElementState saved;
        IReadOnlyList<ReplicationLogEntry> entries;
        using (var store = new SqliteHubStore(database.ConnectionString))
        {
            var catalog = new HubProductCatalogService(store);
            Assert.Equal(1L, catalog.Register(S1, D1, "create-1", Input("Alpha", "prod-1")).Product!.ProductTagNumber);
            Assert.Equal(2L, catalog.Register(S1, D1, "create-2", Input("Beta", "prod-2")).Product!.ProductTagNumber);
            var retired = catalog.Retire(S1, D1, "retire-1", "prod-1", 1);
            Assert.Equal(ProductUpdateStatus.Updated, retired.Status);
            Assert.False(retired.Product!.IsActive);
            Assert.Equal(1L, retired.Product.ProductTagNumber);
            Assert.Equal(3L, catalog.Register(S1, D1, "create-3", Input("Alpha", "prod-3")).Product!.ProductTagNumber);
            var state = store.ExportRestoreState(S1, 1);
            Assert.False(state.TryGetProperty("product_operator_fields", out _));
            saved = new JsonElementState(state, CanonicalJson.ReplayedStateHash(state));
            entries = store.ReadBatch(S1, 100, 1_048_576)!.Entries!;
            Assert.Equal(4, entries.Count);
        }

        using (var reopened = new SqliteHubStore(database.ConnectionString))
        {
            var catalog = new HubProductCatalogService(reopened);
            Assert.Equal(3L, catalog.GetTagHighWatermark(S1));
            Assert.False(catalog.GetByTag(S1, 1)!.IsActive);
            Assert.Equal("prod-3", catalog.GetByTag(S1, 3)!.ProductId);
        }

        using var fresh = new SqliteHubStore("Data Source=:memory:;Pooling=False");
        fresh.RestoreReplayedState(saved.State, entries, saved.Hash);
        var restored = new HubProductCatalogService(fresh);
        Assert.Equal(3L, restored.GetTagHighWatermark(S1));
        Assert.Equal(1L, restored.GetByTag(S1, 1)!.ProductTagNumber);
        Assert.Equal(2L, restored.GetByTag(S1, 2)!.ProductTagNumber);
        Assert.Equal(3L, restored.GetByTag(S1, 3)!.ProductTagNumber);
        var next = restored.Register(S1, D1, "create-4", Input("Gamma", "prod-4", InitialStockMillis: 0));
        Assert.Equal(ProductRegistrationStatus.Created, next.Status);
        Assert.Equal(4L, next.Product!.ProductTagNumber);
        Assert.Equal(0L, next.StockMillis);
        Assert.Equal(5L, next.OperationSeq);
        Assert.Equal(4L, restored.GetTagHighWatermark(S1));
        var pending = Assert.Single(fresh.ReadBatch(S1, 100, 1_048_576)!.Entries!);
        Assert.Equal(5L, pending.OperationSeq);
    }

    [Fact]
    public void C23_SuppliedTagAndOrderedOperatorFieldsSurviveReplayAndRestore()
    {
        using var store = new SqliteHubStore("Data Source=:memory:;Pooling=False");
        var catalog = new HubProductCatalogService(store);
        var fields = new[]
        {
            new ProductOperatorField("z-last-alphabetically", "first-by-operator"),
            new ProductOperatorField("a-first-alphabetically", "second-by-operator")
        };
        var input = new ProductRegistrationInput(
            "Tagged", 2_000, 0, "tagged", ProductTagNumber: 41, OperatorFields: fields);
        var created = catalog.Register(S1, D1, "create-tagged", input);

        Assert.Equal(ProductRegistrationStatus.Created, created.Status);
        Assert.Equal(41L, created.Product!.ProductTagNumber);
        Assert.Equal(41L, catalog.GetTagHighWatermark(S1));
        Assert.Equal(fields, catalog.GetOperatorFields(S1, "tagged"));
        Assert.True(catalog.Register(S1, D1, "create-tagged", input).IdempotentReplay);
        Assert.Equal(1, store.LoadOperationLog().Count(S1));

        var duplicateTag = catalog.Register(S1, D1, "duplicate-tag", new ProductRegistrationInput(
            "Other", 2_000, 0, "other", ProductTagNumber: 41));
        Assert.Equal(ProductRegistrationStatus.DuplicateProductTag, duplicateTag.Status);
        Assert.Equal(41L, catalog.GetTagHighWatermark(S1));
        Assert.Equal(42L, catalog.Register(S1, D1, "create-next", Input("Next", "next", 0)).Product!.ProductTagNumber);

        var state = store.ExportRestoreState(S1, 1);
        var hash = CanonicalJson.ReplayedStateHash(state);
        var entries = store.ReadBatch(S1, 100, 1_048_576)!.Entries!;
        Assert.Equal(3, entries.Count);
        Assert.Equal("z-last-alphabetically", state.GetProperty("product_operator_fields")[0].GetProperty("key").GetString());

        using var fresh = new SqliteHubStore("Data Source=:memory:;Pooling=False");
        fresh.RestoreReplayedState(state, entries, hash);
        var restored = new HubProductCatalogService(fresh);
        Assert.Equal(fields, restored.GetOperatorFields(S1, "tagged"));
        Assert.Equal(42L, restored.GetTagHighWatermark(S1));
        Assert.Equal(hash, CanonicalJson.ReplayedStateHash(fresh.ExportRestoreState(S1, 1)));
    }

    [Fact]
    public void C23_OperatorFieldsAreBoundedOrderedAndUnique()
    {
        using var store = new SqliteHubStore("Data Source=:memory:;Pooling=False");
        var catalog = new HubProductCatalogService(store);
        var invalid = new ProductRegistrationInput(
            "Invalid", 1_000, 0, "invalid",
            OperatorFields: new[] { new ProductOperatorField("same", "one"), new ProductOperatorField("same", "two") });

        Assert.Throws<ArgumentException>(() => catalog.Register(S1, D1, "invalid-fields", invalid));
        Assert.Equal(0, store.LoadOperationLog().Count(S1));
        Assert.Equal(0L, catalog.GetTagHighWatermark(S1));
    }

    [Fact]
    public void C23_LegacyCreatePayloadKeepsItsRequestHash()
    {
        using var store = new SqliteHubStore("Data Source=:memory:;Pooling=False");
        var catalog = new HubProductCatalogService(store);
        catalog.Register(S1, D1, "legacy-create", new ProductRegistrationInput(
            "Legacy", 1_000, 0, "legacy", Category: "old", Specification: null));
        var legacyPayload = HubJson.Element(new
        {
            product_id = "legacy",
            name = "Legacy",
            unit_price_rials = 1_000,
            initial_stock_millis = 0,
            acknowledged_candidate_id = (string?)null,
            category = "old",
            color = (string?)null,
            size = (string?)null,
            material = (string?)null,
            brand = (string?)null,
            barcode = (string?)null,
            specification = (string?)null
        });
        var expected = new OperationRequest(S1, D1, "legacy-create", "product.create", null, legacyPayload).RequestHash;

        Assert.Equal(expected, store.LoadOperationLog().Get(S1, "legacy-create")!.RequestHash);
    }

    private static ProductRegistrationInput Input(
        string name,
        string productId,
        long InitialStockMillis = 1_000,
        string? AcknowledgedCandidateId = null) => new(
            name, 1_000, InitialStockMillis, productId, AcknowledgedCandidateId);

    private sealed record JsonElementState(System.Text.Json.JsonElement State, string Hash);

    private sealed class TempDatabase : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "kandoo-c13", Guid.NewGuid().ToString("N"));

        public TempDatabase()
        {
            Directory.CreateDirectory(_directory);
            ConnectionString = $"Data Source={Path.Combine(_directory, "hub.db")};Pooling=False";
        }

        public string ConnectionString { get; }
        public void Dispose() => Directory.Delete(_directory, true);
    }
}
