using System.Text.Json;
using Kandoo.Core;
using Kandoo.Hub;
using Xunit;

namespace Kandoo.Hub.Tests;

public sealed class SqliteHubStoreTests
{
    private static readonly Guid S1 = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid S2 = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid D1 = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void C04_OperationIdempotencySurvivesRestart()
    {
        using var database = new TempDatabase();
        var request = Request("op-restart", "product.test");
        var firstCallbacks = 0;
        var replayCallbacks = 0;

        using (var store = new SqliteHubStore(database.ConnectionString))
        {
            var first = store.Execute(request, transaction =>
            {
                firstCallbacks++;
                transaction.SaveProduct(new Product(S1, "prod-1", "Sample", "sample", "sample", 1000, 1));
                return "original-response";
            });
            Assert.Equal(OperationExecutionStatus.Succeeded, first.Status);
            Assert.Equal(1L, first.OperationSeq);
        }

        using (var reopened = new SqliteHubStore(database.ConnectionString))
        {
            var replay = reopened.Execute(request, transaction =>
            {
                replayCallbacks++;
                return "new-response";
            });

            Assert.Equal(OperationExecutionStatus.Replayed, replay.Status);
            Assert.Equal("original-response", replay.Response);
            Assert.Equal(1L, replay.OperationSeq);
            Assert.Equal(0, replayCallbacks);
            Assert.Equal(1, firstCallbacks);
            Assert.Equal(1, reopened.LoadOperationLog().Count(S1));
            Assert.Equal(1, reopened.LoadProductCatalog().Count(S1));
        }
    }

    [Fact]
    public void C04_CrashDuringNumberAllocationRollsBackAndNextIssuesStayGapless()
    {
        using var database = new TempDatabase();
        var firstInvoice = Guid.NewGuid();
        var secondInvoice = Guid.NewGuid();
        var crashRequest = Request("op-crash", "invoice.issue");

        using (var store = new SqliteHubStore(database.ConnectionString))
        {
            store.CreateDraft(Request("op-create-1", "invoice.draft_create"), firstInvoice, T0, new[] { Item() });
            Assert.Equal(0L, store.GetDisplayNumberHighWatermark(S1));

            Assert.Throws<SimulatedCrashException>(() => store.Execute(crashRequest, transaction =>
            {
                // Simulate a process failure after the counter write but before commit.
                transaction.SetDisplayNumberHighWatermark(S1, 1);
                throw new SimulatedCrashException();
            }));
        }

        using (var reopened = new SqliteHubStore(database.ConnectionString))
        {
            Assert.Equal(0L, reopened.GetDisplayNumberHighWatermark(S1));
            var stillDraft = reopened.GetInvoice(S1, firstInvoice)!;
            Assert.Equal(InvoiceStatus.Draft, stillDraft.Status);
            Assert.Null(stillDraft.DisplayNumber);

            var issued = reopened.IssueInvoice(Request("op-issue-1", "invoice.issue"), firstInvoice, T0.AddMinutes(1));
            Assert.Equal(OperationExecutionStatus.Succeeded, issued.Status);
            Assert.Equal(1L, reopened.GetInvoice(S1, firstInvoice)!.DisplayNumber);
            Assert.Equal(1L, reopened.GetDisplayNumberHighWatermark(S1));

            reopened.CreateDraft(Request("op-create-2", "invoice.draft_create"), secondInvoice, T0.AddMinutes(2), new[] { Item() });
            reopened.IssueInvoice(Request("op-issue-2", "invoice.issue"), secondInvoice, T0.AddMinutes(3));
            Assert.Equal(2L, reopened.GetInvoice(S1, secondInvoice)!.DisplayNumber);
            Assert.Equal(2L, reopened.GetDisplayNumberHighWatermark(S1));
            Assert.Equal(4, reopened.LoadOperationLog().Count(S1));
        }
    }

    [Fact]
    public void C24_HubCrashMidIssueRecoversWithoutPartialOrDuplicateIssue()
    {
        using var database = new TempDatabase();
        var firstInvoice = Guid.NewGuid();
        var secondInvoice = Guid.NewGuid();
        var issueRequest = Request("op-c24-crash-issue", "invoice.issue");

        using (var store = new SqliteHubStore(database.ConnectionString))
        {
            store.CreateDraft(Request("op-c24-create-1", "invoice.draft_create"), firstInvoice, T0, new[] { Item() });
            Assert.Throws<SimulatedCrashException>(() => store.Execute(issueRequest, transaction =>
            {
                var invoice = transaction.GetInvoice(S1, firstInvoice)!;
                var service = new InvoiceService(new Dictionary<Guid, long>
                {
                    [S1] = transaction.GetDisplayNumberHighWatermark(S1)
                });
                service.Issue(invoice, T0.AddMinutes(1));
                transaction.SaveInvoice(invoice);
                transaction.SetDisplayNumberHighWatermark(S1, service.GetHighWatermark(S1));
                throw new SimulatedCrashException();
            }));
        }

        using (var reopened = new SqliteHubStore(database.ConnectionString))
        {
            Assert.Equal(InvoiceStatus.Draft, reopened.GetInvoice(S1, firstInvoice)!.Status);
            Assert.Null(reopened.GetInvoice(S1, firstInvoice)!.DisplayNumber);
            Assert.Equal(0L, reopened.GetDisplayNumberHighWatermark(S1));
            Assert.Equal(1, reopened.LoadOperationLog().Count(S1));
            Assert.Equal(1, reopened.GetOutboxDepth(S1));

            var recovered = reopened.IssueInvoice(issueRequest, firstInvoice, T0.AddMinutes(2));
            Assert.Equal(OperationExecutionStatus.Succeeded, recovered.Status);
            var issued = reopened.GetInvoice(S1, firstInvoice)!;
            var issuedHash = issued.IssuedContentSha256;
            Assert.Equal(1L, issued.DisplayNumber);
            Assert.Equal(1L, reopened.GetDisplayNumberHighWatermark(S1));

            var replay = reopened.IssueInvoice(issueRequest, firstInvoice, T0.AddDays(1));
            Assert.Equal(OperationExecutionStatus.Replayed, replay.Status);
            Assert.Equal(recovered.Response, replay.Response);
            Assert.Equal(recovered.OperationSeq, replay.OperationSeq);
            Assert.Equal(issuedHash, reopened.GetInvoice(S1, firstInvoice)!.IssuedContentSha256);
            Assert.Equal(2, reopened.LoadOperationLog().Count(S1));
            Assert.Equal(2, reopened.GetOutboxDepth(S1));

            reopened.CreateDraft(Request("op-c24-create-2", "invoice.draft_create"), secondInvoice, T0.AddMinutes(3), new[] { Item() });
            reopened.IssueInvoice(Request("op-c24-issue-2", "invoice.issue"), secondInvoice, T0.AddMinutes(4));
            Assert.Equal(2L, reopened.GetInvoice(S1, secondInvoice)!.DisplayNumber);
            Assert.Equal(2L, reopened.GetDisplayNumberHighWatermark(S1));
            Assert.Equal(4, reopened.LoadOperationLog().Count(S1));
            Assert.Equal(4, reopened.GetOutboxDepth(S1));
        }
    }

    [Fact]
    public void C04_IssuedInvoicesCannotBeUpdated()
    {
        using var store = new SqliteHubStore("Data Source=:memory:;Pooling=False");
        var invoiceId = Guid.NewGuid();
        store.CreateDraft(Request("op-create", "invoice.draft_create"), invoiceId, T0, new[] { Item() });
        store.IssueInvoice(Request("op-issue", "invoice.issue"), invoiceId, T0.AddMinutes(1));
        var issued = store.GetInvoice(S1, invoiceId)!;
        var digest = issued.IssuedContentSha256;

        Assert.Throws<InvalidOperationException>(() => store.SaveInvoice(issued));
        Assert.Throws<InvalidOperationException>(() => issued.AddItem(Item()));
        Assert.Equal(digest, store.GetInvoice(S1, invoiceId)!.IssuedContentSha256);
        Assert.Equal(1L, store.GetInvoice(S1, invoiceId)!.DisplayNumber);
    }

    [Fact]
    public void C04_ProductsInventoryDraftsAndIssuedInvoicesRehydrateWithStoreIsolation()
    {
        using var database = new TempDatabase();
        var invoiceId = Guid.NewGuid();
        var product = new Product(S1, "prod-1", "Sample", "sample", "sample", 1000, 1);
        var inventoryEvent = new InventoryEvent(Guid.NewGuid(), S1, "prod-1", -2000, InventoryEventType.Sale, "op-stock", D1);

        using (var store = new SqliteHubStore(database.ConnectionString))
        {
            store.SaveProduct(product);
            store.AppendInventory(inventoryEvent);
            store.CreateDraft(Request("op-create", "invoice.draft_create"), invoiceId, T0, new[] { Item() });
            store.IssueInvoice(Request("op-issue", "invoice.issue"), invoiceId, T0.AddMinutes(1));
        }

        using (var reopened = new SqliteHubStore(database.ConnectionString))
        {
            Assert.Equal(1, reopened.LoadProductCatalog().Count(S1));
            Assert.Equal(0, reopened.LoadProductCatalog().Count(S2));
            Assert.Equal(-2000L, reopened.LoadInventoryLedger().StockMillis(S1, "prod-1"));
            Assert.Equal(0L, reopened.LoadInventoryLedger().StockMillis(S2, "prod-1"));
            var invoices = reopened.LoadInvoiceService();
            Assert.Equal(1L, invoices.GetHighWatermark(S1));
            Assert.Equal(1L, invoices.Get(S1, invoiceId)!.DisplayNumber);
            Assert.Null(invoices.Get(S2, invoiceId));
            Assert.Equal(2, reopened.LoadOperationLog().Count(S1));
        }
    }

    private static InvoiceItem Item() => new(Guid.NewGuid(), S1, 1, "synthetic item", 1000, 1000, 1000);

    private static OperationRequest Request(string operationId, string operationType)
    {
        using var document = JsonDocument.Parse($"{{\"operation\":\"{operationId}\"}}");
        return new OperationRequest(S1, D1, operationId, operationType, null, document.RootElement.Clone());
    }

    private sealed class SimulatedCrashException : Exception
    {
    }

    private sealed class TempDatabase : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "kandoo-hub-tests", Guid.NewGuid().ToString("N"));

        internal TempDatabase()
        {
            Directory.CreateDirectory(_directory);
            ConnectionString = $"Data Source={Path.Combine(_directory, "hub.db")};Pooling=False";
        }

        internal string ConnectionString { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
                // Best-effort test cleanup only.
            }
        }
    }
}
