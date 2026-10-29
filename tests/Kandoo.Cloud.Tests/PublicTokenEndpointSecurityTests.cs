using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Kandoo.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Kandoo.Cloud.Tests;

[Collection("Postgres")]
public sealed class PublicTokenEndpointSecurityTests
{
    private const string UnknownToken = "AAAAAAAAAAAAAAAAAAAAAA";
    private const string UnauthorizedToken = "BBBBBBBBBBBBBBBBBBBBBB";
    private readonly PostgresTestDatabase _database;

    public PublicTokenEndpointSecurityTests(PostgresTestDatabase database) => _database = database;

    [Fact]
    public void C18_ConstantTimeComparisonHashesBothInputsBeforeEquality()
    {
        var token = PublicTokenGenerator.NewToken();
        var other = PublicTokenGenerator.NewToken();
        Assert.True(PublicTokenSecurity.FixedTimeEquals(token, token));
        Assert.False(PublicTokenSecurity.FixedTimeEquals(token, other));
        Assert.False(PublicTokenSecurity.FixedTimeEquals(token, "short"));
        Assert.True(PublicTokenSecurity.FixedTimeHashEquals(
            PublicTokenSecurity.Hash(token), PublicTokenSecurity.Hash(token)));
    }

    [Fact]
    public async Task C18_UnknownAndUnauthorizedHaveIdenticalResponses()
    {
        using var factory = new SecurityCloudFactory(_database.Store, new UnauthorizedPublicStore(), null);
        using var client = factory.CreateClient();
        using var unknown = await client.GetAsync($"/i/{UnknownToken}?format=json");
        using var unauthorized = await client.GetAsync($"/i/{UnauthorizedToken}?format=json");
        var unknownBody = await unknown.Content.ReadAsStringAsync();
        var unauthorizedBody = await unauthorized.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(unauthorized.StatusCode, unknown.StatusCode);
        Assert.Equal(unauthorizedBody, unknownBody);
        Assert.Equal(unauthorized.Content.Headers.ContentType?.ToString(), unknown.Content.Headers.ContentType?.ToString());
        Assert.Equal("no-store", unknown.Headers.CacheControl?.ToString());
        Assert.Equal("no-store", unauthorized.Headers.CacheControl?.ToString());
        using var body = JsonDocument.Parse(unknownBody);
        Assert.Equal("PUBLIC_INVOICE_NOT_FOUND", body.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain(UnknownToken, unknownBody);
        Assert.DoesNotContain(UnauthorizedToken, unauthorizedBody);
    }

    [Fact]
    public void C18_RateLimitIsPerClientAddressAndRecoversAfterTheWindow()
    {
        var clock = new ManualPublicClock { UtcNow = new DateTimeOffset(2026, 10, 29, 10, 0, 10, TimeSpan.Zero) };
        var limiter = new PublicInvoiceRateLimiter(clock, 2);
        Assert.True(limiter.Check("192.0.2.10").Allowed);
        Assert.True(limiter.Check("192.0.2.10").Allowed);
        var limited = limiter.Check("192.0.2.10");
        Assert.False(limited.Allowed);
        Assert.InRange(limited.RetryAfterSeconds, 1, 60);
        Assert.True(limiter.Check("192.0.2.11").Allowed);

        clock.UtcNow = clock.UtcNow.AddSeconds(61);
        Assert.True(limiter.Check("192.0.2.10").Allowed);
    }

    [Fact]
    public async Task C18_PublicTokenDoesNotAppearInServerLogs()
    {
        var token = PublicTokenGenerator.NewToken();
        _database.Store.SeedPublicInvoice(new PublicInvoiceSeed
        {
            StoreId = Guid.NewGuid(),
            InvoiceId = Guid.NewGuid(),
            PublicToken = token,
            StoreName = "C18 Store",
            DisplayNumber = 1,
            IssuedAt = DateTimeOffset.UtcNow,
            IssuedPayload = JsonSerializer.SerializeToElement(new { invoice_id = Guid.NewGuid(), display_number = 1 }),
            IssuedContentSha256 = new string('a', 64),
            PayableRials = 0
        });
        var logs = new RecordingLoggerProvider();
        using var factory = new SecurityCloudFactory(_database.Store, _database.Store, logs);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/i/{token}?format=json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var unknown = await client.GetAsync($"/i/{UnknownToken}?format=json");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        Assert.DoesNotContain(logs.Messages, message => message.Contains(token, StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Messages, message => message.Contains(UnknownToken, StringComparison.Ordinal));
    }

    private sealed class UnauthorizedPublicStore : IPublicInvoiceStore
    {
        public PublicInvoice? GetPublicInvoice(string token)
        {
            if (token == UnauthorizedToken)
            {
                throw new UnauthorizedAccessException("Synthetic authorization failure.");
            }

            return null;
        }
    }

    private sealed class ManualPublicClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages = new();
        public IReadOnlyList<string> Messages => _messages.ToArray();
        public ILogger CreateLogger(string categoryName) => new Recorder(categoryName, _messages);
        public void Dispose()
        {
        }

        private sealed class Recorder(string category, ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                messages.Enqueue(category + ": " + formatter(state, exception) + " " + exception);
            }
        }
    }

    private sealed class SecurityCloudFactory : WebApplicationFactory<Program>
    {
        private readonly PostgresCloudStore _cloud;
        private readonly IPublicInvoiceStore _public;
        private readonly ILoggerProvider? _logs;

        public SecurityCloudFactory(PostgresCloudStore cloud, IPublicInvoiceStore publicStore, ILoggerProvider? logs)
        {
            _cloud = cloud;
            _public = publicStore;
            _logs = logs;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICloudStore>();
                services.RemoveAll<IPublicInvoiceStore>();
                services.AddSingleton<ICloudStore>(_cloud);
                services.AddSingleton<IPublicInvoiceStore>(_public);
            });
            if (_logs is not null)
            {
                builder.ConfigureLogging(logging => logging.AddProvider(_logs!));
            }
        }
    }
}
