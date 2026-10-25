using System.Text;
using System.Text.Json;
using Kandoo.Core;

namespace Kandoo.Cloud;

public sealed class SmsService
{
    private readonly IPublicInvoiceStore _publicInvoices;
    private readonly ISmsSubscriptionMetadata _subscriptions;
    private readonly ISmsDeliveryStore _deliveries;
    private readonly ISmsProvider _provider;
    private readonly IClock _clock;
    private readonly SmsAbusePolicy _policy;

    public SmsService(
        IPublicInvoiceStore publicInvoices,
        ISmsSubscriptionMetadata subscriptions,
        ISmsDeliveryStore deliveries,
        ISmsProvider provider,
        IClock clock,
        SmsAbusePolicy? policy = null)
    {
        _publicInvoices = publicInvoices;
        _subscriptions = subscriptions;
        _deliveries = deliveries;
        _provider = provider;
        _clock = clock;
        _policy = policy ?? SmsAbusePolicy.Default;
        if (_policy.PerInvoicePerMinute <= 0 || _policy.PerRecipientPerMinute <= 0 || _policy.MaxMessageUtf8Bytes <= 0)
        {
            throw new ArgumentException("SMS abuse limits must be positive.", nameof(policy));
        }
    }

    public async Task<SmsSendResult> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default)
    {
        if (request.StoreId == Guid.Empty || request.InvoiceId == Guid.Empty || !ValidOperationId(request.OperationId))
        {
            return new SmsSendResult("VALIDATION_ERROR");
        }

        if (!TryToken(request.InvoiceLink, out var token))
        {
            return new SmsSendResult("VALIDATION_ERROR");
        }

        string phone;
        try
        {
            phone = PhoneNormalizer.Normalize(request.RecipientPhone);
        }
        catch (ArgumentException)
        {
            return new SmsSendResult("VALIDATION_ERROR");
        }

        var invoice = _publicInvoices.GetPublicInvoice(token);
        if (invoice is null || invoice.View.DigitalInvoice.StoreId != request.StoreId ||
            invoice.View.DigitalInvoice.InvoiceId != request.InvoiceId)
        {
            return new SmsSendResult("INVOICE_NOT_FOUND");
        }

        var subscription = _subscriptions.Get(request.StoreId);
        if (subscription is null || !subscription.IsActive || subscription.StoreId != request.StoreId ||
            subscription.QuotaLimit < 0)
        {
            return new SmsSendResult("SMS_SUBSCRIPTION_INACTIVE");
        }

        var body = SmsMessageTemplates.Render(invoice.View.StoreName, invoice.View.DisplayNumber, request.InvoiceLink);
        if (Encoding.UTF8.GetByteCount(body) > _policy.MaxMessageUtf8Bytes)
        {
            return new SmsSendResult("MESSAGE_TOO_LARGE");
        }

        var normalized = request with { RecipientPhone = phone };
        var fingerprint = CanonicalJson.Sha256Hex(JsonSerializer.SerializeToElement(new
        {
            store_id = normalized.StoreId,
            operation_id = normalized.OperationId,
            invoice_id = normalized.InvoiceId,
            recipient_phone = normalized.RecipientPhone,
            invoice_link = normalized.InvoiceLink,
            template_name = SmsMessageTemplates.PersianInvoiceV1,
            body
        }, CloudJson.Options));
        var now = _clock.UtcNow;
        var begin = _deliveries.BeginSend(normalized, body, fingerprint, subscription, _policy, now);
        if (begin.Completed is not null)
        {
            return begin.Completed;
        }

        var providerResult = await _provider.SendAsync(new SmsProviderRequest(
            normalized.StoreId,
            normalized.OperationId,
            normalized.InvoiceId,
            normalized.RecipientPhone,
            body,
            fingerprint), cancellationToken);
        return _deliveries.CompleteSend(normalized, body, fingerprint, providerResult, _clock.UtcNow);
    }

    private static bool ValidOperationId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
        {
            return false;
        }

        return value.All(character => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '.');
    }

    private static bool TryToken(string? link, out string token)
    {
        token = string.Empty;
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            !uri.AbsolutePath.StartsWith("/i/", StringComparison.Ordinal))
        {
            return false;
        }

        token = uri.AbsolutePath[3..];
        return PublicTokenGenerator.IsValid(token);
    }
}
