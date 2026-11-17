using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kandoo.Core;

namespace Kandoo.Hub;

/// <summary>Seeds realistic Persian demo data into a Hub store (Order 15, C-80).</summary>
/// <remarks>
/// D-L demo data: about 40 products with group/subgroup and legacy code, about 20
/// customers, about 12 invoices. Products are registered through the public catalog
/// service; invoices are created through the store's draft/issue path so they are
/// genuine Hub domain entities (not raw SQL inserts).
/// </remarks>
public sealed class DemoDataSeeder
{
    private readonly SqliteHubStore _store;
    private readonly HubProductCatalogService _catalog;
    private readonly Guid _storeId;
    private readonly Guid _deviceId;
    private readonly IClock _clock;
    private readonly JsonElement _emptyPayload = JsonDocument.Parse("{}").RootElement.Clone();

    public DemoDataSeeder(SqliteHubStore store, HubProductCatalogService catalog, Guid storeId, Guid deviceId, IClock clock)
    {
        _store = store;
        _catalog = catalog;
        _storeId = storeId;
        _deviceId = deviceId;
        _clock = clock;
    }

    public void Seed()
    {
        SeedProductGroupsAndProducts();
        SeedCustomersAndInvoices();
    }

    // 8 groups x 5 products = 40 demo products (C-80 demo data target).
    private void SeedProductGroupsAndProducts()
    {
        var categories = new (string Group, string? Subgroup, (string Legacy, string Name, long Price, long? Purchase, long Stock)[] Products)[]
        {
            (
                "لبنیات", null,
                new (string Legacy, string Name, long Price, long? Purchase, long Stock)[]
                {
                    ("LAC-001", "شير پاستور", 15000, 12000, 60_000),
                    ("LAC-002", "پنير سفید", 65000, 52000, 24_000),
                    ("LAC-003", "دوغ تقليدي", 12000, 9500, 48_000),
                    ("LAC-004", "ماست عسل", 18000, 14500, 30_000),
                    ("LAC-005", "كيرم مخمر", 28000, 22000, 15_000),
                }
            ),
            (
                "نرم‌افزار", null,
                new (string Legacy, string Name, long Price, long? Purchase, long Stock)[]
                {
                    ("SFC-010", "برنامه حسابداري", 450000, 320000, 5_000),
                    ("SFC-011", "سيستم انبارداري", 380000, 290000, 8_000),
                    ("SFC-012", "برنامه فروش", 520000, 390000, 3_000),
                    ("SFC-013", "برنامه صورتحساب", 310000, 250000, 6_000),
                    ("SFC-014", "برنامه انبار", 290000, 210000, 4_000),
                }
            ),
            (
                "لوازم خانگی", null,
                new (string Legacy, string Name, long Price, long? Purchase, long Stock)[]
                {
                    ("HHA-101", "ماشین لباسشویی", 8_500_000, 6_900_000, 2_000),
                    ("HHA-102", "يخچال دو-دره", 12_000_000, 9_500_000, 1_500),
                    ("HHA-103", "چراغ کف", 3_200_000, 2_600_000, 4_000),
                    ("HHA-104", "ميكسر برقي", 1_800_000, 1_400_000, 9_000),
                    ("HHA-105", "سلة غذا", 2_400_000, 1_900_000, 2_500),
                }
            ),
            (
                "غذا", null,
                new (string Legacy, string Name, long Price, long? Purchase, long Stock)[]
                {
                    ("FOD-201", "نان بربري", 7000, 4500, 120_000),
                    ("FOD-202", "روغن مزه دار", 45000, 34000, 36_000),
                    ("FOD-203", "ماکاروني", 19000, 15000, 52_000),
                    ("FOD-204", "توت فرنگي", 85000, 68000, 11_000),
                    ("FOD-205", "چاي سوهان", 34000, 27000, 28_000),
                }
            ),
            (
                "پوشاک", null,
                new (string Legacy, string Name, long Price, long? Purchase, long Stock)[]
                {
                    ("CLT-301", "پانجامه پارچه اي", 95000, 72000, 18_000),
                    ("CLT-302", "شلوار كتان", 145000, 110000, 14_000),
                    ("CLT-303", "دستپوش", 38000, 29000, 45_000),
                    ("CLT-304", "باندانا", 22000, 16500, 60_000),
                    ("CLT-305", "کفش راهپيمايي", 285000, 220000, 7_000),
                }
            ),
            (
                "غذاهای سریع", null,
                new (string Legacy, string Name, long Price, long? Purchase, long Stock)[]
                {
                    ("FST-401", "كباب کوبيده", 65000, 52000, 9_000),
                    ("FST-402", "جوجه کباب", 72000, 58000, 7_000),
                    ("FST-403", "دغه غذا", 58000, 46000, 5_000),
                    ("FST-404", "فلسط تماس", 48000, 38000, 13_000),
                    ("FST-405", "برنج زعفراني", 110000, 88000, 6_000),
                }
            ),
            (
                "آرایش و بهداشت", null,
                new (string Legacy, string Name, long Price, long? Purchase, long Stock)[]
                {
                    ("BEA-501", "شامپو طبيعي", 42000, 34000, 22_000),
                    ("BEA-502", "صابون دستشو", 18000, 14000, 40_000),
                    ("BEA-503", "کرم مرطوب ساز", 55000, 44000, 16_000),
                    ("BEA-504", "ماسک پوست", 98000, 79000, 8_000),
                    ("BEA-505", "معجون دندان", 26000, 20000, 33_000),
                }
            ),
            (
                "لوازم تحریر", null,
                new (string Legacy, string Name, long Price, long? Purchase, long Stock)[]
                {
                    ("OFF-601", "قلم رو", 8000, 6000, 85_000),
                    ("OFF-602", "نسخه ميانوس", 24000, 19000, 32_000),
                    ("OFF-603", "فولدر كارتن", 36000, 29000, 19_000),
                    ("OFF-604", "کاغذ A4", 58000, 47000, 44_000),
                    ("OFF-605", "ماركر تابلو", 31000, 25000, 27_000),
                }
            ),
        };

        foreach (var (group, subgroup, products) in categories)
        {
            var groupId = "g-" + Slug(group);
            _ = _catalog.CreateProductGroup(_storeId, _deviceId, "demo-group-" + Slug(group), groupId, null, group);

            if (subgroup is not null)
            {
                var subId = "g-" + Slug(group) + "-" + Slug(subgroup);
                _ = _catalog.CreateProductGroup(_storeId, _deviceId, "demo-group-" + subId, subId, groupId, subgroup);
            }

            var groupIdForProducts = subgroup is not null ? "g-" + Slug(group) + "-" + Slug(subgroup) : groupId;

            foreach (var (legacy, name, price, purchase, stock) in products)
            {
                var input = new ProductRegistrationInput(
                    name,
                    price,
                    stock,
                    ProductId: "p-" + Slug(legacy),
                    Category: null,
                    GroupId: groupIdForProducts,
                    PurchasePriceRials: purchase);

                var result = _catalog.Register(_storeId, _deviceId, "demo-product-" + Slug(legacy), input);
                // EXACT_DUPLICATE / NEAR_DUPLICATE_WARNING are acceptable on re-seed.
                _ = result;
            }
        }
    }

    private void SeedCustomersAndInvoices()
    {
        var phones = new[]
        {
            "+989120000101", "+989120000102", "+989120000103", "+989120000104", "+989120000105",
            "+989120000106", "+989120000107", "+989120000108", "+989120000109", "+989120000110",
            "+989120000111", "+989120000112", "+989120000113", "+989120000114", "+989120000115",
            "+989120000116", "+989120000117", "+989120000118", "+989120000119", "+989120000120",
        };

        var now = _clock.UtcNow;
        var issued = 0;
        for (var i = 0; i < 12 && issued < phones.Length; i++)
        {
            var phone = phones[i];
            var customerId = DeterministicGuid("demo-customer-" + phone);
            var invoiceId = DeterministicGuid("demo-invoice-" + i);

            var items = new[]
            {
                new InvoiceItem(
                    DeterministicGuid("demo-item-" + i + "-a"),
                    _storeId,
                    1,
                    "نمونه کالا الف",
                    2_000,
                    15_000,
                    30_000),
                new InvoiceItem(
                    DeterministicGuid("demo-item-" + i + "-b"),
                    _storeId,
                    2,
                    "نمونه کالا ب",
                    1_000,
                    45_000,
                    45_000),
            };

            var createdAt = now.AddMinutes(-(i * 15));
            var draftRequest = new OperationRequest(
                _storeId, _deviceId, "demo-draft-" + i, "SALE", null, _emptyPayload);

            var draftResult = _store.CreateDraft(draftRequest, invoiceId, createdAt, items, "demo-ext-" + i);
            if (draftResult.Status != OperationExecutionStatus.Succeeded &&
                draftResult.Status != OperationExecutionStatus.Replayed)
            {
                throw new InvalidOperationException("Demo draft creation failed: " + draftResult.ErrorCode);
            }

            var invoice = _store.GetInvoice(_storeId, invoiceId)
                ?? throw new InvalidOperationException("Demo invoice not found after draft.");
            invoice.ChangeCustomer(customerId);
            _store.SaveInvoice(invoice);

            var issueRequest = new OperationRequest(
                _storeId, _deviceId, "demo-issue-" + i, "SALE", null, _emptyPayload);

            var issueResult = _store.IssueInvoice(issueRequest, invoiceId, createdAt.AddMinutes(2));
            if (issueResult.Status != OperationExecutionStatus.Succeeded &&
                issueResult.Status != OperationExecutionStatus.Replayed)
            {
                throw new InvalidOperationException("Demo issue failed: " + issueResult.ErrorCode);
            }

            issued++;
        }
    }

    private static string Slug(string value)
    {
        var sb = new StringBuilder();
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
            else
                sb.Append('-');
        }
        return sb.ToString().ToLowerInvariant();
    }

    private static Guid DeterministicGuid(string seed)
    {
        using var md5 = MD5.Create();
        var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(seed));
        return new Guid(bytes);
    }
}
