namespace Kandoo.Core;

public enum InvoiceStatus
{
    Draft,
    Extracted,
    Validated,
    Issued,
    Revoked,
    Superseded
}

public enum RetirementReason
{
    Cancel,
    Timeout
}

public sealed record InvoiceItem(
    Guid InvoiceItemId,
    Guid StoreId,
    int LineNumber,
    string Description,
    long QuantityMillis,
    long UnitPriceRials,
    long LineTotalRials,
    string? ProductId = null);

public sealed class Invoice
{
    private readonly List<InvoiceItem> _items = new();

    internal Invoice(Guid invoiceId, Guid storeId, string createdOperationId, Guid createdByDeviceId, DateTimeOffset createdAt, string? externalInvoiceNumber)
    {
        if (invoiceId == Guid.Empty || storeId == Guid.Empty || createdByDeviceId == Guid.Empty || string.IsNullOrWhiteSpace(createdOperationId))
        {
            throw new ArgumentException("Invoice identity, store, device, and creation operation are required.");
        }

        InvoiceId = invoiceId;
        StoreId = storeId;
        CreatedOperationId = createdOperationId;
        CreatedByDeviceId = createdByDeviceId;
        CreatedAt = createdAt;
        ExternalInvoiceNumber = externalInvoiceNumber;
        Status = InvoiceStatus.Draft;
    }

    public Guid InvoiceId { get; }
    public Guid StoreId { get; }
    public string CreatedOperationId { get; }
    public Guid CreatedByDeviceId { get; }
    public DateTimeOffset CreatedAt { get; }
    public string? ExternalInvoiceNumber { get; }
    public long? DisplayNumber { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public DateTimeOffset? RetiredAt { get; private set; }
    public RetirementReason? RetirementReason { get; private set; }
    public DateTimeOffset? IssuedAt { get; private set; }
    public string? IssuedPayload { get; private set; }
    public string? IssuedContentSha256 { get; private set; }
    public Guid? CustomerId { get; private set; }
    public IReadOnlyList<InvoiceItem> Items => _items.ToArray();

    public void AddItem(InvoiceItem item)
    {
        EnsureMutable();
        if (item.StoreId != StoreId || item.InvoiceItemId == Guid.Empty || item.LineNumber <= 0 ||
            string.IsNullOrWhiteSpace(item.Description) || item.QuantityMillis <= 0 ||
            item.UnitPriceRials < 0 || item.LineTotalRials < 0)
        {
            throw new InvalidOperationException("An invoice item must be valid and belong to the invoice store.");
        }

        if (_items.Any(i => i.LineNumber == item.LineNumber || i.InvoiceItemId == item.InvoiceItemId))
        {
            throw new InvalidOperationException("Invoice item IDs and line numbers must be unique.");
        }

        _items.Add(item);
    }

    public void ChangeCustomer(Guid? customerId)
    {
        EnsureMutable();
        CustomerId = customerId;
    }

    public void ChangeDisplayNumber(long displayNumber) =>
        throw new InvalidOperationException("Display numbers are allocated only by the Hub at successful ISSUE.");

    public void MarkValidated()
    {
        EnsureMutable();
        Status = InvoiceStatus.Validated;
    }

    public void ReplaceIssuedPayload(string payload, string digest)
    {
        EnsureMutable();
        throw new InvalidOperationException("An issued payload can only be created during ISSUE.");
    }

    internal void EnsureIssuable()
    {
        if (RetiredAt is not null || Status is InvoiceStatus.Issued or InvoiceStatus.Revoked or InvoiceStatus.Superseded)
        {
            throw new InvalidOperationException("A retired or terminal invoice cannot be issued.");
        }

        if (_items.Count == 0)
        {
            throw new InvalidOperationException("At least one valid item is required before ISSUE.");
        }
    }

    internal void Issue(long displayNumber, DateTimeOffset issuedAt, string payload, string digest)
    {
        EnsureIssuable();
        if (displayNumber <= 0 || string.IsNullOrWhiteSpace(payload) || string.IsNullOrWhiteSpace(digest))
        {
            throw new ArgumentException("A positive number and immutable issued snapshot are required.");
        }

        DisplayNumber = displayNumber;
        IssuedAt = issuedAt;
        IssuedPayload = payload;
        IssuedContentSha256 = digest;
        Status = InvoiceStatus.Issued;
    }

    internal void Retire(DateTimeOffset retiredAt, RetirementReason reason)
    {
        EnsureMutable();
        RetiredAt = retiredAt;
        RetirementReason = reason;
    }

    public void Revoke()
    {
        if (Status != InvoiceStatus.Issued)
        {
            throw new InvalidOperationException("Only an issued invoice can be revoked.");
        }

        Status = InvoiceStatus.Revoked;
    }

    public void Supersede()
    {
        if (Status != InvoiceStatus.Issued)
        {
            throw new InvalidOperationException("Only an issued invoice can be superseded.");
        }

        Status = InvoiceStatus.Superseded;
    }

    private void EnsureMutable()
    {
        if (Status is InvoiceStatus.Issued or InvoiceStatus.Revoked or InvoiceStatus.Superseded || RetiredAt is not null)
        {
            throw new InvalidOperationException("Issued or retired invoice content is immutable.");
        }
    }
}
