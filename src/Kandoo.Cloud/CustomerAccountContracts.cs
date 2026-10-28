using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Kandoo.Core;

namespace Kandoo.Cloud;

public sealed record CustomerOtpPolicy(
    int CodeLifetimeSeconds,
    int MaximumAttempts,
    int IssuesPerWindow,
    int RateWindowSeconds,
    int SessionLifetimeSeconds)
{
    public static CustomerOtpPolicy Default { get; } = new(300, 5, 3, 900, 2_592_000);
}

public sealed record CustomerAccount(Guid AccountId, string VerifiedPhone, DateTimeOffset VerifiedAt);

public sealed record CustomerOtpReservation(
    string Status,
    Guid ChallengeId,
    DateTimeOffset ExpiresAt,
    int? RetryAfterMs = null);

public sealed record CustomerInvoiceListItem(
    Guid StoreId,
    Guid InvoiceId,
    string StoreName,
    long DisplayNumber,
    string LifecycleStatus,
    DateTimeOffset IssuedAt,
    string PublicToken);

public sealed class CustomerAccountResult
{
    private CustomerAccountResult(int httpStatus, object body)
    {
        HttpStatus = httpStatus;
        Body = body;
    }

    public int HttpStatus { get; }
    public object Body { get; }
    public static CustomerAccountResult Success(int status, object body) => new(status, body);

    public static CustomerAccountResult Failure(int status, string code, string message, string action, params (string Key, object? Value)[] fields)
        => new(status, new ErrorResponse { Code = code, Message = message, RetryHint = CloudErrors.Hint(action, fields) });
}

public interface ICustomerAccountStore
{
    CustomerOtpReservation ReserveOtpIssue(
        string phone,
        string operationId,
        string requestFingerprint,
        Guid challengeId,
        string codeHash,
        DateTimeOffset now,
        CustomerOtpPolicy policy);

    void CompleteOtpIssue(Guid challengeId, string providerMessageId, DateTimeOffset sentAt);
    CustomerAccountResult VerifyOtp(Guid challengeId, string code, DateTimeOffset now, CustomerOtpPolicy policy);
    CustomerAccount? Authenticate(string accessToken, DateTimeOffset now);
    CustomerAccountResult ClaimInvoice(CustomerAccount account, string publicToken, DateTimeOffset now);
    IReadOnlyList<CustomerInvoiceListItem> ListInvoices(CustomerAccount account);
}

public sealed class SystemCloudClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class CustomerAccountService
{
    private readonly ICustomerAccountStore _store;
    private readonly ISmsProvider _sms;
    private readonly IClock _clock;
    private readonly CustomerOtpPolicy _policy;

    public CustomerAccountService(ICustomerAccountStore store, ISmsProvider sms, IClock clock, CustomerOtpPolicy? policy = null)
    {
        _store = store;
        _sms = sms;
        _clock = clock;
        _policy = policy ?? CustomerOtpPolicy.Default;
    }

    public async Task<CustomerAccountResult> IssueOtpAsync(string? phoneText, string? operationId, CancellationToken cancellationToken)
    {
        string phone;
        try
        {
            phone = PhoneNormalizer.Normalize(phoneText ?? string.Empty);
        }
        catch (ArgumentException)
        {
            return CustomerAccountResult.Failure(400, "VALIDATION_ERROR", "A valid phone number is required.", "FIX_REQUEST");
        }

        if (string.IsNullOrWhiteSpace(operationId) || operationId.Length > 128)
        {
            return CustomerAccountResult.Failure(400, "IDEMPOTENCY_KEY_REQUIRED", "A bounded Idempotency-Key is required.", "FIX_REQUEST");
        }

        var challengeId = Guid.NewGuid();
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{phone}\n{operationId}"))).ToLowerInvariant();
        var now = _clock.UtcNow;
        var reservation = _store.ReserveOtpIssue(
            phone, operationId, fingerprint, challengeId, HashCode(challengeId, code), now, _policy);
        switch (reservation.Status)
        {
            case "DUPLICATE":
                return CustomerAccountResult.Success(200, new
                {
                    status = "DUPLICATE",
                    challenge_id = reservation.ChallengeId,
                    expires_at = reservation.ExpiresAt
                });
            case "RATE_LIMITED":
                return CustomerAccountResult.Failure(
                    429, "RATE_LIMITED", "Too many verification codes were requested for this phone.", "RETRY_AFTER",
                    ("retry_after_ms", reservation.RetryAfterMs));
            case "OPERATION_ID_CONFLICT":
                return CustomerAccountResult.Failure(409, "OPERATION_ID_CONFLICT", "The operation key was used for a different request.", "REVIEW");
            case "DELIVERY_INCOMPLETE":
                return CustomerAccountResult.Failure(409, "OTP_DELIVERY_INCOMPLETE", "The reserved challenge requires a new issue operation.", "REAUTHENTICATE");
            case "RESERVED":
                break;
            default:
                throw new InvalidOperationException("Unknown OTP reservation state.");
        }

        var body = SmsMessageTemplates.RenderOtp(code);
        var providerResult = await _sms.SendAsync(new SmsProviderRequest(
            Guid.Empty,
            operationId,
            challengeId,
            phone,
            body,
            fingerprint), cancellationToken);
        _store.CompleteOtpIssue(challengeId, providerResult.ProviderMessageId, _clock.UtcNow);
        return CustomerAccountResult.Success(201, new
        {
            status = "OTP_SENT",
            challenge_id = challengeId,
            expires_at = now.AddSeconds(_policy.CodeLifetimeSeconds)
        });
    }

    public CustomerAccountResult VerifyOtp(Guid challengeId, string? code)
    {
        if (challengeId == Guid.Empty || code is null || code.Length != 6 || code.Any(character => character is < '0' or > '9'))
        {
            return CustomerAccountResult.Failure(400, "OTP_INVALID", "The verification code is invalid.", "FIX_REQUEST");
        }

        return _store.VerifyOtp(challengeId, code, _clock.UtcNow, _policy);
    }

    public CustomerAccount? AuthenticateBearer(string? authorization)
    {
        const string prefix = "Bearer ";
        if (authorization is null || !authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = authorization[prefix.Length..].Trim();
        return token.Length is < 32 or > 256 ? null : _store.Authenticate(token, _clock.UtcNow);
    }

    public CustomerAccountResult ClaimInvoice(CustomerAccount account, string publicToken) =>
        _store.ClaimInvoice(account, publicToken, _clock.UtcNow);

    public IReadOnlyList<CustomerInvoiceListItem> ListInvoices(CustomerAccount account) => _store.ListInvoices(account);

    public static string HashCode(Guid challengeId, string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{challengeId:D}:{code}"))).ToLowerInvariant();

    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    public static string NewAccessToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
