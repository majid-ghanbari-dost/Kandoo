using System.Net;
using System.Text;
using System.Text.Json;
using Kandoo.App;
using Xunit;

namespace Kandoo.App.Tests;

public sealed class DevicePairingTests
{
    [Fact]
    public void C41_PairingParserAcceptsOnlyCompanionSingleUseHttpsQr()
    {
        var storeId = Guid.NewGuid();
        var json = QrJson(storeId);
        var code = PairingCodeParser.Parse(json);

        Assert.Equal("pair-1", code.PairingId);
        Assert.Equal("nonce-1", code.Nonce);
        Assert.Equal(storeId, code.StoreId);
        Assert.Equal("https://hub.lan", code.HubEndpoint);
        Assert.Throws<JsonException>(() => PairingCodeParser.Parse(json.Replace("\"0\"", "\"1\"")));
        Assert.Throws<JsonException>(() => PairingCodeParser.Parse(json.Replace("COMPANION", "HUB")));
        Assert.Throws<FormatException>(() => PairingCodeParser.Parse(json.Replace("https://hub.lan", "http://hub.lan")));
    }

    [Fact]
    public async Task C41_AndroidPairingUsesConsumeContractWithoutLevelOverride()
    {
        var storeId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var handler = new StubHandler((request, body) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://hub.lan/v1/pairing-grants/consume", request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Headers.Authorization);
            using var payload = JsonDocument.Parse(body!);
            Assert.Equal("pair-1", payload.RootElement.GetProperty("pairing_id").GetString());
            Assert.Equal("nonce-1", payload.RootElement.GetProperty("nonce").GetString());
            Assert.Equal(deviceId, payload.RootElement.GetProperty("device_id").GetGuid());
            Assert.Equal("Phone", payload.RootElement.GetProperty("label").GetString());
            Assert.Equal("ANDROID", payload.RootElement.GetProperty("platform").GetString());
            Assert.Equal("public-key", payload.RootElement.GetProperty("device_public_key").GetString());
            Assert.False(payload.RootElement.TryGetProperty("operator_level", out _));
            Assert.False(payload.RootElement.TryGetProperty("requested_level", out _));
            return JsonResponse($$"""
                {
                  "status": "DEVICE_PAIRED",
                  "device": {
                    "store_id": "{{storeId}}",
                    "device_id": "{{deviceId}}",
                    "device_role": "COMPANION",
                    "operator_level": "MANAGER",
                    "pairing_status": "ACTIVE",
                    "label": "Phone",
                    "platform": "ANDROID"
                  },
                  "session": {},
                  "session_token": "session-secret",
                  "device_credential": "credential-secret",
                  "qr_consumed": true,
                  "hub_role_granted": false
                }
                """);
        });
        using var client = new HttpClient(handler);
        var source = new HubPairingDataSource(client, () => deviceId, () => "public-key");

        var result = await source.PairAsync(PairingCodeParser.Parse(QrJson(storeId)), "Phone");

        Assert.Equal(deviceId, result.DeviceId);
        Assert.Equal(storeId, result.StoreId);
        Assert.Equal("session-secret", result.SessionToken);
        Assert.Equal("credential-secret", result.DeviceCredential);
        Assert.Equal(DeviceLevels.Manager, result.OperatorLevel);
    }

    [Fact]
    public async Task C41_DeviceListMapsLevelsAndAdminEligibility()
    {
        var storeId = Guid.NewGuid();
        var hubId = Guid.NewGuid();
        var companionId = Guid.NewGuid();
        var revokedId = Guid.NewGuid();
        var settings = new HubConnectionSettings("https://hub.lan", storeId, "owner-session");
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"https://hub.lan/v1/stores/{storeId:D}/devices", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("owner-session", request.Headers.Authorization.Parameter);
            return JsonResponse($$"""
                [
                  {
                    "store_id": "{{storeId}}", "device_id": "{{hubId}}",
                    "device_role": "HUB", "operator_level": null,
                    "pairing_status": "ACTIVE", "label": "Hub", "platform": "WINDOWS"
                  },
                  {
                    "store_id": "{{storeId}}", "device_id": "{{companionId}}",
                    "device_role": "COMPANION", "operator_level": "SELLER",
                    "pairing_status": "ACTIVE", "label": "Phone", "platform": "ANDROID"
                  },
                  {
                    "store_id": "{{storeId}}", "device_id": "{{revokedId}}",
                    "device_role": "COMPANION", "operator_level": "MANAGER",
                    "pairing_status": "REVOKED", "label": "Old phone", "platform": "ANDROID"
                  }
                ]
                """);
        });
        using var client = new HttpClient(handler);
        var source = new HubDeviceAdministrationDataSource(client, settings);

        var rows = await source.ListAsync();

        Assert.Equal(3, rows.Count);
        Assert.False(rows[0].CanAdminister);
        Assert.Equal("ثبت نشده", rows[0].LevelDisplay);
        Assert.True(rows[1].CanAdminister);
        Assert.Equal(DeviceLevels.Seller, rows[1].OperatorLevel);
        Assert.False(rows[2].CanAdminister);
        Assert.Equal(DeviceLevels.Manager, rows[2].OperatorLevel);
    }

    [Fact]
    public async Task C41_LevelChangeUsesOwnerPatchAndMapsUpdatedDevice()
    {
        var storeId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var settings = new HubConnectionSettings("https://hub.lan", storeId, "owner-session");
        var handler = new StubHandler((request, body) =>
        {
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal($"https://hub.lan/v1/stores/{storeId:D}/devices/{deviceId:D}/level", request.RequestUri!.AbsoluteUri);
            Assert.Equal("owner-session", request.Headers.Authorization!.Parameter);
            using var payload = JsonDocument.Parse(body!);
            Assert.Equal("MANAGER", payload.RootElement.GetProperty("operator_level").GetString());
            return JsonResponse($$"""
                {
                  "status": "DEVICE_LEVEL_CHANGED",
                  "device": {
                    "store_id": "{{storeId}}", "device_id": "{{deviceId}}",
                    "device_role": "COMPANION", "operator_level": "MANAGER",
                    "pairing_status": "ACTIVE", "label": "Phone", "platform": "ANDROID"
                  }
                }
                """);
        });
        using var client = new HttpClient(handler);
        var source = new HubDeviceAdministrationDataSource(client, settings);

        var row = await source.ChangeLevelAsync(deviceId, DeviceLevels.Manager);

        Assert.Equal(DeviceLevels.Manager, row.OperatorLevel);
        Assert.Equal(deviceId, row.DeviceId);
    }

    [Fact]
    public async Task C41_RevokeUsesOwnerPostAndFailsOwnerErrorsClosed()
    {
        var storeId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var settings = new HubConnectionSettings("https://hub.lan", storeId, "owner-session");
        var handler = new StubHandler((request, body) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"https://hub.lan/v1/stores/{storeId:D}/devices/{deviceId:D}/revoke", request.RequestUri!.AbsoluteUri);
            Assert.Equal("owner-session", request.Headers.Authorization!.Parameter);
            Assert.Equal("{}", body);
            return JsonResponse("""{"status":"DEVICE_REVOKED"}""");
        });
        using var client = new HttpClient(handler);
        await new HubDeviceAdministrationDataSource(client, settings).RevokeAsync(deviceId);

        var forbidden = new StubHandler((_, _) => JsonResponse(
            """{"code":"OWNER_REQUIRED","message":"owner","retry_hint":"none"}""",
            HttpStatusCode.Forbidden));
        using var forbiddenClient = new HttpClient(forbidden);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new HubDeviceAdministrationDataSource(forbiddenClient, settings).ListAsync());
        Assert.Contains("OWNER_REQUIRED", error.Message, StringComparison.Ordinal);
    }

    private static string QrJson(Guid storeId) => $$"""
        {
          "protocol_version": "0",
          "pairing_id": "pair-1",
          "store_id": "{{storeId}}",
          "hub_device_id": "{{Guid.NewGuid()}}",
          "device_role": "COMPANION",
          "operator_level": "OPERATOR",
          "hub_endpoint": "https://hub.lan",
          "nonce": "nonce-1",
          "issued_at": "2026-11-01T00:00:00Z",
          "expires_at": "2026-11-01T00:05:00Z",
          "single_use": true
        }
        """;

    private static HttpResponseMessage JsonResponse(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            return Task.FromResult(handler(request, body));
        }
    }
}
