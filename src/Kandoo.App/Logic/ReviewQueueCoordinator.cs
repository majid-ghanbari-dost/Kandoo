namespace Kandoo.App;

public sealed class ReviewQueueCoordinator
{
    private readonly IReviewQueueDataSource _fake;
    private readonly Func<HubConnectionSettings, IReviewQueueDataSource> _realFactory;

    public ReviewQueueCoordinator(
        IReviewQueueDataSource fake,
        Func<HubConnectionSettings, IReviewQueueDataSource> realFactory)
    {
        _fake = fake ?? throw new ArgumentNullException(nameof(fake));
        _realFactory = realFactory ?? throw new ArgumentNullException(nameof(realFactory));
    }

    public ReviewDataMode Mode { get; private set; } = ReviewDataMode.Fake;

    public bool IsDemo => Mode == ReviewDataMode.Fake;

    public void UseFake() => Mode = ReviewDataMode.Fake;

    public void UseReal() => Mode = ReviewDataMode.Real;

    public async Task<IReadOnlyList<ReviewQueueRow>> LoadAsync(
        HubConnectionSettings? settings,
        CancellationToken cancellationToken = default)
    {
        if (Mode == ReviewDataMode.Fake)
        {
            return await _fake.LoadAsync(cancellationToken);
        }

        if (settings is null)
        {
            throw new ArgumentException("Real mode requires Hub connection settings.", nameof(settings));
        }

        return await _realFactory(settings).LoadAsync(cancellationToken);
    }
}
