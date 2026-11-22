namespace Kandoo.App;

public enum ReviewDataMode
{
    Fake,
    Real
}

public sealed record ReviewQueueRow(
    string ReviewId,
    string InvoiceNumber,
    string JalaliDate,
    string QueueStatus,
    long AmountRials)
{
    public string AmountDisplay => FormattableString.Invariant($"{AmountRials:N0} ریال");
}

public sealed record HubConnectionSettings(string BaseUrl, Guid StoreId, string SessionToken);

public interface IReviewQueueDataSource
{
    Task<IReadOnlyList<ReviewQueueRow>> LoadAsync(CancellationToken cancellationToken);
}
