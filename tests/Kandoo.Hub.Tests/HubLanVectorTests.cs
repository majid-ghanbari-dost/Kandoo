using System.Net;
using System.Text.Json;
using Kandoo.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Kandoo.Hub.Tests;

public sealed class HubLanVectorTests : IDisposable
{
    private static readonly Guid S1 = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Hub = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid D1 = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid D2 = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid OwnerId = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);
    private const string Phone = "+989120000001";

    private readonly HubApiFactory _factory = new();
    private readonly HubApiClient _api;

    public HubLanVectorTests() => _api = new HubApiClient(_factory);

    [Fact]
    public async Task LAN_PAIR_001_OwnerIssuesSingleUseCompanionGrant()
    {
        var vector = VectorFile.Get("LAN-PAIR-001");
        var owner = await _api.Owner(S1, Hub, OwnerId);
        var response = await _api.IssueGrant(owner, S1);
        var expected = vector.GetProperty("expected_output").GetProperty("qr");
        var qr = response.Body.GetProperty("qr");

        Assert.Equal("PAIRING_QR_ISSUED", Status(response));
        Assert.Equal(expected.GetProperty("store_id").GetGuid(), qr.GetProperty("store_id").GetGuid());
        Assert.Equal(expected.GetProperty("hub_device_id").GetGuid(), qr.GetProperty("hub_device_id").GetGuid());
        Assert.Equal("COMPANION", qr.GetProperty("device_role").GetString());
        Assert.Equal(expected.GetProperty("expires_at").GetDateTimeOffset(), qr.GetProperty("expires_at").GetDateTimeOffset());
        Assert.Equal(T0, qr.GetProperty("issued_at").GetDateTimeOffset());
        Assert.True(qr.GetProperty("single_use").GetBoolean());
        Assert.Equal(22, qr.GetProperty("nonce").GetString()!.Length);
    }

    [Fact]
    public async Task LAN_PAIR_002_ValidGrantPairsCompanionAndReturnsSession()
    {
        var vector = VectorFile.Get("LAN-PAIR-002");
        Assert.Equal("DEVICE_PAIRED", vector.GetProperty("expected_output").GetProperty("status").GetString());
        var owner = await _api.Owner(S1, Hub, OwnerId);
        var grant = await _api.IssueGrant(owner, S1);
        var qr = grant.Body.GetProperty("qr");
        _factory.Clock.UtcNow = T0.AddMinutes(1);
        var consume = await _api.Consume(
            qr.GetProperty("pairing_id").GetString()!, qr.GetProperty("nonce").GetString()!, D1, "Counter 1");
        var session = consume.Body.GetProperty("session");

        Assert.Equal("DEVICE_PAIRED", Status(consume));
        Assert.Equal(D1, consume.Body.GetProperty("device").GetProperty("device_id").GetGuid());
        Assert.Equal("COMPANION", consume.Body.GetProperty("device").GetProperty("device_role").GetString());
        Assert.Equal("ACTIVE", consume.Body.GetProperty("device").GetProperty("pairing_status").GetString());
        Assert.False(consume.Body.GetProperty("hub_role_granted").GetBoolean());
        Assert.True(consume.Body.GetProperty("qr_consumed").GetBoolean());
        Assert.Equal(T0.AddMinutes(1), session.GetProperty("issued_at").GetDateTimeOffset());
        Assert.Equal(session.GetProperty("issued_at").GetDateTimeOffset().AddHours(1), session.GetProperty("expires_at").GetDateTimeOffset());
        Assert.Equal(43, consume.Body.GetProperty("session_token").GetString()!.Length);
        Assert.Equal(43, consume.Body.GetProperty("device_credential").GetString()!.Length);
    }

    [Fact]
    public async Task LAN_PAIR_003_ExpiredGrantHasNoDeviceOrSessionEffect()
    {
        var vector = VectorFile.Get("LAN-PAIR-003");
        Assert.Equal("PAIRING_EXPIRED", vector.GetProperty("expected_output").GetProperty("status").GetString());
        var owner = await _api.Owner(S1, Hub, OwnerId);
        var grant = await _api.IssueGrant(owner, S1);
        var qr = grant.Body.GetProperty("qr");
        _factory.Clock.UtcNow = qr.GetProperty("expires_at").GetDateTimeOffset();
        var response = await _api.Consume(qr.GetProperty("pairing_id").GetString()!, qr.GetProperty("nonce").GetString()!, D1);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal("PAIRING_EXPIRED", Code(response));
        var devices = await _api.Get($"/v1/stores/{S1}/devices", owner.Token);
        Assert.Equal(1, devices.Body.GetArrayLength());
        Assert.Equal(0, _api.Store.LoadOperationLog().Count(S1));
    }

    [Fact]
    public async Task LAN_PAIR_004_ConsumedGrantCannotPairAnotherDevice()
    {
        var vector = VectorFile.Get("LAN-PAIR-004");
        Assert.Equal("PAIRING_REPLAY_REJECTED", vector.GetProperty("expected_output").GetProperty("status").GetString());
        var owner = await _api.Owner(S1, Hub, OwnerId);
        var paired = await _api.Pair(owner, S1, D1);
        _factory.Clock.UtcNow = T0.AddMinutes(2);
        var replay = await _api.Consume(paired.PairingId, paired.Nonce, D2);

        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        Assert.Equal("PAIRING_REPLAY_REJECTED", Code(replay));
        var devices = await _api.Get($"/v1/stores/{S1}/devices", owner.Token);
        Assert.Equal(2, devices.Body.GetArrayLength());
    }

    [Fact]
    public async Task LAN_SESSION_001_ResumeKeepsOperationIdentityAndPermitsExactRetry()
    {
        var sessionVector = VectorFile.Get("LAN-SESSION-001");
        Assert.Equal("SESSION_ACTIVE", sessionVector.GetProperty("expected_output").GetProperty("status").GetString());
        SeedProduct(7, 10000);
        var owner = await _api.Owner(S1, Hub, OwnerId);
        var paired = await _api.Pair(owner, S1, D1);
        var request = VectorFile.Request(VectorFile.Get("LAN-OP-001"));
        var first = await _api.SubmitRequest(request, paired.Session);
        Assert.Equal("SUCCEEDED", Status(first));
        _factory.Clock.UtcNow = T0.AddMinutes(30);

        var resumed = await _api.Post($"/v1/stores/{S1}/devices/{D1}/sessions", new
        {
            device_credential = paired.DeviceCredential
        });
        Assert.Equal("SESSION_ACTIVE", Status(resumed));
        Assert.True(resumed.Body.GetProperty("operation_id_namespace_unchanged").GetBoolean());
        var newSession = new TestSession(
            resumed.Body.GetProperty("session_token").GetString()!,
            resumed.Body.GetProperty("session").GetProperty("session_id").GetString()!, D1, null);
        Assert.NotEqual(paired.Session.SessionId, newSession.SessionId);
        var replay = await _api.SubmitRequest(request, newSession);

        Assert.Equal("SUCCEEDED", Status(replay));
        Assert.True(replay.Body.GetProperty("idempotent_replay").GetBoolean());
        Assert.Equal(first.Body.GetProperty("request_hash").GetString(), replay.Body.GetProperty("request_hash").GetString());
        Assert.Equal(8, _api.Store.LoadProductCatalog().Get(S1, "prod-0001")!.Version);
    }

    [Fact]
    public async Task LAN_SESSION_002_RevocationReleasesLockAndBlocksLateMutation()
    {
        var owner = await _api.Owner(S1, Hub, OwnerId);
        var paired = await _api.Pair(owner, S1, D1);
        var draft = await _api.SubmitRequest(VectorFile.Request(VectorFile.Get("LAN-LOCK-001")), paired.Session);
        Assert.Equal("SUCCEEDED", Status(draft));
        var before = _api.Store.LoadOperationLog().Count(S1);
        var outbox = _api.Store.GetOutboxDepth(S1);

        var revoked = await _api.Post($"/v1/stores/{S1}/devices/{D1}/revoke", new { }, owner.Token);
        Assert.Equal("DEVICE_REVOKED", Status(revoked));
        var late = await _api.SubmitRequest(VectorFile.Request(VectorFile.Get("LAN-SESSION-002"), 1), paired.Session);
        Assert.Equal("DEVICE_REVOKED", Code(late));
        Assert.Equal(before, _api.Store.LoadOperationLog().Count(S1));
        Assert.Equal(outbox, _api.Store.GetOutboxDepth(S1));

        var lockResponse = await _api.Get($"/v1/stores/{S1}/draft-locks?customer_phone={Uri.EscapeDataString(Phone)}", owner.Token);
        Assert.Equal("RELEASED", lockResponse.Body.GetProperty("state").GetString());
        Assert.Equal("DISCONNECT", lockResponse.Body.GetProperty("release_reason").GetString());
        var devices = await _api.Get($"/v1/stores/{S1}/devices", owner.Token);
        Assert.Contains(devices.Body.EnumerateArray(), device =>
            device.GetProperty("device_id").GetGuid() == D1 && device.GetProperty("pairing_status").GetString() == "REVOKED");
    }

    [Fact]
    public async Task LAN_OP_001_ProductUpdateCommitsAtAssignedSequence()
    {
        SeedProduct(7, 10000);
        var owner = await _api.Owner(S1, Hub, OwnerId);
        var paired = await _api.Pair(owner, S1, D1);
        _api.SeedOperations(S1, D1, 40);
        var response = await _api.SubmitRequest(VectorFile.Request(VectorFile.Get("LAN-OP-001")), paired.Session);

        Assert.Equal("SUCCEEDED", Status(response));
        Assert.Equal("ACCEPTED", response.Body.GetProperty("result_status").GetString());
        Assert.Equal(41, response.Body.GetProperty("operation_seq").GetInt64());
        Assert.Equal(41, _api.Store.GetOutboxDepth(S1));
        var product = _api.Store.LoadProductCatalog().Get(S1, "prod-0001")!;
        Assert.Equal(11000, product.UnitPriceRials);
        Assert.Equal(8, product.Version);
    }

    [Fact]
    public async Task LAN_OP_002_ExactReplayReturnsStoredResponseWithoutEffects()
    {
        SeedProduct(7, 10000);
        var owner = await _api.Owner(S1, Hub, OwnerId);
        var paired = await _api.Pair(owner, S1, D1);
        _api.SeedOperations(S1, D1, 40);
        var request = VectorFile.Request(VectorFile.Get("LAN-OP-002"));
        var first = await _api.SubmitRequest(request, paired.Session);
        var replay = await _api.SubmitRequest(request, paired.Session);

        Assert.Equal("SUCCEEDED", Status(replay));
        Assert.True(replay.Body.GetProperty("idempotent_replay").GetBoolean());
        Assert.Equal(first.Body.GetProperty("operation_seq").GetInt64(), replay.Body.GetProperty("operation_seq").GetInt64());
        Assert.Equal(41, _api.Store.LoadOperationLog().Count(S1));
        Assert.Equal(41, _api.Store.GetOutboxDepth(S1));
        Assert.Equal(8, _api.Store.LoadProductCatalog().Get(S1, "prod-0001")!.Version);
    }

    [Fact]
    public async Task LAN_OP_003_StaleOccIsDurableRejectedResultWithNoProductEffect()
    {
        SeedProduct(8, 11000);
        var owner = await _api.Owner(S1, Hub, OwnerId);
        var paired = await _api.Pair(owner, S1, D2);
        _api.SeedOperations(S1, D2, 41);
        var response = await _api.SubmitRequest(VectorFile.Request(VectorFile.Get("LAN-OP-003")), paired.Session);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("VERSION_CONFLICT", Status(response));
        Assert.Equal("REJECTED", response.Body.GetProperty("result_status").GetString());
        Assert.Equal(42, response.Body.GetProperty("operation_seq").GetInt64());
        Assert.Equal(8, response.Body.GetProperty("response").GetProperty("current_version").GetInt32());
        var product = _api.Store.LoadProductCatalog().Get(S1, "prod-0001")!;
        Assert.Equal(8, product.Version);
        Assert.Null(product.Specification);
        Assert.Equal(42, _api.Store.GetOutboxDepth(S1));
    }

    [Fact]
    public async Task LAN_OP_004_ChangedOperationIdReuseCannotOverwriteOriginal()
    {
        SeedProduct(7, 10000);
        var owner = await _api.Owner(S1, Hub, OwnerId);
        var paired = await _api.Pair(owner, S1, D1);
        _api.SeedOperations(S1, D1, 40);
        var first = await _api.SubmitRequest(VectorFile.Request(VectorFile.Get("LAN-OP-001")), paired.Session);
        var conflict = await _api.SubmitRequest(VectorFile.Request(VectorFile.Get("LAN-OP-004")), paired.Session);

        Assert.Equal("SUCCEEDED", Status(first));
        Assert.Equal("OPERATION_ID_CONFLICT", Status(conflict));
        Assert.Equal(41, _api.Store.LoadOperationLog().Count(S1));
        Assert.Equal(41, _api.Store.GetOutboxDepth(S1));
        var product = _api.Store.LoadProductCatalog().Get(S1, "prod-0001")!;
        Assert.Equal(8, product.Version);
        Assert.Equal(11000, product.UnitPriceRials);
    }

    [Fact]
    public async Task LAN_LOCK_001_DraftCreateAtomicallyAcquiresDefaultLock()
    {
        var owner = await _api.Owner(S1, Hub, OwnerId);
        var paired = await _api.Pair(owner, S1, D1);
        var response = await _api.SubmitRequest(VectorFile.Request(VectorFile.Get("LAN-LOCK-001")), paired.Session);
        var invoice = response.Body.GetProperty("response").GetProperty("invoice");
        var draftLock = response.Body.GetProperty("response").GetProperty("lock");

        Assert.Equal("SUCCEEDED", Status(response));
        Assert.Equal("DRAFT", invoice.GetProperty("status").GetString());
        Assert.False(invoice.GetProperty("retired").GetBoolean());
        Assert.Equal(JsonValueKind.Null, invoice.GetProperty("display_number").ValueKind);
        Assert.Equal(Phone, draftLock.GetProperty("customer_phone").GetString());
        Assert.Equal(T0, draftLock.GetProperty("acquired_at").GetDateTimeOffset());
        Assert.Equal(T0.AddMinutes(5), draftLock.GetProperty("expires_at").GetDateTimeOffset());
        Assert.Equal(300, draftLock.GetProperty("timeout_seconds").GetInt32());
        Assert.Equal(0, _api.Store.GetDisplayNumberHighWatermark(S1));
        Assert.Equal(1, _api.Store.GetOutboxDepth(S1));
    }

    [Fact]
    public async Task LAN_LOCK_002_CompetingDeviceConflictCreatesNoSecondDraftOrLock()
    {
        var owner = await _api.Owner(S1, Hub, OwnerId);
        var first = await _api.Pair(owner, S1, D1);
        var second = await _api.Pair(owner, S1, D2);
        var acquired = await _api.SubmitRequest(VectorFile.Request(VectorFile.Get("LAN-LOCK-001")), first.Session);
        Assert.Equal("SUCCEEDED", Status(acquired));
        var conflict = await _api.SubmitRequest(VectorFile.Request(VectorFile.Get("LAN-LOCK-002")), second.Session);

        Assert.Equal("DRAFT_LOCK_CONFLICT", Status(conflict));
        Assert.Equal(1, _api.Store.LoadInvoiceService().Count);
        Assert.Equal(1, _api.Store.GetDraftLocks().Count(current => current.State == DraftLockState.Active));
        Assert.Equal(0, _api.Store.GetDisplayNumberHighWatermark(S1));
    }

    [Fact]
    public async Task LAN_LOCK_003_OwnerReplayReturnsOriginalInvoiceAndLockWithoutRenewal()
    {
        var owner = await _api.Owner(S1, Hub, OwnerId);
        var paired = await _api.Pair(owner, S1, D1);
        var request = VectorFile.Request(VectorFile.Get("LAN-LOCK-003"));
        var first = await _api.SubmitRequest(request, paired.Session);
        var before = first.Body.GetProperty("response").GetProperty("lock").GetProperty("last_activity_at").GetDateTimeOffset();
        _factory.Clock.UtcNow = T0.AddSeconds(90);
        var replay = await _api.SubmitRequest(request, paired.Session);

        Assert.Equal("SUCCEEDED", Status(replay));
        Assert.True(replay.Body.GetProperty("idempotent_replay").GetBoolean());
        Assert.Equal(
            first.Body.GetProperty("response").GetProperty("invoice").GetProperty("invoice_id").GetGuid(),
            replay.Body.GetProperty("response").GetProperty("invoice").GetProperty("invoice_id").GetGuid());
        Assert.Equal(
            first.Body.GetProperty("response").GetProperty("lock").GetProperty("lock_id").GetGuid(),
            replay.Body.GetProperty("response").GetProperty("lock").GetProperty("lock_id").GetGuid());
        Assert.Equal(before, _api.Store.GetDraftLocks().Single().LastActivityAt);
        Assert.Equal(1, _api.Store.GetOutboxDepth(S1));
    }

    [Fact]
    public async Task LAN_LOCK_004_IssueReleasesAndSequentialPurchaseAcquiresImmediately()
    {
        var owner = await _api.Owner(S1, Hub, OwnerId);
        var first = await _api.Pair(owner, S1, D1);
        var second = await _api.Pair(owner, S1, D2);
        var draft = await _api.SubmitRequest(VectorFile.Request(VectorFile.Get("LAN-LOCK-001")), first.Session);
        var invoiceId = draft.Body.GetProperty("response").GetProperty("invoice").GetProperty("invoice_id").GetGuid();
        var added = await _api.SubmitNew(S1, first.Session, "op-item-for-issue", "invoice.item_add", new
        {
            invoice_id = invoiceId,
            description = "synthetic item",
            quantity_millis = 1000L,
            unit_price_rials = 1000L,
            line_total_rials = 1000L
        });
        Assert.Equal("SUCCEEDED", Status(added));

        var vector = VectorFile.Get("LAN-LOCK-004");
        var issueRequest = VectorFile.Request(vector);
        // Bind the vector command to the Hub-generated invoice ID; the fixed UUID
        // in the synthetic given is not a client-supplied allocation contract.
        var issued = await _api.SubmitNew(
            S1, first.Session, issueRequest.GetProperty("operation_id").GetString()!,
            "invoice.issue", new { invoice_id = invoiceId });
        Assert.Equal("SUCCEEDED", Status(issued));
        Assert.Equal(1, issued.Body.GetProperty("response").GetProperty("invoice").GetProperty("display_number").GetInt64());
        Assert.Equal("ISSUE", issued.Body.GetProperty("response").GetProperty("lock").GetProperty("release_reason").GetString());
        _factory.Clock.UtcNow = T0.AddMinutes(2).AddSeconds(1);
        var next = await _api.SubmitRequest(VectorFile.Request(vector, 1), second.Session);

        Assert.Equal("SUCCEEDED", Status(next));
        Assert.Equal(JsonValueKind.Null, next.Body.GetProperty("response").GetProperty("invoice").GetProperty("display_number").ValueKind);
        Assert.Equal(D2, next.Body.GetProperty("response").GetProperty("lock").GetProperty("device_id").GetGuid());
        Assert.Equal(1, _api.Store.GetDisplayNumberHighWatermark(S1));
    }

    [Fact]
    public async Task LAN_LOCK_005_ExactTimeoutRetiresDraftAndOldOwnerCannotWrite()
    {
        var vector = VectorFile.Get("LAN-LOCK-005");
        Assert.Equal("LOCK_TIMEOUT", vector.GetProperty("expected_output").GetProperty("status").GetString());
        var owner = await _api.Owner(S1, Hub, OwnerId);
        var first = await _api.Pair(owner, S1, D1);
        var draft = await _api.SubmitRequest(VectorFile.Request(VectorFile.Get("LAN-LOCK-001")), first.Session);
        var invoiceId = draft.Body.GetProperty("response").GetProperty("invoice").GetProperty("invoice_id").GetGuid();
        _factory.Clock.UtcNow = T0.AddSeconds(299);
        var active = await Lock(first.Session.Token);
        Assert.Equal("ACTIVE", active.Body.GetProperty("state").GetString());
        _factory.Clock.UtcNow = T0.AddSeconds(300);
        var expired = await Lock(first.Session.Token);

        Assert.Equal("RELEASED", expired.Body.GetProperty("state").GetString());
        Assert.Equal("TIMEOUT", expired.Body.GetProperty("release_reason").GetString());
        var invoice = _api.Store.GetInvoice(S1, invoiceId)!;
        Assert.Equal(RetirementReason.Timeout, invoice.RetirementReason);
        Assert.Null(invoice.DisplayNumber);
        var late = await _api.SubmitNew(S1, first.Session, "op-late-timeout", "invoice.item_add", new
        {
            invoice_id = invoiceId,
            description = "late",
            quantity_millis = 1000L,
            unit_price_rials = 1000L,
            line_total_rials = 1000L
        });
        Assert.Equal("LOCK_NOT_OWNED", Status(late));

        var second = await _api.Pair(owner, S1, D2);
        var sequential = await _api.SubmitRequest(VectorFile.Request(VectorFile.Get("LAN-LOCK-002")), second.Session);
        Assert.Equal("SUCCEEDED", Status(sequential));
        Assert.Equal(0, _api.Store.GetDisplayNumberHighWatermark(S1));
    }

    [Fact]
    public async Task LAN_LOCK_006_DisconnectReleasesAndLateWriteCannotReactivate()
    {
        var owner = await _api.Owner(S1, Hub, OwnerId);
        var first = await _api.Pair(owner, S1, D1);
        var draft = await _api.SubmitRequest(VectorFile.Request(VectorFile.Get("LAN-LOCK-001")), first.Session);
        var invoiceId = draft.Body.GetProperty("response").GetProperty("invoice").GetProperty("invoice_id").GetGuid();
        var disconnected = await _api.Post($"/v1/stores/{S1}/devices/{D1}/disconnect", new { }, first.Session.Token);
        Assert.Equal(1, disconnected.Body.GetProperty("locks_released").GetInt32());
        var lateRequest = VectorFile.Request(VectorFile.Get("LAN-LOCK-006"), 1);
        var latePayload = lateRequest.GetProperty("payload");
        var late = await _api.SubmitNew(
            S1, first.Session, lateRequest.GetProperty("operation_id").GetString()!, "invoice.item_add", new
            {
                invoice_id = invoiceId,
                description = latePayload.GetProperty("description").GetString(),
                quantity_millis = latePayload.GetProperty("quantity_millis").GetInt64(),
                unit_price_rials = 1000L,
                line_total_rials = 1000L
            });

        Assert.Equal("LOCK_NOT_OWNED", Status(late));
        var released = await Lock(first.Session.Token);
        Assert.Equal("RELEASED", released.Body.GetProperty("state").GetString());
        Assert.Equal("DISCONNECT", released.Body.GetProperty("release_reason").GetString());
        Assert.Empty(_api.Store.GetInvoice(S1, invoiceId)!.Items);
        Assert.Null(_api.Store.GetInvoice(S1, invoiceId)!.DisplayNumber);

        var second = await _api.Pair(owner, S1, D2);
        var sequential = await _api.SubmitRequest(VectorFile.Request(VectorFile.Get("LAN-LOCK-002")), second.Session);
        Assert.Equal("SUCCEEDED", Status(sequential));
    }

    [Fact]
    public async Task C08_UnpairedDeviceAndHubRoleGrantAreRefused()
    {
        var owner = await _api.Owner(S1, Hub, OwnerId);
        var hubGrant = await _api.IssueGrant(owner, S1, "HUB");
        Assert.Equal("PAIRING_INVALID", Code(hubGrant));
        var forgedSession = new TestSession("forged-token", "forged-session", D1, null);
        var operation = await _api.SubmitRequest(VectorFile.Request(VectorFile.Get("LAN-OP-001")), forgedSession);
        Assert.Equal("SESSION_INVALID", Code(operation));
        Assert.Equal(0, _api.Store.LoadOperationLog().Count(S1));
        Assert.Equal(0, _api.Store.GetOutboxDepth(S1));
    }

    [Fact]
    public async Task C25_CompanionCannotBootstrapOrUseOwnerOnlyRoutes()
    {
        var owner = await _api.Owner(S1, Hub, OwnerId);
        var paired = await _api.Pair(owner, S1, D1);
        var sessionCount = CountSessions();

        var escalation = await OwnerSession(S1, D1, OwnerId);
        Assert.Equal(HttpStatusCode.Forbidden, escalation.StatusCode);
        Assert.Equal("OWNER_BOOTSTRAP_INVALID", Code(escalation));
        Assert.False(escalation.Body.TryGetProperty("session_token", out _));
        Assert.Equal(sessionCount, CountSessions());

        var grant = await _api.IssueGrant(paired.Session, S1);
        Assert.Equal(HttpStatusCode.Forbidden, grant.StatusCode);
        Assert.Equal("OWNER_REQUIRED", Code(grant));
        var devices = await _api.Get($"/v1/stores/{S1}/devices", paired.Session.Token);
        Assert.Equal(HttpStatusCode.Forbidden, devices.StatusCode);
        Assert.Equal("OWNER_REQUIRED", Code(devices));
        var revoke = await _api.Post($"/v1/stores/{S1}/devices/{D1}/revoke", new { }, paired.Session.Token);
        Assert.Equal(HttpStatusCode.Forbidden, revoke.StatusCode);
        Assert.Equal("OWNER_REQUIRED", Code(revoke));
        Assert.Equal(sessionCount, CountSessions());

        var current = await _api.Get($"/v1/stores/{S1}/devices", owner.Token);
        Assert.Equal(2, current.Body.GetArrayLength());
        Assert.Contains(current.Body.EnumerateArray(), device =>
            device.GetProperty("device_id").GetGuid() == D1 &&
            device.GetProperty("device_role").GetString() == "COMPANION" &&
            device.GetProperty("pairing_status").GetString() == "ACTIVE");
    }

    [Fact]
    public async Task C25_BootstrapRejectsSecondHubOwnerRebindAndCrossStoreReuse()
    {
        var owner = await _api.Owner(S1, Hub, OwnerId);
        var sessionCount = CountSessions();

        var secondHub = await OwnerSession(S1, D1, OwnerId);
        Assert.Equal(HttpStatusCode.Conflict, secondHub.StatusCode);
        Assert.Equal("HUB_ALREADY_ACTIVE", Code(secondHub));
        var rebind = await OwnerSession(S1, Hub, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Forbidden, rebind.StatusCode);
        Assert.Equal("OWNER_BOOTSTRAP_INVALID", Code(rebind));
        var otherStore = Guid.Parse("77777777-7777-4777-8777-777777777777");
        var crossStore = await OwnerSession(otherStore, Hub, OwnerId);
        Assert.Equal(HttpStatusCode.Forbidden, crossStore.StatusCode);
        Assert.Equal("OWNER_BOOTSTRAP_INVALID", Code(crossStore));
        Assert.Equal(sessionCount, CountSessions());

        var renewed = await OwnerSession(S1, Hub, OwnerId);
        Assert.Equal("SESSION_ACTIVE", Status(renewed));
        Assert.Equal("HUB", renewed.Body.GetProperty("session").GetProperty("device_role").GetString());
        Assert.Equal(OwnerId, renewed.Body.GetProperty("session").GetProperty("owner_user_id").GetGuid());
        Assert.Equal(sessionCount + 1, CountSessions());
        var devices = await _api.Get($"/v1/stores/{S1}/devices", owner.Token);
        Assert.Single(devices.Body.EnumerateArray());
        Assert.Equal(Hub, devices.Body[0].GetProperty("device_id").GetGuid());
    }

    [Fact]
    public async Task C25_AuthenticationRejectsSessionDeviceRoleDrift()
    {
        var owner = await _api.Owner(S1, Hub, OwnerId);
        using (var connection = new SqliteConnection(_factory.ConnectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE devices SET device_role = $companion, owner_user_id = NULL
                WHERE store_id = $store AND device_id = $device;
                """;
            command.Parameters.AddWithValue("$companion", (int)HubDeviceRole.Companion);
            command.Parameters.AddWithValue("$store", S1.ToString("D"));
            command.Parameters.AddWithValue("$device", Hub.ToString("D"));
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        var authentication = _api.Store.Authenticate(owner.Token, T0);
        Assert.Equal("SESSION_INVALID", authentication.Code);
        Assert.False(authentication.IsAuthenticated);
        var response = await _api.Get($"/v1/stores/{S1}/devices", owner.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("SESSION_INVALID", Code(response));
    }

    public void Dispose()
    {
        _api.Dispose();
        _factory.Dispose();
    }

    private Task<ApiResponse> Lock(string token) =>
        _api.Get($"/v1/stores/{S1}/draft-locks?customer_phone={Uri.EscapeDataString(Phone)}", token);

    private void SeedProduct(int version, long price) => _api.Store.SaveProduct(
        new Product(S1, "prod-0001", "Sample", "sample", "sample", price, version));

    private Task<ApiResponse> OwnerSession(Guid storeId, Guid hubDeviceId, Guid ownerUserId) =>
        _api.Post($"/v1/stores/{storeId}/owner-sessions", new
        {
            hub_device_id = hubDeviceId,
            owner_user_id = ownerUserId,
            bootstrap_proof = "test-only-owner-bootstrap"
        });

    private int CountSessions()
    {
        using var connection = new SqliteConnection(_factory.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sessions;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static string Status(ApiResponse response) => response.Body.GetProperty("status").GetString()!;
    private static string Code(ApiResponse response) => response.Body.GetProperty("code").GetString()!;
}
