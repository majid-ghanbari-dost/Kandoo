using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kandoo.Core;

/// <summary>Hub-side invoice rules with atomic, per-store issue-only numbering.</summary>
public sealed class InvoiceService
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Invoice> _invoices = new();
    private readonly Dictionary<Guid, long> _displayNumberHighWatermarks;

    public InvoiceService(IDictionary<Guid, long>? restoredHighWatermarks = null)
    {
        _displayNumberHighWatermarks = restoredHighWatermarks is null
            ? new()
            : new Dictionary<Guid, long>(restoredHighWatermarks);
    }

    public Invoice CreateDraft(Guid invoiceId, Guid storeId, string operationId, Guid deviceId, DateTimeOffset createdAt, string? externalInvoiceNumber = null)
    {
        lock (_gate)
        {
            if (_invoices.ContainsKey(invoiceId))
            {
                throw new InvalidOperationException("Invoice IDs are never reused.");
            }

            var invoice = new Invoice(invoiceId, storeId, operationId, deviceId, createdAt, externalInvoiceNumber);
            _invoices.Add(invoiceId, invoice);
            return invoice;
        }
    }

    public Invoice Issue(Invoice invoice, DateTimeOffset issuedAt)
    {
        lock (_gate)
        {
            // Validate before touching the watermark: failed issues consume no number.
            invoice.EnsureIssuable();
            var next = checked(GetHighWatermark(invoice.StoreId) + 1);
            var snapshot = JsonSerializer.SerializeToElement(new IssuedSnapshot(invoice.InvoiceId, next));
            var payload = CanonicalJson.Serialize(snapshot);
            var digest = CanonicalJson.Sha256Hex(snapshot);
            invoice.Issue(next, issuedAt, payload, digest);
            _displayNumberHighWatermarks[invoice.StoreId] = next;
            return invoice;
        }
    }

    public void Cancel(Invoice invoice, DateTimeOffset retiredAt) => invoice.Retire(retiredAt, RetirementReason.Cancel);

    public void Timeout(Invoice invoice, DateTimeOffset retiredAt) => invoice.Retire(retiredAt, RetirementReason.Timeout);

    public long GetHighWatermark(Guid storeId)
    {
        lock (_gate)
        {
            return _displayNumberHighWatermarks.GetValueOrDefault(storeId);
        }
    }

    public Invoice? Get(Guid storeId, Guid invoiceId)
    {
        lock (_gate)
        {
            return _invoices.TryGetValue(invoiceId, out var invoice) && invoice.StoreId == storeId ? invoice : null;
        }
    }

    public int Count => _invoices.Count;

    private sealed record IssuedSnapshot(
        [property: JsonPropertyName("invoice_id")] Guid InvoiceId,
        [property: JsonPropertyName("display_number")] long DisplayNumber);
}
