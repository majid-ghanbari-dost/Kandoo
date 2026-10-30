using System.Text.Json;
using System.Text.Json.Nodes;
using Kandoo.Core;
using Xunit;

namespace Kandoo.Hub.Tests;

public sealed class ModeBReviewServiceTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "kandoo-c16", Guid.NewGuid().ToString("N"));
    private readonly SqliteHubStore _store;
    private readonly ManualHubClock _clock = new() { UtcNow = T0 };
    private readonly ModeBReviewService _service;
    private readonly Guid _storeId = Guid.NewGuid();
    private readonly HubSessionIssued _operator;

    public ModeBReviewServiceTests()
    {
        Directory.CreateDirectory(_directory);
        _store = new SqliteHubStore($"Data Source={Path.Combine(_directory, "hub.db")};Pooling=False", clock: _clock);
        _operator = _store.IssueOwnerSession(
            _storeId, Guid.NewGuid(), Guid.NewGuid(), "c16-operator-session", T0, T0.AddHours(1));
        _service = new ModeBReviewService(_store, new HubOperationService(_store, _store, _clock), _clock);
    }

    [Fact]
    public void C16_CleanGoldenIssuesThroughNativeHubAfterExplicitOperatorPhone()
    {
        var accepted = _service.Accept(_storeId, Golden());
        Assert.Equal("REVIEW", accepted.Status);
        Assert.NotNull(accepted.Review);
        var review = accepted.Review!;
        var attention = Assert.Single(_service.ListAttention(review.ReviewId));
        Assert.Equal("OPERATOR_PHONE_REQUIRED", attention.Reason);
        Assert.Equal(0, _store.LoadOperationLog().Count(_storeId));

        Assert.Equal("REVIEW", _service.SetCustomerPhone(review.ReviewId, "+98 912 000 0101", "owner-c16").Status);
        var issued = _service.Issue(review.ReviewId, _operator.Session);
        Assert.Equal("ISSUED", issued.Status);
        Assert.Equal(1, issued.DisplayNumber);
        Assert.NotNull(issued.InvoiceId);
        var native = _store.GetInvoice(_storeId, issued.InvoiceId!.Value);
        Assert.NotNull(native);
        Assert.Equal(InvoiceStatus.Issued, native!.Status);
        Assert.Equal("inv-e11-b-draft-b-0001", native.ExternalInvoiceNumber);
        Assert.Null(native.CustomerId);
        Assert.Equal("+989120000101", issued.Review!.OperatorPhone);
        Assert.Equal(
            "operator_confirmed",
            issued.Review.Source.GetProperty("provenance").GetProperty("fields").GetProperty("customer").GetProperty("phone").GetString());
        var replay = _service.Issue(review.ReviewId, _operator.Session);
        Assert.True(replay.IdempotentReplay);
        Assert.Equal(issued.InvoiceId, replay.InvoiceId);
    }

    [Fact]
    public void C16_CorrectedValueIsOperatorConfirmedAndClearsItsAttentionCell()
    {
        var root = GoldenNode();
        root["line_items"]![0]!["line_total"] = 900;
        root["attention_cells"] = new JsonArray(new JsonObject
        {
            ["field"] = "line_items/0/line_total",
            ["raw_value"] = "900",
            ["reason"] = "SYNTHETIC_TEST_MISMATCH"
        });
        var accepted = _service.Accept(_storeId, root.ToJsonString());
        var review = accepted.Review!;
        Assert.Contains(_service.ListAttention(review.ReviewId), cell => cell.Field == "line_items/0/line_total");

        var corrected = _service.Correct(
            review.ReviewId, "line_items/0/line_total", JsonSerializer.SerializeToElement(1000), "owner-c16");
        Assert.Equal("REVIEW", corrected.Status);
        Assert.Equal(1000, corrected.Review!.Source.GetProperty("line_items")[0].GetProperty("line_total").GetInt64());
        Assert.Equal(
            "operator_confirmed",
            corrected.Review.Source.GetProperty("provenance").GetProperty("fields").GetProperty("line_items")[0].GetProperty("line_total").GetString());
        Assert.DoesNotContain(_service.ListAttention(review.ReviewId), cell => cell.Field == "line_items/0/line_total");

        _service.SetCustomerPhone(review.ReviewId, "+989120000101", "owner-c16");
        var issued = _service.Issue(review.ReviewId, _operator.Session);
        Assert.Equal("ISSUED", issued.Status);
        Assert.Equal(1, issued.DisplayNumber);
    }

    [Fact]
    public void C16_FailingSumRefusesIssueBeforeAnyNativeOperation()
    {
        var root = GoldenNode();
        root["summary"]!["payable"] = 1001;
        var accepted = _service.Accept(_storeId, root.ToJsonString());
        var review = accepted.Review!;
        _service.SetCustomerPhone(review.ReviewId, "+989120000101", "owner-c16");

        var refused = _service.Issue(review.ReviewId, _operator.Session);
        Assert.Equal("PAYABLE_SUM_MISMATCH", refused.Status);
        Assert.Equal(0, _store.LoadOperationLog().Count(_storeId));
        Assert.Equal("REVIEW", _store.GetModeBReview(review.ReviewId)!.QueueStatus);
    }

    [Fact]
    public void C16_MissingOperatorPhoneRefusesIssueAndCreatesNoCustomerOrDraft()
    {
        var accepted = _service.Accept(_storeId, Golden());
        var review = accepted.Review!;
        var refused = _service.Issue(review.ReviewId, _operator.Session);

        Assert.Equal("OPERATOR_PHONE_REQUIRED", refused.Status);
        Assert.Equal(0, _store.LoadOperationLog().Count(_storeId));
        Assert.Empty(_store.GetDraftLocks());
        Assert.Equal("REVIEW", _store.GetModeBReview(review.ReviewId)!.QueueStatus);
    }

    private static string Golden() => File.ReadAllText(GoldenPath());

    private static JsonObject GoldenNode() => JsonNode.Parse(Golden())!.AsObject();

    private static string GoldenPath()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "tests", "Kandoo.Core.Tests", "Fixtures", "E11", "contracts", "goldens", "valid",
                "draft_mode_b_null_display_number.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate the E-11 draft Mode-B golden.");
    }

    public void Dispose()
    {
        _store.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
