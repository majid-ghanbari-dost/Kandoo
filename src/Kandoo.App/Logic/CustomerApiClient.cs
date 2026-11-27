using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Kandoo.App;

/// <summary>
/// HTTP client for the accepted P-31 PHP Cloud customer API
/// (docs/vectors/customer-api-v1-2-proposed.json — PM-13 ACCEPT).
///
/// Wire rules enforced here (verified against the vectors in tests):
/// - POST issue: content-type application/json, Idempotency-Key header, X-Kandoo-Device header.
/// - POST verify: X-Kandoo-Device header (sessions are bound to the issuing device).
/// - list/detail/logout: Authorization Bearer session token; no cookies, no CORS.
/// - Every response body is the {"status": ...} envelope; required subsets per vector.
/// </summary>
public sealed class CustomerApiClient : IDisposable
{
    public const string DeviceHeaderName = "x-kandoo-device";
    public const string IdempotencyHeaderName = "idempotency-key";
    public const string DefaultBaseUrl = "https://api.kandoo.cloud";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public CustomerApiClient(string baseUrl, string deviceId, HttpMessageHandler? handler = null)
    {
        if (!Uri.TryCreate(SanitizeBaseUrl(baseUrl), UriKind.Absolute, out var baseAddress))
        {
            throw new ArgumentException("A valid customer API base URL is required.", nameof(baseUrl));
        }

        CustomerDeviceId.EnsureValid(deviceId);
        DeviceId = deviceId;
        _ownsClient = handler is null;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = baseAddress;
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>Device identity sent as X-Kandoo-Device; generated once per install.</summary>
    public string DeviceId { get; }

    /// <summary>POST /v1/customer/otp/challenges.</summary>
    public Task<CustomerApiResult<OtpIssueResponse>> RequestOtpAsync(
        string phone, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            throw new ArgumentException("A phone number is required.", nameof(phone));
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        return SendAsync<OtpIssueResponse>(
            HttpMethod.Post,
            "/v1/customer/otp/challenges",
            new OtpIssueRequest { Phone = phone },
            sessionToken: null,
            idempotencyKey,
            includeDeviceHeader: true,
            cancellationToken);
    }

    /// <summary>POST /v1/customer/otp/challenges/{challengeId}/verify.</summary>
    public Task<CustomerApiResult<OtpVerifyResponse>> VerifyOtpAsync(
        string challengeId, string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(challengeId))
        {
            throw new ArgumentException("A challenge id is required.", nameof(challengeId));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("A code is required.", nameof(code));
        }

        return SendAsync<OtpVerifyResponse>(
            HttpMethod.Post,
            $"/v1/customer/otp/challenges/{Uri.EscapeDataString(challengeId)}/verify",
            new OtpVerifyRequest { Code = code },
            sessionToken: null,
            idempotencyKey: null,
            includeDeviceHeader: true,
            cancellationToken);
    }

    /// <summary>GET /v1/customer/invoices — invoices grouped per store. Pass null to probe unauthenticated.</summary>
    public Task<CustomerApiResult<CustomerInvoicesResponse>> ListInvoicesAsync(
        string? sessionToken, CancellationToken cancellationToken = default) =>
        SendAsync<CustomerInvoicesResponse>(
            HttpMethod.Get,
            "/v1/customer/invoices",
            body: null,
            sessionToken,
            idempotencyKey: null,
            includeDeviceHeader: false,
            cancellationToken);

    /// <summary>GET /v1/customer/invoices/{storeId}/{invoiceId}.</summary>
    public Task<CustomerApiResult<CustomerInvoiceDetailResponse>> GetInvoiceAsync(
        string? sessionToken, string storeId, string invoiceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storeId))
        {
            throw new ArgumentException("A store id is required.", nameof(storeId));
        }

        if (string.IsNullOrWhiteSpace(invoiceId))
        {
            throw new ArgumentException("An invoice id is required.", nameof(invoiceId));
        }

        return SendAsync<CustomerInvoiceDetailResponse>(
            HttpMethod.Get,
            $"/v1/customer/invoices/{Uri.EscapeDataString(storeId)}/{Uri.EscapeDataString(invoiceId)}",
            body: null,
            sessionToken,
            idempotencyKey: null,
            includeDeviceHeader: false,
            cancellationToken);
    }

    /// <summary>DELETE /v1/customer/session — revoke the remembered session.</summary>
    public Task<CustomerApiResult<CustomerStatusResponse>> LogoutAsync(
        string? sessionToken, CancellationToken cancellationToken = default) =>
        SendAsync<CustomerStatusResponse>(
            HttpMethod.Delete,
            "/v1/customer/session",
            body: null,
            sessionToken,
            idempotencyKey: null,
            includeDeviceHeader: false,
            cancellationToken);

    private async Task<CustomerApiResult<T>> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string? sessionToken,
        string? idempotencyKey,
        bool includeDeviceHeader,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (includeDeviceHeader)
        {
            // The vectors bind issue/verify to the device (X-Kandoo-Device); the
            // session endpoints authenticate with the Bearer token only.
            request.Headers.Add(DeviceHeaderName, DeviceId);
        }

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.Add(IdempotencyHeaderName, idempotencyKey);
        }

        if (!string.IsNullOrWhiteSpace(sessionToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
        }

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var httpStatus = (int)response.StatusCode;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return CustomerApiResult<T>.Failure(httpStatus, status: null, rawBody: raw);
        }

        try
        {
            var value = JsonSerializer.Deserialize<T>(raw, JsonOptions);
            return value is null
                ? CustomerApiResult<T>.Failure(httpStatus, status: null, rawBody: raw)
                : CustomerApiResult<T>.Ok(httpStatus, value, raw);
        }
        catch (JsonException)
        {
            return CustomerApiResult<T>.Failure(httpStatus, status: null, rawBody: raw);
        }
    }

    private static string SanitizeBaseUrl(string baseUrl) =>
        baseUrl.Trim().TrimEnd('/');

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }
}
