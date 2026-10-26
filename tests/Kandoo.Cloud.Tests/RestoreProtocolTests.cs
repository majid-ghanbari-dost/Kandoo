extern alias Hub;

using System.Text.Json;
using Kandoo.Cloud;
using Kandoo.Core;
using Npgsql;
using NpgsqlTypes;
using Xunit;
using Hub::Kandoo.Hub;

namespace Kandoo.Cloud.Tests;

[Collection("Postgres")]
public sealed class RestoreProtocolTests
{
    private readonly PostgresTestDatabase _database;

    public RestoreProtocolTests(PostgresTestDatabase database)
    {
        _database = database;
    }

    [Fact]
    public void C12_RESTORE_001_SmsAloneCannotExportOrActivate()
    {
        var vector = VectorFile.Get("RESTORE-001");
        var cloud = VectorCloud(vector);
        var service = Service(cloud, out _);
        var result = service.Authorize(Request(vector), Manifest(vector), "valid-test-otp-proof", null);

        Assert.Equal("SECOND_FACTOR_REQUIRED", result.Status);
        Assert.Null(result.Grant);
        Assert.Equal(0, cloud.ExportCalls);
        Assert.Equal(0, cloud.ActivationCalls);
        Assert.Equal(3L, cloud.CurrentEpoch);
    }

    [Fact]
    public void C12_RESTORE_002_RecoveryKeyAndMatchingHashAreRequired()
    {
        var vector = VectorFile.Get("RESTORE-002");
        var cloud = VectorCloud(vector);
        var service = Service(cloud, out _);
        var authorization = service.Authorize(Request(vector), Manifest(vector), "valid-test-otp-proof", SecondFactor(vector));

        Assert.Equal("AUTHORIZED_RECOVERY_KEY", authorization.Status);
        Assert.Equal("RECOVERY_KEY", authorization.SecondFactor);
        Assert.NotNull(authorization.Grant);
        var export = service.ExportAuthorized(authorization.Grant!);
        var verification = StoreRestoreService.VerifyReplayedState(export.Manifest, export.ReplayedState);
        var expected = vector.GetProperty("expected_output");
        Assert.Equal("MATCH", verification.ConsistencyCheck);
        Assert.Equal(expected.GetProperty("computed_state_hash").GetString(), verification.ComputedStateHash);
        Assert.True(verification.CanActivate);
        Assert.Equal(expected.GetProperty("new_writer_epoch").GetInt64(), verification.NewWriterEpoch);
        var report = StoreRestoreService.AssessLoss(export.Manifest, Array.Empty<long>(), NewDevice(vector));
        Assert.Equal("RESTORED", report.Status);
        Assert.Equal(42L, report.NextOperationSeq);
        Assert.False(report.SessionsRestored);
        Assert.Equal(0, cloud.ActivationCalls);
    }

    [Fact]
    public void C12_RESTORE_003_PairedApprovalAuthorizesExportButNotEarlyActivation()
    {
        var vector = VectorFile.Get("RESTORE-003");
        var cloud = VectorCloud(vector);
        var service = Service(cloud, out _);
        var result = service.Authorize(Request(vector), Manifest(vector), "valid-test-otp-proof", SecondFactor(vector));

        Assert.Equal("RESTORE_AUTHORIZED", result.Status);
        Assert.Equal("PAIRED_DEVICE_APPROVAL", result.SecondFactor);
        Assert.NotNull(result.Grant);
        Assert.Equal(41L, service.ExportAuthorized(result.Grant!).Manifest.ThroughOperationSeq);
        Assert.Equal(1, cloud.ExportCalls);
        Assert.Equal(0, cloud.ActivationCalls);
        Assert.Equal(3L, cloud.CurrentEpoch);
    }

    [Fact]
    public void C12_RESTORE_004_HashMismatchIsReviewAndCannotActivate()
    {
        var vector = VectorFile.Get("RESTORE-004");
        var cloud = VectorCloud(vector);
        var verification = StoreRestoreService.VerifyReplayedState(Manifest(vector), ReplayedState(vector));

        Assert.Equal("CONSISTENCY_MISMATCH", verification.Status);
        Assert.Equal("MISMATCH", verification.ConsistencyCheck);
        Assert.Equal("REVIEW", verification.Quarantine);
        Assert.Equal(vector.GetProperty("expected_output").GetProperty("computed_state_hash").GetString(), verification.ComputedStateHash);
        Assert.False(verification.CanActivate);
        Assert.Null(verification.NewWriterEpoch);
        Assert.Equal(0, cloud.ActivationCalls);
        Assert.Equal(3L, cloud.CurrentEpoch);
    }

    [Fact]
    public void C12_PostgresProjectionCorruptionCannotSelfCertifyOrTouchFreshHub()
    {
        using var temporary = new TempDirectory();
        var storeId = Guid.NewGuid();
        var oldWriter = new HubWriterIdentity(Guid.NewGuid(), 3);
        var replacement = Guid.NewGuid();
        var state = JsonSerializer.SerializeToElement(new HubReplayedState
        {
            Store = new HubStoreRestore { StoreId = storeId },
            WriterEpoch = oldWriter.Epoch
        }, HubJson.Options);
        _database.Store.Seed(new CloudSeed
        {
            StoreId = storeId,
            ActiveWriterDeviceId = oldWriter.DeviceId,
            ActiveWriterEpoch = oldWriter.Epoch,
            AcceptedThroughSeq = 0,
            EntryCount = 0,
            ProjectionVersion = 0
        });
        _database.Store.PublishRestoreProjection(storeId, state);
        var manifest = _database.Store.GetRestoreManifest(storeId)!;
        var corrupted = JsonSerializer.Deserialize<HubReplayedState>(state.GetRawText(), HubJson.Options)!;
        corrupted.DisplayNumberHighWatermark = 1;
        using (var connection = new NpgsqlConnection(_database.ConnectionString))
        {
            connection.Open();
            using var command = new NpgsqlCommand(
                "UPDATE cloud_restore_states SET replayed_state = @state WHERE store_id = @store", connection);
            command.Parameters.AddWithValue("store", storeId);
            command.Parameters.AddWithValue("state", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(corrupted, HubJson.Options));
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        var service = Service(_database.Store, out _);
        var authorization = service.Authorize(
            new RestoreAuthorizationRequest("restore-corrupt", storeId, replacement, DateTimeOffset.UnixEpoch),
            manifest, "valid-test-otp-proof", new RestoreSecondFactor("recovery_key", "valid-test-recovery-key-proof"));
        using var fresh = new SqliteHubStore(temporary.FreshConnection, oldWriter, new RestoreClock());
        var report = service.CompleteAuthorizedRestore(authorization.Grant!, fresh);

        Assert.Equal("CONSISTENCY_MISMATCH", report.Status);
        Assert.NotEqual(manifest.StateHash, report.ComputedStateHash);
        Assert.Null(report.NewWriterEpoch);
        Assert.Equal(0L, fresh.GetDisplayNumberHighWatermark(storeId));
        Assert.Empty(fresh.GetDevices(storeId));
        Assert.Equal(3L, _database.Store.GetSnapshot(storeId)!.ActiveWriterEpoch);
    }

    [Fact]
    public void C12_FAIL_001_UnsyncedOperationsAreExplicitlyUnrecoverable()
    {
        var vector = VectorFile.Get("FAIL-001");
        var expected = vector.GetProperty("expected_output");
        var lost = vector.GetProperty("input").GetProperty("given").GetProperty("failed_hub")
            .GetProperty("unsynced_operation_seqs").EnumerateArray().Select(value => value.GetInt64()).ToArray();
        var newWriter = vector.GetProperty("input").GetProperty("steps")[0].GetProperty("new_hub_device_id").GetGuid();
        var report = StoreRestoreService.AssessLoss(Manifest(vector), lost, newWriter);

        Assert.Equal("RESTORED_WITH_UNSYNCED_LOSS", report.Status);
        Assert.Equal(lost, report.UnrecoveredOperationSeqs);
        Assert.True(report.OwnerLossAcknowledgementRequired);
        Assert.Equal(41L, report.RestoredThroughSeq);
        Assert.Equal(42L, report.NextOperationSeq);
        Assert.Equal(4L, report.NewWriterEpoch);
        Assert.Equal(newWriter, report.NewWriterDeviceId);
        Assert.Equal(expected.GetProperty("status").GetString(), report.Status);
    }

    [Fact]
    public void C12_SmsShapedSecondFactorIsStillDenied()
    {
        var vector = VectorFile.Get("RESTORE-001");
        var cloud = VectorCloud(vector);
        var service = Service(cloud, out _);
        var result = service.Authorize(
            Request(vector), Manifest(vector), "valid-test-otp-proof", new RestoreSecondFactor("sms_otp", "valid-test-otp-proof"));

        Assert.Equal("SECOND_FACTOR_INVALID", result.Status);
        Assert.Null(result.Grant);
        Assert.Equal(0, cloud.ExportCalls);
        Assert.Equal(0, cloud.ActivationCalls);
    }

    [Fact]
    public void C12_UnsyncedLossRequiresAcknowledgementBeforeActivation()
    {
        using var temporary = new TempDirectory();
        var storeId = Guid.NewGuid();
        var oldWriter = Guid.NewGuid();
        var replacement = Guid.NewGuid();
        var state = JsonSerializer.SerializeToElement(new HubReplayedState
        {
            Store = new HubStoreRestore { StoreId = storeId },
            WriterEpoch = 3
        }, HubJson.Options);
        var manifest = new RestoreManifest(
            storeId, 0, HubReplicationConstants.ZeroHash, CanonicalJson.ReplayedStateHash(state), 3, oldWriter);
        var cloud = new ObservedRestoreCloud(manifest, state);
        var service = Service(cloud, out _);
        var authorization = service.Authorize(
            new RestoreAuthorizationRequest("restore-loss", storeId, replacement, DateTimeOffset.UnixEpoch),
            manifest, "valid-test-otp-proof", new RestoreSecondFactor("recovery_key", "valid-test-recovery-key-proof"));
        using var fresh = new SqliteHubStore(temporary.FreshConnection, new HubWriterIdentity(oldWriter, 3), new RestoreClock());

        var blocked = service.CompleteAuthorizedRestore(authorization.Grant!, fresh, new[] { 1L });
        Assert.Equal("LOSS_ACKNOWLEDGEMENT_REQUIRED", blocked.Status);
        Assert.Equal(new[] { 1L }, blocked.UnrecoveredOperationSeqs);
        Assert.Null(blocked.NewWriterEpoch);
        Assert.Equal(0, cloud.ActivationCalls);

        var acknowledged = service.CompleteAuthorizedRestore(authorization.Grant!, fresh, new[] { 1L }, true);
        Assert.Equal("RESTORED_WITH_UNSYNCED_LOSS", acknowledged.Status);
        Assert.Equal(new[] { 1L }, acknowledged.UnrecoveredOperationSeqs);
        Assert.True(acknowledged.OwnerLossAcknowledgementRequired);
        Assert.Equal(4L, acknowledged.NewWriterEpoch);
        Assert.Equal(1, cloud.ActivationCalls);
    }

    [Fact]
    public void C12_PopulatedHubCloudExportFreshHubRoundTripPreservesHashAndContinuesAtNextSequence()
    {
        using var temporary = new TempDirectory();
        var storeId = Guid.NewGuid();
        var otherStoreId = Guid.NewGuid();
        var oldWriter = new HubWriterIdentity(Guid.NewGuid(), 3);
        var replacement = Guid.NewGuid();
        var clock = new RestoreClock();
        var ownerId = Guid.NewGuid();
        string originalSessionToken;
        OperationRequest firstRequest;
        long restoredThrough;
        string originalHash;
        string recoveredHead;

        _database.Store.Seed(new CloudSeed
        {
            StoreId = storeId,
            ActiveWriterDeviceId = oldWriter.DeviceId,
            ActiveWriterEpoch = oldWriter.Epoch,
            AcceptedThroughSeq = 0,
            EntryCount = 0,
            ProjectionVersion = 0
        });

        using (var original = new SqliteHubStore(temporary.OriginalConnection, oldWriter, clock))
        {
            var owner = original.IssueOwnerSession(
                storeId, oldWriter.DeviceId, ownerId, "original-session", clock.UtcNow, clock.UtcNow.AddHours(1));
            originalSessionToken = owner.SessionToken;
            var product = new Product(storeId, "prod-restore", "شیر", "شير", "شیر", 12_500, 2, true, "۱ لیتر");
            firstRequest = Operation(storeId, oldWriter.DeviceId, "op-restore-1", new { kind = "product-and-inventory" });
            var inventory = new InventoryEvent(
                Guid.NewGuid(), storeId, product.ProductId, 7_000, InventoryEventType.Receipt,
                firstRequest.OperationId, oldWriter.DeviceId);
            var first = original.Execute(firstRequest, transaction =>
            {
                transaction.SaveProduct(product);
                transaction.AppendInventory(inventory);
                return "{\"status\":\"SUCCEEDED\",\"result_status\":\"ACCEPTED\",\"response\":{}}";
            });
            Assert.Equal(OperationExecutionStatus.Succeeded, first.Status);

            var issuedId = Guid.NewGuid();
            var issuedDraft = Operation(storeId, oldWriter.DeviceId, "op-restore-2", new { invoice_id = issuedId });
            var line = new InvoiceItem(Guid.NewGuid(), storeId, 1, "شیر", 2_000, 12_500, 25_000, product.ProductId);
            Assert.Equal(OperationExecutionStatus.Succeeded, original.CreateDraft(
                issuedDraft, issuedId, clock.UtcNow, new[] { line }, "EXT-RESTORE-1").Status);
            var issue = Operation(storeId, oldWriter.DeviceId, "op-restore-3", new { invoice_id = issuedId });
            Assert.Equal(OperationExecutionStatus.Succeeded, original.IssueInvoice(issue, issuedId, clock.UtcNow).Status);

            var operations = new HubOperationService(original, original, clock);
            var activeDraft = Submit(operations, storeId, owner.Session, "op-restore-4", "invoice.draft_create", new { customer_phone = "+989121234567" });
            Assert.Equal("SUCCEEDED", activeDraft.Body.Status);
            var activeDraftId = activeDraft.Body.Response.GetProperty("invoice").GetProperty("invoice_id").GetGuid();
            var addLine = Submit(operations, storeId, owner.Session, "op-restore-5", "invoice.item_add", new
            {
                invoice_id = activeDraftId,
                description = "شیر",
                quantity_millis = 1_000,
                unit_price_rials = 12_500,
                line_total_rials = 12_500
            });
            Assert.Equal("SUCCEEDED", addLine.Body.Status);

            var otherRequest = Operation(otherStoreId, oldWriter.DeviceId, "op-other-store", new { kind = "isolated" });
            Assert.Equal(OperationExecutionStatus.Succeeded, original.Execute(otherRequest, transaction =>
            {
                transaction.SaveProduct(new Product(
                    otherStoreId, "prod-other", "Other", "other", "other", 1, 1));
                return "{\"status\":\"SUCCEEDED\"}";
            }).Status);

            var batch = original.ReadBatch(storeId, 100, 1_048_576)!;
            restoredThrough = batch.Entries!.Count;
            Assert.Equal(5L, restoredThrough);
            recoveredHead = batch.Entries[^1].EntryHash;
            var cloudBatch = JsonSerializer.Deserialize<OperationBatch>(
                JsonSerializer.Serialize(batch, HubJson.Options), CloudJson.Options)!;
            var ingest = _database.Store.Ingest(storeId, cloudBatch, "c12-populated-roundtrip");
            Assert.Equal(200, ingest.HttpStatus);
            original.Acknowledge(storeId, restoredThrough, recoveredHead, clock.UtcNow);

            var replayedState = original.ExportRestoreState(storeId, oldWriter.Epoch);
            originalHash = CanonicalJson.ReplayedStateHash(replayedState);
            _database.Store.PublishRestoreProjection(storeId, replayedState);
        }

        var manifest = _database.Store.GetRestoreManifest(storeId)!;
        Assert.Equal(originalHash, manifest.StateHash);
        Assert.Equal(recoveredHead, manifest.HeadEntryHash);
        var service = Service(_database.Store, out _);
        var request = new RestoreAuthorizationRequest("restore-real-1", storeId, replacement, clock.UtcNow);
        var authorization = service.Authorize(
            request, manifest, "valid-test-otp-proof", new RestoreSecondFactor("recovery_key", "valid-test-recovery-key-proof"));
        Assert.NotNull(authorization.Grant);

        using (var fresh = new SqliteHubStore(temporary.FreshConnection, oldWriter, clock))
        {
            var report = service.CompleteAuthorizedRestore(authorization.Grant!, fresh);
            Assert.Equal("RESTORED", report.Status);
            Assert.Equal(originalHash, report.ComputedStateHash);
            Assert.Equal(restoredThrough, report.RestoredThroughSeq);
            Assert.Equal(restoredThrough + 1, report.NextOperationSeq);
            Assert.Equal(4L, report.NewWriterEpoch);
            Assert.Equal(replacement, report.NewWriterDeviceId);
            Assert.False(report.OwnerLossAcknowledgementRequired);
            Assert.Empty(report.UnrecoveredOperationSeqs);
            Assert.False(report.SessionsRestored);
            Assert.Null(fresh.ReadBatch(storeId, 100, 1_048_576));
            Assert.False(fresh.Authenticate(originalSessionToken, clock.UtcNow).IsAuthenticated);
            Assert.Single(fresh.GetDevices(storeId));
            Assert.Single(fresh.GetDraftLocks());
            Assert.Equal(1L, fresh.GetDisplayNumberHighWatermark(storeId));
            Assert.Equal(originalHash, CanonicalJson.ReplayedStateHash(fresh.ExportRestoreState(storeId, oldWriter.Epoch)));
            var otherReplay = JsonSerializer.Deserialize<HubReplayedState>(
                fresh.ExportRestoreState(otherStoreId, oldWriter.Epoch).GetRawText(), HubJson.Options)!;
            Assert.Empty(otherReplay.Products);
            Assert.Empty(otherReplay.Operations);
            Assert.Empty(otherReplay.Devices);
            Assert.Empty(otherReplay.Invoices);
        }

        var cloudSnapshot = _database.Store.GetSnapshot(storeId)!;
        Assert.Equal(replacement, cloudSnapshot.ActiveWriterDeviceId);
        Assert.Equal(4L, cloudSnapshot.ActiveWriterEpoch);
        Assert.Equal(restoredThrough, cloudSnapshot.AcceptedThroughSeq);
        var stale = _database.Store.Ingest(storeId, new OperationBatch
        {
            BatchId = "c12-stale-after-restore",
            StoreId = storeId,
            WriterDeviceId = oldWriter.DeviceId,
            WriterEpoch = oldWriter.Epoch,
            Entries = new List<LogEntry>()
        }, "c12-stale-after-restore");
        Assert.Equal(409, stale.HttpStatus);
        Assert.Equal("WRITER_FENCED", Assert.IsType<ErrorResponse>(stale.Body).Code);

        using (var restored = new SqliteHubStore(
                   temporary.FreshConnection, new HubWriterIdentity(replacement, 4), clock))
        {
            var replay = restored.Execute(firstRequest, _ => throw new InvalidOperationException("Replay must not execute domain work."));
            Assert.Equal(OperationExecutionStatus.Replayed, replay.Status);
            Assert.Equal(1L, replay.OperationSeq);
            var nextRequest = Operation(storeId, replacement, "op-after-restore", new { kind = "new-writer" });
            var next = restored.Execute(nextRequest, _ => "{\"status\":\"SUCCEEDED\"}");
            Assert.Equal(restoredThrough + 1, next.OperationSeq);
            var pending = restored.ReadBatch(storeId, 100, 1_048_576)!;
            var entry = Assert.Single(pending.Entries!);
            Assert.Equal(restoredThrough + 1, entry.OperationSeq);
            Assert.Equal(recoveredHead, entry.PreviousEntryHash);
            Assert.Equal(replacement, entry.WriterDeviceId);
            Assert.Equal(4L, entry.WriterEpoch);
        }
    }

    private static StoreRestoreService Service(IRestoreExportStore cloud, out TestSecondFactor verifier)
    {
        verifier = new TestSecondFactor();
        return new StoreRestoreService(cloud, new TestOtpVerifier(), verifier);
    }

    private static OperationRequest Operation(Guid storeId, Guid deviceId, string operationId, object payload) =>
        new(storeId, deviceId, operationId, "test.seed", null, HubJson.Element(payload));

    private static HubOperationResult Submit(
        HubOperationService service,
        Guid storeId,
        HubSession session,
        string operationId,
        string operationType,
        object payload)
    {
        var element = HubJson.Element(payload);
        var domain = new OperationRequest(storeId, session.DeviceId, operationId, operationType, null, element);
        return service.Submit(storeId, session, new HubOperationRequest(
            "0", session.SessionId, storeId, session.DeviceId, operationId, operationType, null, element, domain.RequestHash, null));
    }

    private static RestoreManifest Manifest(JsonElement vector)
    {
        var manifest = vector.GetProperty("input").GetProperty("given").GetProperty("cloud_manifest");
        return new RestoreManifest(
            manifest.GetProperty("store_id").GetGuid(),
            manifest.GetProperty("through_operation_seq").GetInt64(),
            manifest.GetProperty("head_entry_hash").GetString()!,
            manifest.GetProperty("state_hash").GetString()!,
            manifest.GetProperty("writer_epoch").GetInt64(),
            Guid.Parse("22222222-2222-4222-8222-222222222222"));
    }

    private static RestoreAuthorizationRequest Request(JsonElement vector)
    {
        var given = vector.GetProperty("input").GetProperty("given");
        return new RestoreAuthorizationRequest(
            "restore-0001", given.GetProperty("cloud_manifest").GetProperty("store_id").GetGuid(),
            given.GetProperty("new_hub_device_id").GetGuid(), given.GetProperty("now").GetDateTimeOffset());
    }

    private static Guid NewDevice(JsonElement vector) =>
        vector.GetProperty("input").GetProperty("given").GetProperty("new_hub_device_id").GetGuid();

    private static RestoreSecondFactor SecondFactor(JsonElement vector)
    {
        var factor = vector.GetProperty("input").GetProperty("steps")[0].GetProperty("second_factor");
        var type = factor.GetProperty("type").GetString()!;
        if (type == "RECOVERY_KEY")
        {
            return new RestoreSecondFactor("recovery_key", factor.GetProperty("proof").GetString());
        }

        var approval = factor.GetProperty("approval");
        return new RestoreSecondFactor("paired_device_approval", Approval: new RestorePairedDeviceApproval(
            approval.GetProperty("device_id").GetGuid(),
            approval.GetProperty("expires_at").GetDateTimeOffset(),
            approval.GetProperty("manifest_state_hash").GetString()!,
            approval.GetProperty("new_hub_device_id").GetGuid(),
            approval.GetProperty("restore_request_id").GetString()!,
            approval.GetProperty("signature").GetString()!));
    }

    private static JsonElement ReplayedState(JsonElement vector) => vector.GetProperty("input").GetProperty("steps")
        .EnumerateArray().Single(step => step.GetProperty("action").GetString() == "restore_verify_replayed_state")
        .GetProperty("replayed_state").Clone();

    private static ObservedRestoreCloud VectorCloud(JsonElement vector) => new(Manifest(vector), ReplayedStateOrEmpty(vector));

    private static JsonElement ReplayedStateOrEmpty(JsonElement vector)
    {
        try
        {
            return ReplayedState(vector);
        }
        catch (InvalidOperationException)
        {
            return HubJson.Element(new { });
        }
    }

    private sealed class TestOtpVerifier : IRestoreOtpProofVerifier
    {
        public bool Verify(Guid storeId, string otpProof, DateTimeOffset now) => otpProof == "valid-test-otp-proof";
    }

    private sealed class TestSecondFactor : IRestoreSecondFactorVerifier
    {
        public bool Verify(RestoreSecondFactor factor, RestoreAuthorizationContext context)
        {
            if (factor.Type == "recovery_key")
            {
                return factor.RecoveryKeyProof == "valid-test-recovery-key-proof";
            }

            if (factor.Type != "paired_device_approval" || factor.Approval is not { } approval)
            {
                return false;
            }

            return approval.DeviceId == Guid.Parse("33333333-3333-4333-8333-333333333333") &&
                   approval.ExpiresAt > context.Now && approval.ManifestStateHash == context.Manifest.StateHash &&
                   approval.NewHubDeviceId == context.NewHubDeviceId && approval.RestoreRequestId == context.RestoreRequestId &&
                   approval.Signature == "test-only-approval-signature";
        }
    }

    private sealed class ObservedRestoreCloud(RestoreManifest manifest, JsonElement state) : IRestoreExportStore
    {
        public int ExportCalls { get; private set; }
        public int ActivationCalls { get; private set; }
        public long CurrentEpoch { get; private set; } = manifest.WriterEpoch;
        public RestoreManifest? GetRestoreManifest(Guid storeId) => storeId == manifest.StoreId ? manifest : null;

        public RestoreExport? GetRestoreExport(Guid storeId)
        {
            if (storeId != manifest.StoreId)
            {
                return null;
            }

            ExportCalls++;
            return new RestoreExport(manifest, state, Array.Empty<JsonElement>());
        }

        public long ActivateRestoredWriter(Guid storeId, long expectedWriterEpoch, string expectedStateHash, Guid replacementWriterDeviceId)
        {
            ActivationCalls++;
            CurrentEpoch = expectedWriterEpoch + 1;
            return CurrentEpoch;
        }
    }

    private sealed class RestoreClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);
    }

    private sealed class TempDirectory : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "kandoo-c12", Guid.NewGuid().ToString("N"));

        public TempDirectory()
        {
            Directory.CreateDirectory(_directory);
        }

        public string OriginalConnection => $"Data Source={Path.Combine(_directory, "original.db")};Pooling=False";
        public string FreshConnection => $"Data Source={Path.Combine(_directory, "fresh.db")};Pooling=False";
        public void Dispose() => Directory.Delete(_directory, true);
    }

    private static class VectorFile
    {
        private static readonly JsonElement Root = LoadRoot();

        public static JsonElement Get(string id) => Root.GetProperty("vectors").EnumerateArray()
            .Single(vector => vector.GetProperty("id").GetString() == id);

        private static JsonElement LoadRoot()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "docs", "sync-protocol-test-vectors.json");
                if (File.Exists(candidate))
                {
                    return JsonDocument.Parse(File.ReadAllText(candidate)).RootElement.Clone();
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException("Could not locate docs/sync-protocol-test-vectors.json from the test output directory.");
        }
    }
}
