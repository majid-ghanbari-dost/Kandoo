using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kandoo.App.Tests;

/// <summary>
/// SYNTHETIC/TEST-ONLY in-memory double of the PHP Cloud customer API (P-31).
///
/// Behaviour mirrored from the PHP implementation the accepted vectors describe
/// (kandoo-cloud-php: src/Customer/CustomerAuth.php + src/Customer/CustomerPages.php):
/// phone-invoice gating, idempotency locks, device-bound challenges and sessions,
/// consumed-challenge replay rejection, attempt lockout, per-store invoice
/// ownership, one-shot session revocation. All fixture values come from the
/// synthetic vector file docs/vectors/customer-api-v1-2-proposed.json — no real
/// data anywhere. Used to prove the Android customer mode speaks the accepted
/// P-31 contract end-to-end at the HTTP boundary, emulator-free.
/// </summary>
public sealed class P31ContractServer : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Challenge> _challengesByPhone = new();
    private readonly Dictionary<string, Challenge> _challengesById = new();
    private readonly Dictionary<string, IdempotencyEntry> _idempotency = new();
    private readonly Dictionary<string, Session> _sessions = new();
    private readonly Dictionary<string, List<InvoiceRow>> _invoicesByPhone = new();
    private readonly HashSet<string> _deviceDailyNumbers = new();
    private int _codeSequence;

    public P31ContractServer(long clockUnix)
    {
        ClockUnix = clockUnix;
        // Synthetic fixture — exactly the vector setup (TEST Store / display 7 / payable 25000).
        SeedInvoice(
            phone: "+989120000001",
            storeId: "11111111-1111-4111-8111-111111111111",
            storeName: "TEST Store",
            invoiceId: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
            displayNumber: 7,
            payableRials: 25000);
        // Foreign invoice (owned by another number) — must be NOT_FOUND for the test session.
        SeedInvoice(
            phone: "+989120000002",
            storeId: "11111111-1111-4111-8111-111111111111",
            storeName: "TEST Other",
            invoiceId: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
            displayNumber: 8,
            payableRials: 1000);
    }

    public long ClockUnix { get; }

    /// <summary>Requests received, for wire-format assertions against the vectors.</summary>
    public List<RecordedRequest> Requests { get; } = new();

    /// <summary>Codes handed to the SMS transport (the vector {{otp}} placeholder).</summary>
    public List<(string Phone, string Code)> Outbox { get; } = new();

    public string? LastCodeForChallenge(string challengeId)
    {
        lock (_gate)
        {
            return _challengesById.TryGetValue(challengeId, out var challenge) ? challenge.Code : null;
        }
    }

    public bool IsChallengeLocked(string challengeId, int maxAttempts)
    {
        lock (_gate)
        {
            return _challengesById.TryGetValue(challengeId, out var challenge) && challenge.Attempts >= maxAttempts;
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedRequest(
            request.Method.Method,
            request.RequestUri!.AbsolutePath,
            CaptureHeaders(request),
            body);
        Requests.Add(recorded);

        return request.Method.Method switch
        {
            "POST" when recorded.Path == "/v1/customer/otp/challenges" => HandleIssue(recorded),
            "POST" when Regex.IsMatch(recorded.Path, "^/v1/customer/otp/challenges/[^/]+/verify$") =>
                HandleVerify(recorded, recorded.Path.Split('/')[5]),
            "GET" when recorded.Path == "/v1/customer/invoices" => HandleList(recorded),
            "GET" when Regex.IsMatch(recorded.Path, "^/v1/customer/invoices/[^/]+/[^/]+$") =>
                HandleDetail(recorded, recorded.Path.Split('/')),
            "DELETE" when recorded.Path == "/v1/customer/session" => HandleLogout(recorded),
            _ => JsonResponse(404, new { status = "NOT_FOUND" }),
        };
    }

    private HttpResponseMessage HandleIssue(RecordedRequest request)
    {
        if (!IsJson(request))
        {
            return JsonResponse(400, new { status = "INVALID" });
        }

        string? phone;
        try
        {
            using var document = JsonDocument.Parse(request.Body);
            phone = document.RootElement.TryGetProperty("phone", out var p) ? p.GetString() : null;
        }
        catch (JsonException)
        {
            return JsonResponse(400, new { status = "INVALID" });
        }

        var device = request.Header(CustomerApiClient.DeviceHeaderName);
        var key = request.Header(CustomerApiClient.IdempotencyHeaderName);
        if (string.IsNullOrEmpty(phone) || !Regex.IsMatch(phone, "^\\+[1-9][0-9]{7,14}$")
            || string.IsNullOrEmpty(device) || !Regex.IsMatch(device, "^[A-Za-z0-9_-]{43}$")
            || string.IsNullOrEmpty(key) || key.Length > 128)
        {
            return JsonResponse(400, new { status = "INVALID" });
        }

        lock (_gate)
        {
            if (_idempotency.TryGetValue(key, out var prior))
            {
                if (prior.Phone != phone || prior.Device != device)
                {
                    return JsonResponse(409, new { status = "CONFLICT" });
                }

                if (prior.DeliveryState != "sent")
                {
                    return JsonResponse(409, new { status = "INCOMPLETE" });
                }

                return JsonResponse(200, new
                {
                    status = "DUPLICATE",
                    challenge_id = prior.ChallengeId,
                    expires_at = prior.ExpiresAt,
                });
            }

            if (!_invoicesByPhone.ContainsKey(phone))
            {
                _idempotency[key] = new IdempotencyEntry(phone, device, null, 0, "failed");
                return JsonResponse(400, new { status = "INVALID" });
            }

            if (!_deviceDailyNumbers.Contains($"{device}|{phone}"))
            {
                var numbersToday = _deviceDailyNumbers.Count(n => n.StartsWith(device + "|", StringComparison.Ordinal));
                if (numbersToday >= 3)
                {
                    return JsonResponse(429, new { status = "RATE_LIMITED" });
                }

                _deviceDailyNumbers.Add($"{device}|{phone}");
            }

            var issued = _challengesByPhone.Values.Count(c => c.Phone == phone);
            if (issued >= 2)
            {
                // codes_per_announcement budget exhausted → support (TELEGRAM).
                return JsonResponse(429, new { status = "TELEGRAM" });
            }

            var id = Guid.NewGuid().ToString("D");
            var code = $"1{(++_codeSequence):00000}"; // deterministic 6-digit synthetic code
            var expires = ClockUnix + 300;
            var challenge = new Challenge(id, phone, device, code, expires);
            _challengesById[id] = challenge;
            _challengesByPhone[id] = challenge;
            _idempotency[key] = new IdempotencyEntry(phone, device, id, expires, "sent");
            Outbox.Add((phone, code));
            return JsonResponse(201, new { status = "OTP_SENT", challenge_id = id, expires_at = expires });
        }
    }

    private HttpResponseMessage HandleVerify(RecordedRequest request, string challengeId)
    {
        if (!IsJson(request))
        {
            return JsonResponse(400, new { status = "INVALID" });
        }

        string? code;
        try
        {
            using var document = JsonDocument.Parse(request.Body);
            code = document.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
        }
        catch (JsonException)
        {
            return JsonResponse(400, new { status = "INVALID" });
        }

        var device = request.Header(CustomerApiClient.DeviceHeaderName);
        if (string.IsNullOrEmpty(device) || !Regex.IsMatch(device, "^[A-Za-z0-9_-]{43}$")
            || string.IsNullOrEmpty(code) || !Regex.IsMatch(code, "^[0-9]{6}$"))
        {
            return JsonResponse(400, new { status = "INVALID" });
        }

        lock (_gate)
        {
            if (!_challengesById.TryGetValue(challengeId, out var challenge)
                || challenge.Device != device
                || challenge.Consumed
                || challenge.ExpiresAt <= ClockUnix
                || challenge.Attempts >= 3)
            {
                return JsonResponse(400, new { status = "INVALID" });
            }

            challenge.Attempts++;
            if (challenge.Code != code)
            {
                return JsonResponse(400, new { status = "INVALID" });
            }

            challenge.Consumed = true;
            var token = CustomerDeviceId.New();
            _sessions[token] = new Session(token, challenge.Phone);
            return JsonResponse(200, new { status = "OK", token });
        }
    }

    private HttpResponseMessage HandleList(RecordedRequest request)
    {
        var session = BearerSession(request);
        if (session is null)
        {
            return JsonResponse(401, new { status = "UNAUTHENTICATED" });
        }

        lock (_gate)
        {
            var stores = _invoicesByPhone[session.Phone]
                .GroupBy(row => row.StoreId)
                .Select(group => new
                {
                    store_id = group.Key,
                    store_name = group.First().StoreName,
                    invoices = group.Select(row => new
                    {
                        invoice_id = row.InvoiceId,
                        display_number = row.DisplayNumber,
                    }).ToArray(),
                })
                .ToArray();
            return JsonResponse(200, new { stores });
        }
    }

    private HttpResponseMessage HandleDetail(RecordedRequest request, string[] segments)
    {
        var session = BearerSession(request);
        if (session is null)
        {
            return JsonResponse(401, new { status = "UNAUTHENTICATED" });
        }

        var storeId = segments[4];
        var invoiceId = segments[5];
        lock (_gate)
        {
            var row = _invoicesByPhone[session.Phone]
                .FirstOrDefault(r => r.StoreId == storeId && r.InvoiceId == invoiceId);
            if (row is null)
            {
                return JsonResponse(404, new { status = "NOT_FOUND" });
            }

            return JsonResponse(200, new
            {
                invoice = new
                {
                    store_id = row.StoreId,
                    invoice_id = row.InvoiceId,
                    display_number = row.DisplayNumber,
                    payable_rials = row.PayableRials,
                    line_items = Array.Empty<object>(),
                    // Optional extras beyond the required vector subset:
                    store_name = row.StoreName,
                    lifecycle_status = "ISSUED",
                    issued_at = "2026-01-02 03:04:05",
                },
            });
        }
    }

    private HttpResponseMessage HandleLogout(RecordedRequest request)
    {
        var session = BearerSession(request);
        if (session is null)
        {
            return JsonResponse(401, new { status = "UNAUTHENTICATED" });
        }

        lock (_gate)
        {
            _sessions.Remove(session.Token);
            return JsonResponse(200, new { status = "REVOKED" });
        }
    }

    private Session? BearerSession(RecordedRequest request)
    {
        var authorization = request.Header("authorization");
        if (authorization is null || !authorization.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            return null;
        }

        var token = authorization["Bearer ".Length..];
        lock (_gate)
        {
            return _sessions.TryGetValue(token, out var session) ? session : null;
        }
    }

    private void SeedInvoice(
        string phone, string storeId, string storeName, string invoiceId, long displayNumber, long payableRials)
    {
        if (!_invoicesByPhone.TryGetValue(phone, out var rows))
        {
            rows = new List<InvoiceRow>();
            _invoicesByPhone[phone] = rows;
        }

        rows.Add(new InvoiceRow(storeId, storeName, invoiceId, displayNumber, payableRials));
    }

    private static bool IsJson(RecordedRequest request)
    {
        var contentType = request.Header("content-type");
        return contentType is not null
            && contentType.Split(';')[0].Trim().Equals("application/json", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> CaptureHeaders(HttpRequestMessage request)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in request.Headers)
        {
            headers[pair.Key.ToLowerInvariant()] = string.Join(",", pair.Value);
        }

        if (request.Content is not null)
        {
            foreach (var pair in request.Content.Headers)
            {
                headers[pair.Key.ToLowerInvariant()] = string.Join(",", pair.Value);
            }
        }

        return headers;
    }

    private static HttpResponseMessage JsonResponse(int status, object body) =>
        new((HttpStatusCode)status)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json"),
        };

    private sealed record Challenge(string Id, string Phone, string Device, string Code, long ExpiresAt)
    {
        public int Attempts { get; set; }
        public bool Consumed { get; set; }
    }

    private sealed record IdempotencyEntry(
        string Phone, string Device, string? ChallengeId, long ExpiresAt, string DeliveryState);

    private sealed record Session(string Token, string Phone);

    private sealed record InvoiceRow(
        string StoreId, string StoreName, string InvoiceId, long DisplayNumber, long PayableRials);
}

/// <summary>One request as the server saw it (wire-format evidence for the vectors).</summary>
public sealed record RecordedRequest(
    string Method,
    string Path,
    IReadOnlyDictionary<string, string> Headers,
    string Body)
{
    public string? Header(string name) =>
        Headers.TryGetValue(name, out var value) ? value : null;
}
