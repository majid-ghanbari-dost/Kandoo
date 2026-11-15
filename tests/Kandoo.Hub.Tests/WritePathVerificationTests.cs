using System.Net;
using Kandoo.Hub;
using Xunit;

namespace Kandoo.Hub.Tests;

/// <summary>
/// C-67: Write-path verification.
///
/// Verifies which Hub mutation paths create durable Operation Log / Outbox
/// entries and which bypass them. The sync protocol (docs/sync-protocol.md)
/// states: "Every accepted mutation has an Operation record and one durable
/// log/outbox entry." Domain mutations routed through HubOperationService
/// satisfy this. Auth/session/pairing mutations that call IHubAccessStore
/// directly (Program.cs endpoints) currently bypass the Operation Log.
/// This test documents the current behavior objectively; it does not change
/// production code. Any decision to route access-store mutations through the
/// Operation Log is a separate owner/PM decision.
/// </summary>
public sealed class WritePathVerificationTests : IDisposable
{
    private readonly HubApiFactory _factory = new();
    private readonly HubApiClient _api;

    public WritePathVerificationTests() => _api = new HubApiClient(_factory);

    [Fact]
    public async Task C67_DomainMutationViaInvoiceDraftCreatesOperationLogEntry()
    {
        var storeId = Guid.NewGuid();
        var owner = await _api.Owner(storeId, Guid.NewGuid(), Guid.NewGuid());
        var paired = await _api.Pair(owner, storeId, Guid.NewGuid());

        var before = _factory.Store.LoadOperationLog().Count(storeId);

        var draft = await _api.Post($"/v1/stores/{storeId:D}/invoices/drafts", new
        {
            operation_id = "c67-draft-1",
            service_type = "SALE",
            customer_phone = "+989120000101",
            external_invoice_number = "native-c67-1"
        }, paired.Session.Token);

        Assert.Equal(HttpStatusCode.OK, draft.StatusCode);

        var after = _factory.Store.LoadOperationLog().Count(storeId);
        Assert.Equal(before + 1, after);
    }

    [Fact]
    public async Task C67_OwnerSessionDoesNotCreateOperationLogEntry()
    {
        var storeId = Guid.NewGuid();
        var before = _factory.Store.LoadOperationLog().Count(storeId);

        await _api.Owner(storeId, Guid.NewGuid(), Guid.NewGuid());

        var after = _factory.Store.LoadOperationLog().Count(storeId);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task C67_PairingGrantDoesNotCreateOperationLogEntry()
    {
        var storeId = Guid.NewGuid();
        var owner = await _api.Owner(storeId, Guid.NewGuid(), Guid.NewGuid());
        var before = _factory.Store.LoadOperationLog().Count(storeId);

        var grant = await _api.IssueGrant(owner, storeId);
        Assert.Equal("PAIRING_QR_ISSUED", grant.Body.GetProperty("status").GetString());

        var after = _factory.Store.LoadOperationLog().Count(storeId);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task C67_PairingConsumeDoesNotCreateOperationLogEntry()
    {
        var storeId = Guid.NewGuid();
        var owner = await _api.Owner(storeId, Guid.NewGuid(), Guid.NewGuid());
        var before = _factory.Store.LoadOperationLog().Count(storeId);

        var grant = await _api.IssueGrant(owner, storeId);
        var qr = grant.Body.GetProperty("qr");
        var consume = await _api.Consume(
            qr.GetProperty("pairing_id").GetString()!,
            qr.GetProperty("nonce").GetString()!,
            Guid.NewGuid(), "Counter", "WINDOWS");

        Assert.Equal("DEVICE_PAIRED", consume.Body.GetProperty("status").GetString());

        var after = _factory.Store.LoadOperationLog().Count(storeId);
        Assert.Equal(before, after);
    }

    public void Dispose()
    {
        _api.Dispose();
        try { _factory.Dispose(); } catch { /* best-effort */ }
    }
}
