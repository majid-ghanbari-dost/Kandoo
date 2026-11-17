using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Kandoo.Cloud.Tests;

[Collection("Postgres")]
public sealed class CustomerAccountTests : IDisposable
{
    private static int _phoneSequence;
    private readonly PostgresTestDatabase _database;
    private readonly DeterministicStubSmsProvider _provider = new();
    private readonly ManualClock _clock = new();
    private readonly CloudFactory _factory;
    private readonly HttpClient _client;

    public CustomerAccountTests(PostgresTestDatabase database)
    {
        _database = database;
        _factory = new CloudFactory(database.Store, new PostgresCustomerAccountStore(database.ConnectionString, database.Store), _provider, _clock);
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task C11_OtpExpiresWithoutCreatingAccess()
    {
        var phone = NewPhone();
        var challenge = await Issue(phone, "op-expiry");
        Assert.Equal(1, _provider.CaptureCount);
        _clock.UtcNow = _clock.UtcNow.AddSeconds(301);

        using var response = await Verify(challenge.ChallengeId, challenge.Code);
        Assert.Equal((HttpStatusCode)410, response.StatusCode);
        using var body = await ReadBody(response);
        Assert.Equal("OTP_EXPIRED", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task C11_WrongCodeAttemptsAreLimited()
    {
        var phone = NewPhone();
        var challenge = await Issue(phone, "op-attempts");
        var wrong = challenge.Code == "000000" ? "000001" : "000000";
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var response = await Verify(challenge.ChallengeId, wrong);
            Assert.Equal(attempt < 5 ? HttpStatusCode.BadRequest : (HttpStatusCode)429, response.StatusCode);
            using var body = await ReadBody(response);
            Assert.Equal(attempt < 5 ? "OTP_INVALID" : "OTP_ATTEMPTS_EXCEEDED", body.RootElement.GetProperty("code").GetString());
        }

        using var correct = await Verify(challenge.ChallengeId, challenge.Code);
        Assert.Equal((HttpStatusCode)429, correct.StatusCode);
        using var correctBody = await ReadBody(correct);
        Assert.Equal("OTP_ATTEMPTS_EXCEEDED", correctBody.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task C11_PerNumberRateLimitAndDuplicateIssueHaveNoExtraSms()
    {
        var phone = NewPhone();
        using var first = await IssueRaw(phone, "op-rate-1");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var duplicate = await IssueRaw(phone, "op-rate-1");
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        using var duplicateBody = await ReadBody(duplicate);
        Assert.Equal("DUPLICATE", duplicateBody.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, _provider.CaptureCount);

        using var second = await IssueRaw(phone, "op-rate-2");
        using var third = await IssueRaw(phone, "op-rate-3");
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(HttpStatusCode.Created, third.StatusCode);
        using var limited = await IssueRaw(phone, "op-rate-4");
        Assert.Equal((HttpStatusCode)429, limited.StatusCode);
        using var limitedBody = await ReadBody(limited);
        Assert.Equal("RATE_LIMITED", limitedBody.RootElement.GetProperty("code").GetString());
        Assert.True(limitedBody.RootElement.GetProperty("retry_hint").GetProperty("retry_after_ms").GetInt32() > 0);
        Assert.Equal(3, _provider.CaptureCount);

        _clock.UtcNow = _clock.UtcNow.AddSeconds(901);
        using var afterWindow = await IssueRaw(phone, "op-rate-4");
        Assert.Equal(HttpStatusCode.Created, afterWindow.StatusCode);
        Assert.Equal(4, _provider.CaptureCount);
    }

    [Fact]
    public async Task C11_VerifiedPhoneListsOnlyItsInvoicesAcrossStoresAndClaimsAtomically()
    {
        var firstPhone = NewPhone();
        var secondPhone = NewPhone();
        var first = await Register(firstPhone, "op-register-1");
        var second = await Register(secondPhone, "op-register-2");
        var invoiceA = SeedInvoice(firstPhone, "Store A");
        var invoiceB = SeedInvoice(firstPhone, "Store B");
        var invoiceC = SeedInvoice(secondPhone, "Store C");
        var invoiceD = SeedInvoice(null, "Store D");

        var firstItems = await ListInvoiceIds(first.Token);
        Assert.Equal(2, firstItems.Count);
        Assert.Contains(invoiceA.InvoiceId, firstItems);
        Assert.Contains(invoiceB.InvoiceId, firstItems);
        Assert.DoesNotContain(invoiceC.InvoiceId, firstItems);
        Assert.DoesNotContain(invoiceD.InvoiceId, firstItems);
        var secondItems = await ListInvoiceIds(second.Token);
        Assert.Equal(new[] { invoiceC.InvoiceId }, secondItems);

        using var claim = await Claim(first.Token, invoiceD.PublicToken!);
        Assert.Equal(HttpStatusCode.Created, claim.StatusCode);
        using var duplicate = await Claim(first.Token, invoiceD.PublicToken!);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Contains(invoiceD.InvoiceId, await ListInvoiceIds(first.Token));
        using var conflict = await Claim(second.Token, invoiceD.PublicToken!);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.DoesNotContain(invoiceD.InvoiceId, await ListInvoiceIds(second.Token));
    }

    [Fact]
    public async Task C11_SingleInvoiceTokenStillNeedsNoAccount()
    {
        var invoice = SeedInvoice(NewPhone(), "Public store");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/i/{invoice.PublicToken}?format=json");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(request.Headers.Contains("Authorization"));
        using var body = await ReadBody(response);
        Assert.Equal(invoice.InvoiceId, body.RootElement.GetProperty("digital_invoice").GetProperty("invoice_id").GetGuid());

        using var list = await _client.GetAsync("/v1/customer/invoices");
        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        using var claim = await _client.PostAsync($"/v1/public/invoices/{invoice.PublicToken}/claim", null);
        Assert.Equal(HttpStatusCode.Unauthorized, claim.StatusCode);
    }

    private async Task<Registration> Register(string phone, string operationId)
    {
        var challenge = await Issue(phone, operationId);
        using var response = await Verify(challenge.ChallengeId, challenge.Code);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadBody(response);
        Assert.Equal("VERIFIED", body.RootElement.GetProperty("phone_status").GetString());
        return new Registration(
            body.RootElement.GetProperty("account_id").GetGuid(),
            body.RootElement.GetProperty("access_token").GetString()!);
    }

    private async Task<Challenge> Issue(string phone, string operationId)
    {
        using var response = await IssueRaw(phone, operationId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = await ReadBody(response);
        var challengeId = body.RootElement.GetProperty("challenge_id").GetGuid();
        var capture = _provider.Captures[^1];
        var match = Regex.Match(capture.Body, "(?<code>[0-9]{6})");
        Assert.True(match.Success);
        return new Challenge(challengeId, match.Groups["code"].Value);
    }

    private Task<HttpResponseMessage> IssueRaw(string phone, string operationId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/customer/otp/challenges")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { phone }), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotency-Key", operationId);
        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> Verify(Guid challengeId, string code) =>
        _client.PostAsync(
            $"/v1/customer/otp/challenges/{challengeId:D}/verify",
            new StringContent(JsonSerializer.Serialize(new { code }), Encoding.UTF8, "application/json"));

    private async Task<IReadOnlyList<Guid>> ListInvoiceIds(string token)
    {
        using var request = Authorized(HttpMethod.Get, "/v1/customer/invoices", token);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadBody(response);
        return body.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("invoice_id").GetGuid()).ToArray();
    }

    private Task<HttpResponseMessage> Claim(string accountToken, string publicToken)
    {
        var request = Authorized(HttpMethod.Post, $"/v1/public/invoices/{publicToken}/claim", accountToken);
        return _client.SendAsync(request);
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private PublicInvoiceSeed SeedInvoice(string? phone, string storeName)
    {
        var seed = new PublicInvoiceSeed
        {
            StoreId = Guid.NewGuid(),
            InvoiceId = Guid.NewGuid(),
            PublicToken = PublicTokenGenerator.NewToken(),
            StoreName = storeName,
            DisplayNumber = 7,
            IssuedAt = _clock.UtcNow,
            CustomerPhone = phone,
            PayableRials = 1000
        };
        _database.Store.SeedPublicInvoice(seed);
        return seed;
    }

    private static string NewPhone() => $"+98911{Interlocked.Increment(ref _phoneSequence):D7}";

    private static async Task<JsonDocument> ReadBody(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private sealed record Challenge(Guid ChallengeId, string Code);
    private sealed record Registration(Guid AccountId, string Token);

    private sealed class ManualClock : Kandoo.Core.IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 25, 10, 0, 0, TimeSpan.Zero);
    }

    private sealed class CloudFactory : WebApplicationFactory<Program>
    {
        private readonly PostgresCustomerAccountStore _accountStore;
        private readonly ISmsProvider _provider;
        private readonly ManualClock _clock;

        public CloudFactory(PostgresCloudStore store, PostgresCustomerAccountStore accountStore, ISmsProvider provider, ManualClock clock)
        {
            Store = store;
            _accountStore = accountStore;
            _provider = provider;
            _clock = clock;
        }

        public PostgresCloudStore Store { get; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICloudStore>();
                services.RemoveAll<IPublicInvoiceStore>();
                services.RemoveAll<CustomerAccountService>();
                services.AddSingleton<ICloudStore>(Store);
                services.AddSingleton<IPublicInvoiceStore>(Store);
                services.AddSingleton(new CustomerAccountService(_accountStore, _provider, _clock));
            });
        }
    }
}
