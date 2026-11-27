using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace Kandoo.App.Tests;

/// <summary>
/// LIVE end-to-end of the Android customer mode against the real PHP Cloud
/// customer API (P-31 accepted contract) — the D3 customer-channel leg.
///
/// This test is armed by environment only (no emulator, no invented infrastructure):
///   KANDOO_CLOUD_BASE_URL   e.g. http://192.168.1.5:8080  (the PHP local kit, LAN)
///   KANDOO_CLOUD_PHONE      customer number with a seeded/demo invoice (default the vector number)
///   KANDOO_CLOUD_OTP        the code from the demo outbox («کد ورود نمایشی کندو: …»)
///   KANDOO_CLOUD_OTP_FILE   …or the path of the kit's sms.log (JSONL outbox) to read the code from
///   KANDOO_CLOUD_E2E_STRICT = 1  fail on every non-passing step (D3 gate mode)
///
/// Without KANDOO_CLOUD_BASE_URL the test reports SKIPPED and succeeds — the CI
/// app lane has no PHP kit; the contract-level replay (P31VectorReplayTests) runs
/// there instead. Steps whose endpoints are not yet served by the PHP lane
/// (P-31 finish in flight: list/detail/session JSON) report PENDING unless strict.
/// </summary>
public sealed class P31LiveE2ETests
{
    private readonly ITestOutputHelper _output;

    public P31LiveE2ETests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "LiveE2E")]
    public async Task CustomerMode_EndToEnd_Against_RealPhpCloud()
    {
        var baseUrl = Environment.GetEnvironmentVariable("KANDOO_CLOUD_BASE_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _output.WriteLine(
                "SKIPPED (live E2E not armed): set KANDOO_CLOUD_BASE_URL to the PHP Cloud base URL " +
                "to run the customer mode end-to-end against the real P-31 API. " +
                "The contract-level vector replay runs unconditionally in P31VectorReplayTests.");
            return;
        }

        var strict = Environment.GetEnvironmentVariable("KANDOO_CLOUD_E2E_STRICT") == "1";
        var phone = Environment.GetEnvironmentVariable("KANDOO_CLOUD_PHONE") ?? "+989120000001";
        var deviceId = CustomerDeviceId.New();
        using var client = new CustomerApiClient(baseUrl, deviceId);

        var failures = new List<string>();

        // 1) Unauthenticated probe — the accepted envelope.
        var anonymous = await client.ListInvoicesAsync(null);
        Check(failures, "missing-session", expectStatus: 401, expectBodyStatus: "UNAUTHENTICATED", anonymous, strict);

        // 2) OTP issue (device-bound + idempotent key on the wire).
        var issued = await client.RequestOtpAsync(phone, "LIVE-" + Guid.NewGuid().ToString("D"));
        var issuedOk = issued.HttpStatus == 201 && issued.Status == "OTP_SENT" && issued.Value?.ChallengeId is not null
            || issued.HttpStatus == 200 && issued.Status == "DUPLICATE" && issued.Value?.ChallengeId is not null;
        if (!issuedOk)
        {
            failures.Add($"issue: HTTP {issued.HttpStatus} status {issued.Status} (a demo outbox SMS needs an existing invoice for {phone})");
        }

        if (issued.Value?.ChallengeId is not null)
        {
            // 3) Verify with the code from the demo outbox.
            var code = ResolveOtp();
            if (code is null)
            {
                failures.Add("verify: no OTP code available — set KANDOO_CLOUD_OTP or KANDOO_CLOUD_OTP_FILE (kit sms.log)");
            }
            else
            {
                var verified = await client.VerifyOtpAsync(issued.Value.ChallengeId, code);
                if (verified.Status != "OK" || verified.Value?.Token is null)
                {
                    failures.Add($"verify: HTTP {verified.HttpStatus} status {verified.Status}");
                }
                else
                {
                    var session = verified.Value.Token;

                    // 4) Invoices grouped per store.
                    var list = await client.ListInvoicesAsync(session);
                    if (list.HttpStatus == 200 && list.Value is not null)
                    {
                        _output.WriteLine($"list: PASS ({list.Value.Stores.Count} store group(s))");
                        var first = list.Value.Stores.SelectMany(s => s.Invoices).FirstOrDefault();
                        if (first is not null)
                        {
                            // 5) Invoice detail.
                            var store = list.Value.Stores.First(s => s.Invoices.Contains(first));
                            var detail = await client.GetInvoiceAsync(session, store.StoreId, first.InvoiceId);
                            Check(failures, "detail", expectStatus: 200, expectBodyStatus: null, detail, strict);
                        }
                    }
                    else
                    {
                        PendingOrFailure(failures, "list", list, strict);
                    }

                    // 6) Logout + revoked-session proof.
                    var logout = await client.LogoutAsync(session);
                    Check(failures, "logout", expectStatus: 200, expectBodyStatus: "REVOKED", logout, strict);
                    var revoked = await client.ListInvoicesAsync(session);
                    Check(failures, "revoked-session", expectStatus: 401, expectBodyStatus: "UNAUTHENTICATED", revoked, strict);
                }
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail("LIVE P-31 E2E failures: " + string.Join(" | ", failures));
        }

        _output.WriteLine("LIVE P-31 E2E: all executed steps passed.");
    }

    /// <summary>Read the demo OTP from env or from the kit's sms.log JSONL outbox.</summary>
    private static string? ResolveOtp()
    {
        var direct = Environment.GetEnvironmentVariable("KANDOO_CLOUD_OTP");
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct.Trim();
        }

        var path = Environment.GetEnvironmentVariable("KANDOO_CLOUD_OTP_FILE");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        string? last = null;
        foreach (var line in File.ReadLines(path))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("kind", out var kind) && kind.GetString() == "otp"
                    && root.TryGetProperty("body", out var body))
                {
                    var match = Regex.Match(body.GetString() ?? string.Empty, "[0-9]{6,9}");
                    if (match.Success)
                    {
                        last = match.Value;
                    }
                }
            }
            catch (JsonException)
            {
                // ignore malformed outbox lines
            }
        }

        return last;
    }

    private void Check<T>(
        List<string> failures,
        string step,
        int expectStatus,
        string? expectBodyStatus,
        CustomerApiResult<T> result,
        bool strict)
    {
        var statusOk = result.HttpStatus == expectStatus
            && (expectBodyStatus is null || result.Status == expectBodyStatus);
        if (statusOk)
        {
            _output.WriteLine($"{step}: PASS (HTTP {result.HttpStatus} status {result.Status})");
            return;
        }

        PendingOrFailure(failures, step, result, strict);
    }

    private void PendingOrFailure<T>(
        List<string> failures, string step, CustomerApiResult<T> result, bool strict)
    {
        // The P-31 list/detail/session JSON endpoints land with the PHP lane's
        // "P-31 finish"; until then a 404 on those routes is a known pending item,
        // not a client defect. Strict mode (D3 gate) treats it as a failure.
        var pendingEndpoint = result.HttpStatus == 404;
        if (pendingEndpoint && !strict)
        {
            _output.WriteLine(
                $"{step}: PENDING (HTTP 404 — endpoint not yet served by the PHP lane; " +
                "P-31 finish in flight). Re-run with KANDOO_CLOUD_E2E_STRICT=1 for the D3 gate.");
            return;
        }

        failures.Add($"{step}: HTTP {result.HttpStatus} status {result.Status}");
    }
}
