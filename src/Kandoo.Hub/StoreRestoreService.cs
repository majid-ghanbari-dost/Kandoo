using System.Text.Json;
using Kandoo.Core;

namespace Kandoo.Hub;

public sealed class StoreRestoreService
{
    private readonly IRestoreExportStore _cloud;
    private readonly IRestoreOtpProofVerifier _otpVerifier;
    private readonly IRestoreSecondFactorVerifier _secondFactorVerifier;

    public StoreRestoreService(
        IRestoreExportStore cloud,
        IRestoreOtpProofVerifier otpVerifier,
        IRestoreSecondFactorVerifier secondFactorVerifier)
    {
        _cloud = cloud;
        _otpVerifier = otpVerifier;
        _secondFactorVerifier = secondFactorVerifier;
    }

    public RestoreAuthorizationResult Authorize(
        RestoreAuthorizationRequest request,
        RestoreManifest manifest,
        string otpProof,
        RestoreSecondFactor? secondFactor)
    {
        if (request.StoreId != manifest.StoreId || request.NewHubDeviceId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.RestoreRequestId))
        {
            return new RestoreAuthorizationResult("RESTORE_REQUEST_INVALID", null, null);
        }

        if (!_otpVerifier.Verify(request.StoreId, otpProof, request.Now))
        {
            return new RestoreAuthorizationResult("OTP_INVALID", null, null);
        }

        if (secondFactor is null)
        {
            return new RestoreAuthorizationResult("SECOND_FACTOR_REQUIRED", null, null);
        }

        var context = new RestoreAuthorizationContext(
            request.RestoreRequestId, request.StoreId, request.NewHubDeviceId, manifest, request.Now);
        if (!_secondFactorVerifier.Verify(secondFactor, context))
        {
            return new RestoreAuthorizationResult("SECOND_FACTOR_INVALID", secondFactor.Type, null);
        }

        if (!string.Equals(secondFactor.Type, "recovery_key", StringComparison.Ordinal) &&
            !string.Equals(secondFactor.Type, "paired_device_approval", StringComparison.Ordinal))
        {
            return new RestoreAuthorizationResult("SECOND_FACTOR_INVALID", secondFactor.Type, null);
        }

        return new RestoreAuthorizationResult(
            secondFactor.Type == "recovery_key" ? "AUTHORIZED_RECOVERY_KEY" : "RESTORE_AUTHORIZED",
            secondFactor.Type == "recovery_key" ? "RECOVERY_KEY" : "PAIRED_DEVICE_APPROVAL",
            new RestoreAuthorizationGrant(context, secondFactor.Type));
    }

    public RestoreExport ExportAuthorized(RestoreAuthorizationGrant grant)
    {
        var export = _cloud.GetRestoreExport(grant.StoreId)
            ?? throw new InvalidOperationException("RESTORE_STATE_INVALID");
        if (!ManifestMatches(grant.Manifest, export.Manifest))
        {
            throw new InvalidOperationException("RESTORE_MANIFEST_CHANGED");
        }

        return export;
    }

    public static RestoreVerificationResult VerifyReplayedState(RestoreManifest manifest, JsonElement replayedState)
    {
        var computed = CanonicalJson.ReplayedStateHash(replayedState);
        var matches = string.Equals(computed, manifest.StateHash, StringComparison.Ordinal);
        return new RestoreVerificationResult(
            matches ? "RESTORE_READY" : "CONSISTENCY_MISMATCH",
            matches ? "MATCH" : "MISMATCH",
            computed,
            matches,
            matches ? checked(manifest.WriterEpoch + 1) : null,
            matches ? null : "REVIEW");
    }

    public static RestoreCompletionResult AssessLoss(RestoreManifest manifest, IEnumerable<long> knownOperationSeqs, Guid newWriterDeviceId)
    {
        var lost = LostOperations(manifest, knownOperationSeqs);
        return new RestoreCompletionResult(
            lost.Count == 0 ? "RESTORED" : "RESTORED_WITH_UNSYNCED_LOSS",
            manifest.StateHash,
            manifest.ThroughOperationSeq,
            newWriterDeviceId,
            checked(manifest.WriterEpoch + 1),
            checked(manifest.ThroughOperationSeq + 1),
            lost.Count != 0,
            lost);
    }

    public RestoreCompletionResult CompleteAuthorizedRestore(
        RestoreAuthorizationGrant grant,
        SqliteHubStore freshHub,
        IEnumerable<long>? knownUnsyncedOperationSeqs = null,
        bool ownerLossAcknowledged = false)
    {
        var export = ExportAuthorized(grant);
        var verification = VerifyReplayedState(export.Manifest, export.ReplayedState);
        if (!verification.CanActivate)
        {
            return Completion("CONSISTENCY_MISMATCH", verification.ComputedStateHash, export.Manifest, grant.NewHubDeviceId, null, false, Array.Empty<long>());
        }

        if (export.ReplayedState.GetProperty("store").GetProperty("store_id").GetGuid() != export.Manifest.StoreId ||
            export.ReplayedState.GetProperty("through_operation_seq").GetInt64() != export.Manifest.ThroughOperationSeq ||
            export.ReplayedState.GetProperty("writer_epoch").GetInt64() != export.Manifest.WriterEpoch)
        {
            return Completion("RESTORE_STATE_INVALID", verification.ComputedStateHash, export.Manifest, grant.NewHubDeviceId, null, false, Array.Empty<long>());
        }

        IReadOnlyList<ReplicationLogEntry> entries;
        try
        {
            entries = VerifyEntries(export);
        }
        catch (InvalidOperationException)
        {
            return Completion("CLOUD_REPLICA_INVALID", verification.ComputedStateHash, export.Manifest, grant.NewHubDeviceId, null, false, Array.Empty<long>());
        }

        var lost = LostOperations(export.Manifest, knownUnsyncedOperationSeqs ?? Array.Empty<long>());
        if (lost.Count != 0 && !ownerLossAcknowledged)
        {
            return Completion("LOSS_ACKNOWLEDGEMENT_REQUIRED", verification.ComputedStateHash, export.Manifest, grant.NewHubDeviceId, null, true, lost);
        }

        try
        {
            freshHub.RestoreReplayedState(export.ReplayedState, entries, export.Manifest.StateHash);
        }
        catch (InvalidOperationException exception) when (exception.Message == "RESTORE_HASH_MISMATCH")
        {
            return Completion("CONSISTENCY_MISMATCH", verification.ComputedStateHash, export.Manifest, grant.NewHubDeviceId, null, false, lost);
        }

        var replayed = freshHub.ExportRestoreState(export.Manifest.StoreId, export.Manifest.WriterEpoch);
        var replayedHash = CanonicalJson.ReplayedStateHash(replayed);
        if (!string.Equals(replayedHash, export.Manifest.StateHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("RESTORE_HASH_MISMATCH");
        }

        var newEpoch = _cloud.ActivateRestoredWriter(
            export.Manifest.StoreId, export.Manifest.WriterEpoch, export.Manifest.StateHash, grant.NewHubDeviceId);
        return Completion(
            lost.Count == 0 ? "RESTORED" : "RESTORED_WITH_UNSYNCED_LOSS",
            replayedHash,
            export.Manifest,
            grant.NewHubDeviceId,
            newEpoch,
            lost.Count != 0,
            lost);
    }

    private static IReadOnlyList<ReplicationLogEntry> VerifyEntries(RestoreExport export)
    {
        if (export.Entries.Count != export.Manifest.ThroughOperationSeq)
        {
            throw new InvalidOperationException("CLOUD_REPLICA_INVALID");
        }

        var entries = new List<ReplicationLogEntry>();
        var previousHash = HubReplicationConstants.ZeroHash;
        for (var index = 0; index < export.Entries.Count; index++)
        {
            var entry = JsonSerializer.Deserialize<ReplicationLogEntry>(export.Entries[index].GetRawText(), HubJson.Options)
                ?? throw new InvalidOperationException("CLOUD_REPLICA_INVALID");
            var sequence = index + 1L;
            if (entry.StoreId != export.Manifest.StoreId || entry.OperationSeq != sequence ||
                !string.Equals(entry.PreviousEntryHash, previousHash, StringComparison.Ordinal) ||
                !string.Equals(CanonicalJson.EntryHash(export.Entries[index]), entry.EntryHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("CLOUD_REPLICA_INVALID");
            }

            entries.Add(entry);
            previousHash = entry.EntryHash;
        }

        if (!string.Equals(previousHash, export.Manifest.HeadEntryHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("CLOUD_REPLICA_INVALID");
        }

        return entries;
    }

    private static List<long> LostOperations(RestoreManifest manifest, IEnumerable<long> sequences) => sequences
        .Where(sequence => sequence > manifest.ThroughOperationSeq)
        .Distinct()
        .OrderBy(sequence => sequence)
        .ToList();

    private static bool ManifestMatches(RestoreManifest left, RestoreManifest right) =>
        left.StoreId == right.StoreId && left.ThroughOperationSeq == right.ThroughOperationSeq &&
        left.WriterEpoch == right.WriterEpoch && left.WriterDeviceId == right.WriterDeviceId &&
        string.Equals(left.HeadEntryHash, right.HeadEntryHash, StringComparison.Ordinal) &&
        string.Equals(left.StateHash, right.StateHash, StringComparison.Ordinal);

    private static RestoreCompletionResult Completion(
        string status,
        string hash,
        RestoreManifest manifest,
        Guid newWriter,
        long? newEpoch,
        bool lossAcknowledgementRequired,
        IReadOnlyList<long> lost) => new(
            status,
            hash,
            manifest.ThroughOperationSeq,
            newWriter,
            newEpoch,
            checked(manifest.ThroughOperationSeq + 1),
            lossAcknowledgementRequired,
            lost);
}
