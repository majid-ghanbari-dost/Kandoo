using System.Text.Json;
using Xunit;

namespace Kandoo.App.Tests;

/// <summary>
/// End-to-end replay of the ACCEPTED P-31 customer API vectors
/// (docs/vectors/customer-api-v1-2-proposed.json — PM-13 cross-lane review ACCEPT,
/// PHP lane replayed all 12 steps green) through the real CustomerApiClient —
/// the exact HTTP code the Android customer mode uses.
///
/// The server side is P31ContractServer: a synthetic in-memory double of the PHP
/// Cloud customer API mirrored from its implementation semantics (device-bound
/// challenges, idempotency locks, replay rejection, session revocation). Every
/// request the client emits is compared against the vector's declared wire format
/// (method, path, headers, body) and every response against the expected status
/// and required body subset. Emulator-free; runs in the ordinary app test lane.
/// </summary>
public sealed class P31VectorReplayTests
{
    private const string VectorsRelativePath = "docs/vectors/customer-api-v1-2-proposed.json";

    public static TheoryData<string> StepCases => new()
    {
        "missing-session",
        "issue",
        "issue-idempotent",
        "verify-wrong-device",
        "verify",
        "verify-replay",
        "list",
        "detail",
        "foreign-or-unknown",
        "logout",
        "revoked-session",
        "logout-replay",
    };

    // Theory (not one big Fact) so the failing case NAME pinpoints the vector step.
    [Theory]
    [MemberData(nameof(StepCases))]
    public async Task P31_Vector_Replay_Up_To_Step(string lastStepId)
    {
        var vectors = LoadVectors();
        var setup = vectors.GetProperty("setup");
        var server = new P31ContractServer(setup.GetProperty("clock_unix").GetInt64());
        using var primary = new CustomerApiClient(
            "http://p31.synthetic.test",
            setup.GetProperty("device").GetString()!,
            server);
        using var other = new CustomerApiClient(
            "http://p31.synthetic.test",
            setup.GetProperty("other_device").GetString()!,
            server);

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["phone"] = setup.GetProperty("phone").GetString()!,
            ["device"] = setup.GetProperty("device").GetString()!,
            ["other_device"] = setup.GetProperty("other_device").GetString()!,
            ["store"] = setup.GetProperty("store").GetString()!,
            ["invoice"] = setup.GetProperty("invoice").GetString()!,
            ["foreign_invoice"] = setup.GetProperty("foreign_invoice").GetString()!,
        };

        var steps = vectors.GetProperty("steps");
        foreach (var step in steps.EnumerateArray())
        {
            var id = step.GetProperty("id").GetString()!;
            var before = server.Requests.Count;

            // {{otp}} is the code the synthetic SMS transport received — resolved
            // from the double's outbox exactly as the demo kit reads its sms.log.
            if (values.TryGetValue("challenge", out var challengeId))
            {
                var otp = server.LastCodeForChallenge(challengeId);
                if (otp is not null)
                {
                    values["otp"] = otp;
                }
            }

            try
            {
                var (httpStatus, rawBody) = await ExecuteStepAsync(primary, other, step, values, server);

                // Wire format: what the app sent must match the vector's declared request.
                Assert.Equal(before + 1, server.Requests.Count);
                var sent = server.Requests[^1];
                Assert.True(step.TryGetProperty("headers", out var declaredHeaders), $"{id}: vector headers");
                Assert.True(step.TryGetProperty("expect", out var expect), $"{id}: vector expect");
                var expectedStatus = expect.GetProperty("http").GetInt32();
                var expectedBody = expect.GetProperty("body");

                AssertSentRequest(id, step, sent, values);

                // Response: exact status + required body subset (vectors semantics:
                // object fields are required subsets; arrays and scalars compare exactly).
                Assert.True(
                    expectedStatus == httpStatus,
                    $"{id}: expected HTTP {expectedStatus}, got {httpStatus}: {rawBody}");
                using var actualBody = JsonDocument.Parse(rawBody);
                AssertBodyMatches(id, expectedBody, actualBody.RootElement, values);

                if (expect.TryGetProperty("capture", out var capture))
                {
                    foreach (var pair in capture.EnumerateObject())
                    {
                        var sourceField = pair.Value.GetString()!;
                        Assert.True(
                            actualBody.RootElement.TryGetProperty(sourceField, out var captured),
                            $"{id}: capture source {sourceField} missing");
                        values[pair.Name] = captured.GetString()!;
                    }
                }
            }
            catch (Exception ex)
            {
                // One line: the CI problem matcher keeps only the first message line,
                // so the case/step context has to live there to be visible in annotations.
                var sentBody = server.Requests.Count > before ? server.Requests[^1].Body : "<no request>";
                throw new InvalidOperationException(
                    $"Assert.True() Failure VECTOR-REPLAY case={lastStepId} step={id} :: " +
                    $"sentBody=[{sentBody}] valuesPhone=[{values.GetValueOrDefault("phone", "<none>")}] :: " +
                    ex.Message.Replace("\r", " ").Replace("\n", " | "));
            }

            if (id == lastStepId)
            {
                return;
            }
        }

        // The full accepted sequence ran: 12 steps (missing-session … logout-replay).
        Assert.Equal(12, steps.GetArrayLength());
    }

    [Fact]
    public async Task P31_WireFormat_Detail()
    {
        var vectors = LoadVectors();
        var setup = vectors.GetProperty("setup");
        var server = new P31ContractServer(setup.GetProperty("clock_unix").GetInt64());
        using var client = new CustomerApiClient("http://p31.synthetic.test", setup.GetProperty("device").GetString()!, server);

        var issued = await client.RequestOtpAsync("+989120000001", "TEST-wire");
        Assert.Equal(201, issued.HttpStatus);
        var request = server.Requests[^1];

        // Device identity header shape (43-char base64url) + idempotency key + JSON content type.
        var device = request.Header(CustomerApiClient.DeviceHeaderName);
        Assert.NotNull(device);
        Assert.Equal(43, device!.Length);
        Assert.Equal("TEST-wire", request.Header(CustomerApiClient.IdempotencyHeaderName));
        Assert.StartsWith("application/json", request.Header("content-type"));

        // Session endpoints carry Bearer only — no device header (accepted vector headers).
        var verified = await client.VerifyOtpAsync(issued.Value!.ChallengeId!, server.Outbox[^1].Code);
        Assert.Equal(200, verified.HttpStatus);
        Assert.NotNull(verified.Value!.Token);
        var list = await client.ListInvoicesAsync(verified.Value!.Token);
        Assert.Equal(200, list.HttpStatus);
        var listRequest = server.Requests[^1];
        Assert.Null(listRequest.Header(CustomerApiClient.DeviceHeaderName));
        Assert.StartsWith("Bearer ", listRequest.Header("authorization"));
        Assert.Equal(200, (await client.LogoutAsync(verified.Value!.Token)).HttpStatus);
    }

    private static async Task<(int HttpStatus, string RawBody)> ExecuteStepAsync(
        CustomerApiClient primary,
        CustomerApiClient other,
        JsonElement step,
        Dictionary<string, string> values,
        P31ContractServer server)
    {
        var id = step.GetProperty("id").GetString()!;
        var session = values.TryGetValue("session", out var token) ? token : null;

        switch (id)
        {
            case "missing-session":
                return await StatusOf(primary.ListInvoicesAsync(sessionToken: null));
            case "issue":
            case "issue-idempotent":
                return await StatusOf(primary.RequestOtpAsync(values["phone"], "TEST-native-issue"));
            case "verify-wrong-device":
                return await StatusOf(other.VerifyOtpAsync(values["challenge"], OtpFor(server, values["challenge"])));
            case "verify":
            case "verify-replay":
                return await StatusOf(primary.VerifyOtpAsync(values["challenge"], OtpFor(server, values["challenge"])));
            case "list":
            case "revoked-session":
                return await StatusOf(primary.ListInvoicesAsync(session));
            case "detail":
                return await StatusOf(primary.GetInvoiceAsync(session, values["store"], values["invoice"]));
            case "foreign-or-unknown":
                return await StatusOf(primary.GetInvoiceAsync(session, values["store"], values["foreign_invoice"]));
            case "logout":
            case "logout-replay":
                return await StatusOf(primary.LogoutAsync(session));
            default:
                throw new InvalidOperationException($"Unknown vector step id: {id}");
        }

        static async Task<(int, string)> StatusOf<T>(Task<CustomerApiResult<T>> task)
        {
            var result = await task;
            return (result.HttpStatus, result.RawBody ?? string.Empty);
        }

        static string OtpFor(P31ContractServer server, string challengeId) =>
            server.LastCodeForChallenge(challengeId)
            ?? throw new InvalidOperationException($"No OTP code was issued for challenge {challengeId}.");
    }

    private static void AssertSentRequest(
        string id, JsonElement step, RecordedRequest sent, Dictionary<string, string> values)
    {
        Assert.Equal(step.GetProperty("method").GetString()!, sent.Method);
        var expectedPath = Resolve(step.GetProperty("path").GetString()!, values);
        Assert.Equal(expectedPath, sent.Path);

        foreach (var header in step.GetProperty("headers").EnumerateObject())
        {
            var name = header.Name.ToLowerInvariant();
            var actual = sent.Header(name);
            Assert.True(actual is not null, $"{id}: header {header.Name} missing on the wire");
            var expected = Resolve(header.Value.GetString()!, values);
            if (name == "content-type")
            {
                Assert.Equal(
                    expected.Split(';')[0].Trim(),
                    actual!.Split(';')[0].Trim(),
                    ignoreCase: true);
            }
            else
            {
                Assert.Equal(expected, actual);
            }
        }

        if (step.TryGetProperty("body", out var declaredBody))
        {
            using var expectedJson = JsonDocument.Parse(Resolve(declaredBody.GetRawText(), values));
            using var actualJson = JsonDocument.Parse(string.IsNullOrWhiteSpace(sent.Body) ? "{}" : sent.Body);
            AssertJsonEqual(id + ".request", expectedJson.RootElement, actualJson.RootElement);
        }
    }

    private static void AssertBodyMatches(
        string id, JsonElement expected, JsonElement actual, Dictionary<string, string> values)
    {
        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                Assert.Equal(JsonValueKind.Object, actual.ValueKind);
                foreach (var property in expected.EnumerateObject())
                {
                    Assert.True(
                        actual.TryGetProperty(property.Name, out var actualProperty),
                        $"{id}: body field {property.Name} missing");
                    AssertBodyMatches(id, property.Value, actualProperty, values);
                }

                break;
            case JsonValueKind.Array:
                Assert.Equal(JsonValueKind.Array, actual.ValueKind);
                var expectedItems = expected.EnumerateArray().ToList();
                var actualItems = actual.EnumerateArray().ToList();
                Assert.Equal(expectedItems.Count, actualItems.Count);
                for (var i = 0; i < expectedItems.Count; i++)
                {
                    AssertBodyMatches(id, expectedItems[i], actualItems[i], values);
                }

                break;
            default:
                if (expected.ValueKind == JsonValueKind.String)
                {
                    Assert.Equal(Resolve(expected.GetString()!, values), actual.GetString());
                }
                else
                {
                    Assert.Equal(expected.GetRawText(), actual.GetRawText());
                }

                break;
        }
    }

    private static void AssertJsonEqual(string path, JsonElement expected, JsonElement actual)
    {
        Assert.Equal(expected.ValueKind, actual.ValueKind);
        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                var expectedProps = expected.EnumerateObject().ToList();
                var actualProps = actual.EnumerateObject().ToList();
                Assert.Equal(expectedProps.Count, actualProps.Count);
                foreach (var property in expectedProps)
                {
                    Assert.True(actual.TryGetProperty(property.Name, out var actualProperty), $"{path}.{property.Name}");
                    AssertJsonEqual($"{path}.{property.Name}", property.Value, actualProperty);
                }

                break;
            case JsonValueKind.Array:
                var expectedItems = expected.EnumerateArray().ToList();
                var actualItems = actual.EnumerateArray().ToList();
                Assert.Equal(expectedItems.Count, actualItems.Count);
                for (var i = 0; i < expectedItems.Count; i++)
                {
                    AssertJsonEqual($"{path}[{i}]", expectedItems[i], actualItems[i]);
                }

                break;
            default:
                Assert.Equal(expected.GetRawText(), actual.GetRawText());
                break;
        }
    }

    private static string Resolve(string text, Dictionary<string, string> values)
    {
        var resolved = text;
        foreach (var pair in values)
        {
            resolved = resolved.Replace("{{" + pair.Key + "}}", pair.Value, StringComparison.Ordinal);
        }

        return resolved;
    }

    private static JsonElement LoadVectors()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepositoryFile(VectorsRelativePath)));
        return document.RootElement.Clone();
    }

    /// <summary>Walk up from the test output directory until the repository file is found.</summary>
    internal static string FindRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate {relativePath} by walking up from {AppContext.BaseDirectory}.");
    }
}
