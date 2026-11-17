using Kandoo.Core;
using Kandoo.Hub;
using Xunit;

namespace Kandoo.Hub.Tests;

public sealed class ProductGroupServiceTests
{
    private static readonly Guid S1 = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid S2 = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid D1 = Guid.Parse("33333333-3333-4333-8333-333333333333");

    [Fact]
    public void Create_list_get_duplicate_and_parent_group_rules()
    {
        using var store = new SqliteHubStore("Data Source=:memory:;Pooling=False");
        var catalog = new HubProductCatalogService(store);

        var created = catalog.CreateProductGroup(S1, D1, "op-g1", "G1", null, "غذا");
        Assert.Equal("CREATED", created);

        var groups = catalog.GetProductGroups(S1);
        Assert.Single(groups);
        Assert.Equal("G1", groups[0].GroupId);
        Assert.Null(groups[0].ParentGroupId);
        Assert.Equal("غذا", groups[0].Name);
        Assert.True(groups[0].IsActive);
        Assert.False(string.IsNullOrEmpty(groups[0].CreatedAt));

        var single = catalog.GetProductGroup(S1, "G1");
        Assert.NotNull(single);
        Assert.Equal("G1", single.GroupId);
        Assert.Equal("غذا", single.Name);

        Assert.Null(catalog.GetProductGroup(S1, "NOPE"));

        var duplicate = catalog.CreateProductGroup(S1, D1, "op-g2", "G1", null, "غذا دیگر");
        Assert.Equal("DUPLICATE_GROUP_ID", duplicate);

        var badParent = catalog.CreateProductGroup(S1, D1, "op-g3", "G2", "MISSING", "ناشناس");
        Assert.Equal("UNKNOWN_PARENT_GROUP", badParent);

        var child = catalog.CreateProductGroup(S1, D1, "op-g4", "G2", "G1", "ناشود");
        Assert.Equal("CREATED", child);
        Assert.Equal(2, catalog.GetProductGroups(S1).Count);
        Assert.Equal("G1", catalog.GetProductGroup(S1, "G2")!.ParentGroupId);

        // Replaying the same operation returns the original outcome without duplication.
        var replay = catalog.CreateProductGroup(S1, D1, "op-g1", "G1", null, "غذا");
        Assert.Equal("CREATED", replay);
        Assert.Equal(2, catalog.GetProductGroups(S1).Count);

        // Same operation ID with different content is a conflict.
        var conflict = Assert.Throws<InvalidOperationException>(
            () => catalog.CreateProductGroup(S1, D1, "op-g1", "G1", null, "غذا تغییر یافت"));
        Assert.Equal("OPERATION_ID_CONFLICT", conflict.Message);
    }

    [Fact]
    public void Product_group_assignment_and_list_filter()
    {
        using var store = new SqliteHubStore("Data Source=:memory:;Pooling=False");
        var catalog = new HubProductCatalogService(store);

        Assert.Equal("CREATED", catalog.CreateProductGroup(S1, D1, "op-g1", "G1", null, "غذا"));

        var product = catalog.Register(S1, D1, "op-p1",
            new ProductRegistrationInput("نان سنگک", 10_000, 1_000, "prod-1"));
        Assert.Equal(ProductRegistrationStatus.Created, product.Status);
        var productId = product.Product!.ProductId;

        var assigned = catalog.AssignProductToGroup(S1, D1, "op-a1", productId, "G1");
        Assert.Equal("ASSIGNED", assigned);

        var inGroup = catalog.List(S1, groupId: "G1");
        Assert.Single(inGroup.Items);
        Assert.Equal(productId, inGroup.Items[0].Product.ProductId);
        Assert.Equal("G1", inGroup.Items[0].Product.GroupId);

        var all = catalog.List(S1);
        Assert.Single(all.Items);
        Assert.Equal("G1", all.Items[0].Product.GroupId);

        var stored = store.GetProduct(S1, productId);
        Assert.Equal("G1", stored!.GroupId);

        var unassigned = catalog.AssignProductToGroup(S1, D1, "op-a2", productId, null);
        Assert.Equal("UNASSIGNED", unassigned);
        Assert.Empty(catalog.List(S1, groupId: "G1").Items);
        Assert.Null(store.GetProduct(S1, productId)!.GroupId);

        Assert.Equal("UNKNOWN_PRODUCT", catalog.AssignProductToGroup(S1, D1, "op-a3", "nope", "G1"));
        Assert.Equal("UNKNOWN_GROUP", catalog.AssignProductToGroup(S1, D1, "op-a4", productId, "nope"));

        // Groups are store-scoped: S2 may reuse the same group ID.
        Assert.Equal("CREATED", catalog.CreateProductGroup(S2, D1, "op-g5", "G1", null, "غذا"));
        Assert.Single(catalog.GetProductGroups(S2));
    }

    [Fact]
    public void Product_registration_carries_group_id()
    {
        using var store = new SqliteHubStore("Data Source=:memory:;Pooling=False");
        var catalog = new HubProductCatalogService(store);

        Assert.Equal("CREATED", catalog.CreateProductGroup(S1, D1, "op-g1", "G1", null, "غذا"));

        var product = catalog.Register(S1, D1, "op-p1",
            new ProductRegistrationInput("نان سنگک", 10_000, 1_000, "prod-1", GroupId: "G1"));
        Assert.Equal(ProductRegistrationStatus.Created, product.Status);
        Assert.Equal("G1", product.Product!.GroupId);
        Assert.Equal("G1", store.GetProduct(S1, "prod-1")!.GroupId);

        // Omitting the group leaves it null.
        var noGroup = catalog.Register(S1, D1, "op-p2",
            new ProductRegistrationInput("نان بربری", 12_000, 500, "prod-2"));
        Assert.Null(noGroup.Product!.GroupId);
        Assert.Null(store.GetProduct(S1, "prod-2")!.GroupId);
    }
}
