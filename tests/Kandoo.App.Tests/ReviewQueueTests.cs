using System.Text.Json;
using Kandoo.App;
using Xunit;

namespace Kandoo.App.Tests;

public sealed class ReviewQueueTests
{
    [Fact]
    public async Task C40_DefaultFakeModeIsZeroConfigAndClearlyDemo()
    {
        var coordinator = new ReviewQueueCoordinator(
            new FakeReviewQueueDataSource(),
            _ => throw new InvalidOperationException("Real source must not be constructed."));

        Assert.True(coordinator.IsDemo);
        var rows = await coordinator.LoadAsync(settings: null);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.StartsWith("DEMO-", row.InvoiceNumber));
        Assert.All(rows, row => Assert.EndsWith("ریال", row.AmountDisplay));
    }

    [Fact]
    public async Task C40_RealAndFakeSwitchingSelectsTheExpectedSource()
    {
        var expected = new[]
        {
            new ReviewQueueRow("real-1", "REAL-1", "1405/08/10", "REVIEW", 1000)
        };
        var real = new StubSource(expected);
        HubConnectionSettings? observed = null;
        var coordinator = new ReviewQueueCoordinator(
            new FakeReviewQueueDataSource(),
            settings =>
            {
                observed = settings;
                return real;
            });
        var settings = new HubConnectionSettings("https://hub.invalid", Guid.NewGuid(), "session");

        coordinator.UseReal();
        var realRows = await coordinator.LoadAsync(settings);
        Assert.False(coordinator.IsDemo);
        Assert.Same(settings, observed);
        Assert.Equal(expected, realRows);
        Assert.Equal(1, real.CallCount);

        coordinator.UseFake();
        var fakeRows = await coordinator.LoadAsync(settings: null);
        Assert.True(coordinator.IsDemo);
        Assert.Equal(2, fakeRows.Count);
        Assert.Equal(1, real.CallCount);
    }

    [Fact]
    public void C40_MapsCertifiedReviewFieldsToJalaliRowsWithExplicitRials()
    {
        var rows = ReviewQueueMapper.Map(Root("""
            {
              "status": "OK",
              "items": [
                {
                  "review_id": "review-1",
                  "queue_status": "REVIEW",
                  "source": {
                    "invoice_id": "external-1",
                    "header": {
                      "invoice_number": "POS-0042",
                      "captured_at": "2026-02-03T04:05:06Z"
                    },
                    "summary": { "payable": 125000 }
                  }
                },
                {
                  "review_id": "review-2",
                  "queue_status": "ISSUED",
                  "source": {
                    "invoice_id": "external-2",
                    "header": {
                      "invoice_number": null,
                      "captured_at": null
                    },
                    "summary": { "payable": 98000 }
                  }
                }
              ]
            }
            """));

        Assert.Equal(2, rows.Count);
        Assert.Equal("POS-0042", rows[0].InvoiceNumber);
        Assert.Equal("1404/11/14", rows[0].JalaliDate);
        Assert.Equal("REVIEW", rows[0].QueueStatus);
        Assert.Equal("125,000 ریال", rows[0].AmountDisplay);
        Assert.Equal("external-2", rows[1].InvoiceNumber);
        Assert.Equal(ReviewQueueMapper.MissingDateMarker, rows[1].JalaliDate);
        Assert.Equal("ISSUED", rows[1].QueueStatus);
    }

    [Fact]
    public void C40_MalformedOrUnsupportedResponsesFailClosed()
    {
        Assert.Throws<JsonException>(() => ReviewQueueMapper.Map(Root("{}")));
        Assert.Throws<JsonException>(() => ReviewQueueMapper.Map(Root("""
            {
              "items": [{
                "review_id": "review-1",
                "queue_status": "UNKNOWN",
                "source": {
                  "header": {},
                  "summary": { "payable": 1000 }
                }
              }]
            }
            """)));
        Assert.Throws<JsonException>(() => ReviewQueueMapper.Map(Root("""
            {
              "items": [{
                "review_id": "review-1",
                "queue_status": "REVIEW",
                "source": {
                  "header": {},
                  "summary": {}
                }
              }]
            }
            """)));
    }

    private static JsonElement Root(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class StubSource(IReadOnlyList<ReviewQueueRow> rows) : IReviewQueueDataSource
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<ReviewQueueRow>> LoadAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(rows);
        }
    }
}
