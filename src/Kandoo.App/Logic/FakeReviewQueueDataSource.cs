using Kandoo.Core;

namespace Kandoo.App;

public sealed class FakeReviewQueueDataSource : IReviewQueueDataSource
{
    private static readonly IReadOnlyList<ReviewQueueRow> DemoRows = new[]
    {
        new ReviewQueueRow(
            "demo-review-001", "DEMO-1405-001",
            JalaliDateConverter.Format(new DateTimeOffset(2026, 10, 31, 9, 0, 0, TimeSpan.Zero)),
            "REVIEW", 245000),
        new ReviewQueueRow(
            "demo-review-002", "DEMO-1405-002",
            JalaliDateConverter.Format(new DateTimeOffset(2026, 11, 1, 10, 30, 0, TimeSpan.Zero)),
            "ISSUED", 98000)
    };

    public Task<IReadOnlyList<ReviewQueueRow>> LoadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(DemoRows);
}
