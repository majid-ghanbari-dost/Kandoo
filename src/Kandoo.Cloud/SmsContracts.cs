using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using Kandoo.Core;

namespace Kandoo.Cloud;

public sealed record SmsSendRequest(
    Guid StoreId,
    string OperationId,
    Guid InvoiceId,
    string RecipientPhone,
    string InvoiceLink);

public sealed record SmsSubscriptionMetadata(
    Guid StoreId,
    bool IsActive,
    int QuotaLimit,
    DateTimeOffset QuotaPeriodStart);

public sealed record SmsAbusePolicy(
    int PerInvoicePerMinute,
    int PerRecipientPerMinute,
    int MaxMessageUtf8Bytes)
{
    public static SmsAbusePolicy Default { get; } = new(30, 5, 1024);
}

public sealed record SmsDelivery(
    Guid StoreId,
    string OperationId,
    Guid InvoiceId,
    string RecipientPhone,
    string InvoiceLink,
    string TemplateName,
    string Body,
    string ProviderMessageId,
    DateTimeOffset CreatedAt,
    DateTimeOffset SentAt,
    int QuotaRemaining,
    int RateRemaining);

public sealed record SmsSendResult(
    string Status,
    SmsDelivery? Delivery = null,
    bool IdempotentReplay = false,
    int? RetryAfterMs = null);

public sealed record SmsStoreCounter(
    Guid StoreId,
    DateTimeOffset QuotaPeriodStart,
    int UsedCount,
    int SentCount);

public sealed record SmsProviderRequest(
    Guid StoreId,
    string OperationId,
    Guid InvoiceId,
    string RecipientPhone,
    string Body,
    string RequestFingerprint);

public sealed record SmsProviderResult(string ProviderMessageId, string Body);

public interface ISmsProvider
{
    Task<SmsProviderResult> SendAsync(SmsProviderRequest request, CancellationToken cancellationToken = default);
}

public interface ISmsSubscriptionMetadata
{
    SmsSubscriptionMetadata? Get(Guid storeId);
}

public sealed record SmsBeginResult(SmsSendResult? Completed);

public interface ISmsDeliveryStore
{
    /// <summary>Null Completed means a new PENDING reservation was committed.</summary>
    SmsBeginResult BeginSend(
        SmsSendRequest request,
        string body,
        string requestFingerprint,
        SmsSubscriptionMetadata subscription,
        SmsAbusePolicy policy,
        DateTimeOffset now);

    SmsSendResult CompleteSend(
        SmsSendRequest request,
        string body,
        string requestFingerprint,
        SmsProviderResult providerResult,
        DateTimeOffset now);

    SmsStoreCounter? GetCounter(Guid storeId);
}

public static class SmsMessageTemplates
{
    public const string PersianInvoiceV1 = "invoice_fa_v1";

    public static string Render(string storeName, long displayNumber, string invoiceLink)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceLink);
        if (displayNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(displayNumber));
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"فروشگاه {storeName}\nصورتحساب شماره {displayNumber}\nمشاهده صورتحساب: {invoiceLink}");
    }
}

/// <summary>Deterministic, process-local D4 stub. It has no network or credential path.</summary>
public sealed class DeterministicStubSmsProvider : ISmsProvider
{
    private readonly object _gate = new();
    private readonly Dictionary<(Guid StoreId, string OperationId), SmsProviderResult> _messages = new();
    private readonly Dictionary<(Guid StoreId, string OperationId), string> _fingerprints = new();

    public int CaptureCount
    {
        get
        {
            lock (_gate)
            {
                return _messages.Count;
            }
        }
    }

    public IReadOnlyList<SmsProviderRequest> Captures
    {
        get
        {
            lock (_gate)
            {
                return _captures.ToArray();
            }
        }
    }

    private readonly List<SmsProviderRequest> _captures = new();

    public Task<SmsProviderResult> SendAsync(SmsProviderRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var key = (request.StoreId, request.OperationId);
            if (_messages.TryGetValue(key, out var existing))
            {
                if (!string.Equals(_fingerprints[key], request.RequestFingerprint, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("OPERATION_ID_CONFLICT");
                }

                return Task.FromResult(existing);
            }

            var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{request.StoreId:D}:{request.OperationId}"))).ToLowerInvariant()[..24];
            var result = new SmsProviderResult($"stub-{identity}", request.Body);
            _messages.Add(key, result);
            _fingerprints.Add(key, request.RequestFingerprint);
            _captures.Add(request);
            return Task.FromResult(result);
        }
    }
}
