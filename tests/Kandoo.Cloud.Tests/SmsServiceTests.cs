using System.Text.Json;
using Kandoo.Core;
using Xunit;

namespace Kandoo.Cloud.Tests;

[Collection("Postgres")]
public sealed class SmsServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 8, 0, 0, TimeSpan.Zero);
    private readonly PostgresTestDatabase _database;

    public SmsServiceTests(PostgresTestDatabase database) => _database = database;

    [Fact]
    public async Task C09_PersianTemplateCarriesTheSingleInvoiceLink()
    {
        var context = CreateContext();
        var result = await context.Service.SendAsync(context.Request());

        Assert.Equal("SENT", result.Status);
        Assert.NotNull(result.Delivery);
        Assert.Equal(SmsMessageTemplates.PersianInvoiceV1, result.Delivery!.TemplateName);
        Assert.Contains("فروشگاه نمونه", result.Delivery.Body);
        Assert.Contains("صورتحساب شماره 7", result.Delivery.Body);
        Assert.Contains("مشاهده صورتحساب:", result.Delivery.Body);
        Assert.Contains(context.Link, result.Delivery.Body);
        var capture = Assert.Single(context.Provider.Captures);
        Assert.Equal(result.Delivery.Body, capture.Body);
        Assert.Equal("+989120000001", capture.RecipientPhone);
    }

    [Fact]
    public async Task C09_RetryHasNoDuplicateEvenAfterServiceRecreation()
    {
        var context = CreateContext();
        var request = context.Request();
        var first = await context.Service.SendAsync(request);
        var replay = await context.Service.SendAsync(request);

        Assert.Equal("SENT", first.Status);
        Assert.Equal("DUPLICATE", replay.Status);
        Assert.True(replay.IdempotentReplay);
        Assert.Equal(first.Delivery!.ProviderMessageId, replay.Delivery!.ProviderMessageId);
        Assert.Equal(1, context.Provider.CaptureCount);

        var newProvider = new DeterministicStubSmsProvider();
        var recreated = new SmsService(
            _database.Store,
            context.Subscriptions,
            new PostgresSmsDeliveryStore(_database.ConnectionString),
            newProvider,
            context.Clock);
        var afterRestart = await recreated.SendAsync(request);
        Assert.Equal("DUPLICATE", afterRestart.Status);
        Assert.Equal(0, newProvider.CaptureCount);
        var counter = context.Deliveries.GetCounter(context.StoreId);
        Assert.Equal(1, counter!.UsedCount);
        Assert.Equal(1, counter!.SentCount);
    }

    [Fact]
    public async Task C09_QuotaExhaustionBlocksBeforeProviderCall()
    {
        var context = CreateContext(quota: 1);
        var first = await context.Service.SendAsync(context.Request("op-quota-1"));
        var exhausted = await context.Service.SendAsync(context.Request("op-quota-2"));

        Assert.Equal("SENT", first.Status);
        Assert.Equal("SMS_QUOTA_EXHAUSTED", exhausted.Status);
        Assert.Equal(1, context.Provider.CaptureCount);
        var counter = context.Deliveries.GetCounter(context.StoreId);
        Assert.Equal(1, counter!.UsedCount);
        Assert.Equal(1, counter!.SentCount);
    }

    [Fact]
    public async Task C09_RateLimitReturnsRetryMetadataAndAllowsSameOperationAfterWindow()
    {
        var context = CreateContext(quota: 10, policy: new SmsAbusePolicy(2, 100, 1024));
        Assert.Equal("SENT", (await context.Service.SendAsync(context.Request("op-rate-1"))).Status);
        Assert.Equal("SENT", (await context.Service.SendAsync(context.Request("op-rate-2"))).Status);
        var limited = await context.Service.SendAsync(context.Request("op-rate-3"));

        Assert.Equal("RATE_LIMITED", limited.Status);
        Assert.True(limited.RetryAfterMs is > 0);
        Assert.Equal(2, context.Provider.CaptureCount);
        Assert.Equal(2, context.Deliveries.GetCounter(context.StoreId)!.UsedCount);

        context.Clock.UtcNow = context.Clock.UtcNow.AddSeconds(61);
        var retry = await context.Service.SendAsync(context.Request("op-rate-3"));
        Assert.Equal("SENT", retry.Status);
        Assert.Equal(3, context.Provider.CaptureCount);
    }

    [Fact]
    public async Task C09_RecipientAbuseLimitSpansInvoicesInTheStore()
    {
        var context = CreateContext(quota: 10, policy: new SmsAbusePolicy(100, 1, 1024));
        Assert.Equal("SENT", (await context.Service.SendAsync(context.Request("op-recipient-1"))).Status);
        var secondInvoice = Guid.NewGuid();
        var secondToken = PublicTokenGenerator.NewToken();
        SeedPublicInvoice(context.StoreId, secondInvoice, secondToken);
        var second = await context.Service.SendAsync(new SmsSendRequest(
            context.StoreId, "op-recipient-2", secondInvoice, "+989120000001",
            $"https://example.test/i/{secondToken}"));

        Assert.Equal("RATE_LIMITED", second.Status);
        Assert.Equal(1, context.Provider.CaptureCount);
    }

    [Fact]
    public async Task C09_OperationFingerprintConflictHasNoProviderEffect()
    {
        var context = CreateContext();
        Assert.Equal("SENT", (await context.Service.SendAsync(context.Request())).Status);
        var changed = context.Request() with { RecipientPhone = "+989120000002" };
        var conflict = await context.Service.SendAsync(changed);

        Assert.Equal("OPERATION_ID_CONFLICT", conflict.Status);
        Assert.Equal(1, context.Provider.CaptureCount);
        Assert.Equal(1, context.Deliveries.GetCounter(context.StoreId)!.UsedCount);
    }

    [Fact]
    public async Task C09_CrossStoreInvoiceLinkIsRejectedBeforeProviderCall()
    {
        var context = CreateContext();
        var request = context.Request() with { StoreId = Guid.NewGuid() };
        var result = await context.Service.SendAsync(request);

        Assert.Equal("INVOICE_NOT_FOUND", result.Status);
        Assert.Equal(0, context.Provider.CaptureCount);
        Assert.Null(context.Deliveries.GetCounter(context.StoreId));
    }

    private TestContext CreateContext(int quota = 100, SmsAbusePolicy? policy = null)
    {
        var storeId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var token = PublicTokenGenerator.NewToken();
        SeedPublicInvoice(storeId, invoiceId, token);
        var subscriptions = new TestSubscriptions();
        subscriptions.Set(new SmsSubscriptionMetadata(storeId, true, quota, T0.AddDays(-1)));
        var deliveries = new PostgresSmsDeliveryStore(_database.ConnectionString);
        var provider = new DeterministicStubSmsProvider();
        var clock = new ManualSmsClock { UtcNow = T0 };
        var service = new SmsService(_database.Store, subscriptions, deliveries, provider, clock, policy);
        return new TestContext(storeId, invoiceId, token, subscriptions, deliveries, provider, clock, service);
    }

    private void SeedPublicInvoice(Guid storeId, Guid invoiceId, string token)
    {
        _database.Store.SeedPublicInvoice(new PublicInvoiceSeed
        {
            StoreId = storeId,
            InvoiceId = invoiceId,
            PublicToken = token,
            StoreName = "فروشگاه نمونه",
            DisplayNumber = 7,
            IssuedAt = T0,
            IssuedPayload = JsonSerializer.SerializeToElement(new { invoice_id = invoiceId }),
            IssuedContentSha256 = new string('a', 64),
            PayableRials = 1000
        });
    }

    private sealed record TestContext(
        Guid StoreId,
        Guid InvoiceId,
        string Token,
        TestSubscriptions Subscriptions,
        PostgresSmsDeliveryStore Deliveries,
        DeterministicStubSmsProvider Provider,
        ManualSmsClock Clock,
        SmsService Service)
    {
        public string Link => $"https://example.test/i/{Token}";
        public SmsSendRequest Request(string operationId = "op-sms-0001") =>
            new(StoreId, operationId, InvoiceId, "+98 912 000 0001", Link);
    }

    private sealed class TestSubscriptions : ISmsSubscriptionMetadata
    {
        private readonly Dictionary<Guid, SmsSubscriptionMetadata> _values = new();
        public void Set(SmsSubscriptionMetadata value) => _values[value.StoreId] = value;
        public SmsSubscriptionMetadata? Get(Guid storeId) => _values.GetValueOrDefault(storeId);
    }

    private sealed class ManualSmsClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; }
    }
}
