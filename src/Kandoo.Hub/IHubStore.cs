using Kandoo.Core;

namespace Kandoo.Hub;

/// <summary>Persistence primitives used inside one SQLite transaction.</summary>
public interface IHubTransaction : IDisposable
{
    Product? GetProduct(Guid storeId, string productId);
    IReadOnlyList<Product> GetProducts();
    void SaveProduct(Product product);
    IReadOnlyList<ProductOperatorField> GetProductOperatorFields(Guid storeId, string productId);
    void SaveProductOperatorFields(Guid storeId, string productId, IReadOnlyList<ProductOperatorField> fields);
    void AppendInventory(InventoryEvent inventoryEvent);
    IReadOnlyList<InventoryEvent> GetInventoryEvents();
    Invoice? GetInvoice(Guid storeId, Guid invoiceId);
    IReadOnlyList<Invoice> GetInvoices();
    void SaveInvoice(Invoice invoice);
    long GetDisplayNumberHighWatermark(Guid storeId);
    void SetDisplayNumberHighWatermark(Guid storeId, long value);
    IReadOnlyList<OperationRecord> GetOperations();
    DraftLock? GetDraftLock(Guid storeId, Guid lockId);
    IReadOnlyList<DraftLock> GetDraftLocks();
    void SaveDraftLock(DraftLock draftLock);
}

/// <summary>
/// Hub aggregate persistence. Execute is the durable idempotency/atomicity
/// boundary; all callback writes and the operation record commit together.
/// </summary>
public interface IHubStore : IDisposable
{
    OperationResult Execute(OperationRequest request, Func<IHubTransaction, string> action);
    OperationResult CreateDraft(
        OperationRequest request,
        Guid invoiceId,
        DateTimeOffset createdAt,
        IEnumerable<InvoiceItem>? items = null,
        string? externalInvoiceNumber = null);
    OperationResult IssueInvoice(OperationRequest request, Guid invoiceId, DateTimeOffset issuedAt);
    void SaveProduct(Product product);
    void AppendInventory(InventoryEvent inventoryEvent);
    void SaveInvoice(Invoice invoice);
    Invoice? GetInvoice(Guid storeId, Guid invoiceId);
    IReadOnlyList<ReplicationLogEntry> ReadReplicationEntries(Guid storeId);
    long GetDisplayNumberHighWatermark(Guid storeId);
    IReadOnlyList<DraftLock> GetDraftLocks();
    void CompleteTimeout(DraftLock draftLock);
    int GetOutboxDepth(Guid storeId);
    ProductCatalog LoadProductCatalog();
    InventoryLedger LoadInventoryLedger();
    InvoiceService LoadInvoiceService();
    OperationLog LoadOperationLog();
}
