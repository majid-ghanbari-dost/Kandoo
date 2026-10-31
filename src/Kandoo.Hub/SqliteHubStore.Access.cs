using System.Globalization;
using Kandoo.Core;
using Microsoft.Data.Sqlite;

namespace Kandoo.Hub;

public sealed partial class SqliteHubStore
{
    public HubPairingGrant CreatePairingGrant(
        Guid storeId,
        Guid hubDeviceId,
        Guid ownerUserId,
        string hubEndpoint,
        string pairingId,
        string proofHash,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        string operatorLevel = HubOperatorLevel.Operator)
    {
        if (!HubOperatorLevel.IsSupported(operatorLevel))
        {
            throw new ArgumentException("Unsupported operator level.", nameof(operatorLevel));
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            using var transaction = _connection.BeginTransaction();
            try
            {
                using (var hub = AccessCommand(transaction, "SELECT device_role, pairing_status, owner_user_id FROM devices WHERE store_id = $store AND device_id = $device;"))
                {
                    AddAccess(hub, "$store", AccessUuid(storeId));
                    AddAccess(hub, "$device", AccessUuid(hubDeviceId));
                    using var reader = hub.ExecuteReader();
                    if (!reader.Read() || reader.GetInt32(0) != (int)HubDeviceRole.Hub || reader.GetInt32(1) != (int)HubPairingStatus.Active ||
                        reader.IsDBNull(2) || Guid.Parse(reader.GetString(2)) != ownerUserId)
                    {
                        throw new InvalidOperationException("OWNER_HUB_MISMATCH");
                    }
                }

                using (var command = AccessCommand(transaction, """
                    INSERT INTO pairing_grants
                        (pairing_id, store_id, hub_device_id, owner_user_id, hub_endpoint,
                         proof_hash, issued_at, expires_at, consumed, operator_level)
                    VALUES ($id, $store, $hub, $owner, $endpoint, $proof, $issued, $expires, 0, $level);
                    """))
                {
                    AddAccess(command, "$id", pairingId);
                    AddAccess(command, "$store", AccessUuid(storeId));
                    AddAccess(command, "$hub", AccessUuid(hubDeviceId));
                    AddAccess(command, "$owner", AccessUuid(ownerUserId));
                    AddAccess(command, "$endpoint", hubEndpoint);
                    AddAccess(command, "$proof", proofHash);
                    AddAccess(command, "$issued", AccessDate(issuedAt));
                    AddAccess(command, "$expires", AccessDate(expiresAt));
                    AddAccess(command, "$level", operatorLevel);
                    command.ExecuteNonQuery();
                }

                InsertAudit(transaction, storeId, "PAIRING_GRANT_ISSUED", ownerUserId, hubDeviceId, issuedAt);
                transaction.Commit();
                return new HubPairingGrant(
                    pairingId, storeId, hubDeviceId, hubEndpoint, proofHash, issuedAt, expiresAt, false, operatorLevel);
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    public HubPairingConsumeResult ConsumePairingGrant(
        string pairingId,
        string pairingProof,
        Guid deviceId,
        string label,
        string platform,
        string devicePublicKey,
        string deviceCredential,
        string sessionId,
        DateTimeOffset now,
        DateTimeOffset sessionExpiresAt)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var transaction = _connection.BeginTransaction();
            try
            {
                GrantRow? grant;
                using (var command = AccessCommand(transaction, """
                    SELECT pairing_id, store_id, hub_device_id, hub_endpoint, proof_hash,
                           issued_at, expires_at, consumed, operator_level
                    FROM pairing_grants WHERE pairing_id = $id;
                    """))
                {
                    AddAccess(command, "$id", pairingId);
                    using var reader = command.ExecuteReader();
                    grant = reader.Read()
                        ? new GrantRow(
                            reader.GetString(0), Guid.Parse(reader.GetString(1)), Guid.Parse(reader.GetString(2)),
                            reader.GetString(3), reader.GetString(4), AccessParseDate(reader.GetString(5)),
                            AccessParseDate(reader.GetString(6)), reader.GetInt64(7) != 0,
                            reader.IsDBNull(8) ? null : reader.GetString(8))
                        : null;
                }

                if (grant is null)
                {
                    return new HubPairingConsumeResult("PAIRING_INVALID");
                }

                if (grant.Consumed)
                {
                    return new HubPairingConsumeResult("PAIRING_REPLAY_REJECTED");
                }

                if (now >= grant.ExpiresAt)
                {
                    return new HubPairingConsumeResult("PAIRING_EXPIRED");
                }

                if (!HubToken.FixedTimeEquals(grant.ProofHash, HubToken.Hash(pairingProof)))
                {
                    return new HubPairingConsumeResult("PAIRING_INVALID");
                }

                using (var existing = AccessCommand(transaction, "SELECT store_id FROM devices WHERE device_id = $device;"))
                {
                    AddAccess(existing, "$device", AccessUuid(deviceId));
                    var existingStore = existing.ExecuteScalar();
                    if (existingStore is not null && existingStore is not DBNull)
                    {
                        return new HubPairingConsumeResult("DEVICE_ALREADY_PAIRED");
                    }
                }

                var token = HubToken.NewToken();
                var credentialHash = HubToken.Hash(deviceCredential);
                using (var consume = AccessCommand(transaction, """
                    UPDATE pairing_grants SET consumed = 1, consumed_device_id = $device, consumed_at = $now
                    WHERE pairing_id = $id AND consumed = 0;
                    """))
                {
                    AddAccess(consume, "$device", AccessUuid(deviceId));
                    AddAccess(consume, "$now", AccessDate(now));
                    AddAccess(consume, "$id", pairingId);
                    if (consume.ExecuteNonQuery() != 1)
                    {
                        return new HubPairingConsumeResult("PAIRING_REPLAY_REJECTED");
                    }
                }

                using (var device = AccessCommand(transaction, """
                    INSERT INTO devices
                        (store_id, device_id, device_role, pairing_status, owner_user_id,
                         label, platform, device_public_key, credential_hash, paired_at, operator_level)
                    VALUES ($store, $device, $role, $status, NULL, $label, $platform, $key, $credential, $now, $level);
                    """))
                {
                    AddAccess(device, "$store", AccessUuid(grant.StoreId));
                    AddAccess(device, "$device", AccessUuid(deviceId));
                    AddAccess(device, "$role", (int)HubDeviceRole.Companion);
                    AddAccess(device, "$status", (int)HubPairingStatus.Active);
                    AddAccess(device, "$label", label);
                    AddAccess(device, "$platform", platform);
                    AddAccess(device, "$key", devicePublicKey);
                    AddAccess(device, "$credential", credentialHash);
                    AddAccess(device, "$now", AccessDate(now));
                    AddAccess(device, "$level", grant.OperatorLevel ?? HubOperatorLevel.Operator);
                    device.ExecuteNonQuery();
                }

                var issued = InsertSession(
                    transaction, sessionId, grant.StoreId, deviceId, HubDeviceRole.Companion,
                    null, token, now, sessionExpiresAt);
                InsertAudit(transaction, grant.StoreId, "DEVICE_PAIRED", null, deviceId, now);
                transaction.Commit();
                var resultDevice = new HubDevice(
                    grant.StoreId, deviceId, HubDeviceRole.Companion, HubPairingStatus.Active,
                    null, label, platform, devicePublicKey, grant.OperatorLevel ?? HubOperatorLevel.Operator);
                return new HubPairingConsumeResult(
                    "DEVICE_PAIRED", resultDevice, new HubSessionIssued(issued, token), deviceCredential);
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    public HubSessionIssued IssueOwnerSession(
        Guid storeId,
        Guid hubDeviceId,
        Guid ownerUserId,
        string sessionId,
        DateTimeOffset now,
        DateTimeOffset expiresAt)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var transaction = _connection.BeginTransaction();
            try
            {
                HubDevice? existing = null;
                using (var device = AccessCommand(transaction, """
                    SELECT store_id, device_id, device_role, pairing_status, owner_user_id,
                           label, platform, device_public_key, credential_hash, operator_level
                    FROM devices WHERE device_id = $device;
                    """))
                {
                    AddAccess(device, "$device", AccessUuid(hubDeviceId));
                    using var reader = device.ExecuteReader();
                    if (reader.Read())
                    {
                        existing = ReadAccessDevice(reader);
                    }
                }

                // A valid bootstrap proof does not promote a companion, rebind an
                // owner, move a device between stores, or activate a second Hub.
                // Repeating bootstrap for the identical active Hub only renews its session.
                if (existing is not null)
                {
                    if (existing.StoreId != storeId || existing.DeviceRole != HubDeviceRole.Hub ||
                        existing.PairingStatus != HubPairingStatus.Active || existing.OwnerUserId != ownerUserId)
                    {
                        throw new InvalidOperationException("OWNER_HUB_MISMATCH");
                    }
                }
                else
                {
                    using (var activeHub = AccessCommand(transaction, """
                        SELECT 1 FROM devices
                        WHERE store_id = $store AND device_role = $role AND pairing_status = $status
                        LIMIT 1;
                        """))
                    {
                        AddAccess(activeHub, "$store", AccessUuid(storeId));
                        AddAccess(activeHub, "$role", (int)HubDeviceRole.Hub);
                        AddAccess(activeHub, "$status", (int)HubPairingStatus.Active);
                        if (activeHub.ExecuteScalar() is not null)
                        {
                            throw new InvalidOperationException("HUB_ALREADY_ACTIVE");
                        }
                    }

                    using var device = AccessCommand(transaction, """
                        INSERT INTO devices
                            (store_id, device_id, device_role, pairing_status, owner_user_id,
                             label, platform, device_public_key, credential_hash, paired_at)
                        VALUES ($store, $device, $role, $status, $owner, 'Store Hub', 'HUB', NULL, '', $now);
                        """);
                    AddAccess(device, "$store", AccessUuid(storeId));
                    AddAccess(device, "$device", AccessUuid(hubDeviceId));
                    AddAccess(device, "$role", (int)HubDeviceRole.Hub);
                    AddAccess(device, "$status", (int)HubPairingStatus.Active);
                    AddAccess(device, "$owner", AccessUuid(ownerUserId));
                    AddAccess(device, "$now", AccessDate(now));
                    device.ExecuteNonQuery();
                }

                var token = HubToken.NewToken();
                var session = InsertSession(
                    transaction, sessionId, storeId, hubDeviceId, HubDeviceRole.Hub,
                    ownerUserId, token, now, expiresAt);
                InsertAudit(transaction, storeId, "OWNER_SESSION_ISSUED", ownerUserId, hubDeviceId, now);
                transaction.Commit();
                return new HubSessionIssued(session, token);
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    public HubSessionResumeResult ResumeSession(
        Guid storeId,
        Guid deviceId,
        string deviceCredential,
        string sessionId,
        DateTimeOffset now,
        DateTimeOffset expiresAt)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var transaction = _connection.BeginTransaction();
            try
            {
                string? credentialHash = null;
                HubDevice? device = null;
                using (var command = AccessCommand(transaction, """
                    SELECT store_id, device_id, device_role, pairing_status, owner_user_id,
                           label, platform, device_public_key, credential_hash, operator_level
                    FROM devices WHERE store_id = $store AND device_id = $device;
                    """))
                {
                    AddAccess(command, "$store", AccessUuid(storeId));
                    AddAccess(command, "$device", AccessUuid(deviceId));
                    using var reader = command.ExecuteReader();
                    if (reader.Read())
                    {
                        device = ReadAccessDevice(reader);
                        credentialHash = reader.GetString(8);
                    }
                }

                if (device is null)
                {
                    return new HubSessionResumeResult("DEVICE_NOT_PAIRED");
                }

                if (device.PairingStatus == HubPairingStatus.Revoked)
                {
                    return new HubSessionResumeResult("DEVICE_REVOKED");
                }

                if (!HubToken.FixedTimeEquals(credentialHash!, HubToken.Hash(deviceCredential)))
                {
                    return new HubSessionResumeResult("DEVICE_CREDENTIAL_INVALID");
                }

                var token = HubToken.NewToken();
                var session = InsertSession(
                    transaction, sessionId, storeId, deviceId, device.DeviceRole,
                    device.OwnerUserId, token, now, expiresAt);
                transaction.Commit();
                return new HubSessionResumeResult("SESSION_ACTIVE", new HubSessionIssued(session, token));
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    public HubAuthenticationResult Authenticate(string sessionToken, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
        {
            return new HubAuthenticationResult("SESSION_INVALID");
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            using var transaction = _connection.BeginTransaction();
            SessionRow? row;
            using (var command = AccessCommand(transaction, """
                SELECT session_id, store_id, device_id, device_role, owner_user_id,
                       issued_at, expires_at, status
                FROM sessions WHERE token_hash = $token;
                """))
            {
                AddAccess(command, "$token", HubToken.Hash(sessionToken));
                using var reader = command.ExecuteReader();
                row = reader.Read()
                    ? new SessionRow(
                        reader.GetString(0), Guid.Parse(reader.GetString(1)), Guid.Parse(reader.GetString(2)),
                        (HubDeviceRole)reader.GetInt32(3), reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4)),
                        AccessParseDate(reader.GetString(5)), AccessParseDate(reader.GetString(6)),
                        (HubSessionStatus)reader.GetInt32(7))
                    : null;
            }

            if (row is null)
            {
                return new HubAuthenticationResult("SESSION_INVALID");
            }

            HubDevice? device = null;
            using (var command = AccessCommand(transaction, """
                SELECT store_id, device_id, device_role, pairing_status, owner_user_id,
                       label, platform, device_public_key, credential_hash, operator_level
                FROM devices WHERE store_id = $store AND device_id = $device;
                """))
            {
                AddAccess(command, "$store", AccessUuid(row.StoreId));
                AddAccess(command, "$device", AccessUuid(row.DeviceId));
                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    device = ReadAccessDevice(reader);
                }
            }

            if (device is null)
            {
                return new HubAuthenticationResult("DEVICE_NOT_PAIRED");
            }

            if (device.PairingStatus == HubPairingStatus.Revoked)
            {
                return new HubAuthenticationResult("DEVICE_REVOKED");
            }

            if (device.DeviceRole != row.DeviceRole || device.OwnerUserId != row.OwnerUserId)
            {
                return new HubAuthenticationResult("SESSION_INVALID");
            }

            if (row.Status == HubSessionStatus.Revoked)
            {
                return new HubAuthenticationResult("SESSION_REVOKED");
            }

            if (row.Status == HubSessionStatus.Expired || now >= row.ExpiresAt)
            {
                using var expire = AccessCommand(transaction, "UPDATE sessions SET status = $expired WHERE session_id = $id AND status = $active;");
                AddAccess(expire, "$expired", (int)HubSessionStatus.Expired);
                AddAccess(expire, "$id", row.SessionId);
                AddAccess(expire, "$active", (int)HubSessionStatus.Active);
                expire.ExecuteNonQuery();
                transaction.Commit();
                return new HubAuthenticationResult("SESSION_EXPIRED");
            }

            transaction.Commit();
            var session = new HubSession(
                row.SessionId, row.StoreId, row.DeviceId, row.DeviceRole, row.OwnerUserId,
                row.IssuedAt, row.ExpiresAt, HubSessionStatus.Active);
            return new HubAuthenticationResult("ACTIVE", session, device);
        }
    }

    public IReadOnlyList<HubDevice> GetDevices(Guid storeId)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT store_id, device_id, device_role, pairing_status, owner_user_id,
                       label, platform, device_public_key, credential_hash, operator_level
                FROM devices WHERE store_id = $store ORDER BY device_id;
                """;
            AddAccess(command, "$store", AccessUuid(storeId));
            using var reader = command.ExecuteReader();
            var devices = new List<HubDevice>();
            while (reader.Read())
            {
                devices.Add(ReadAccessDevice(reader));
            }

            return devices;
        }
    }

    public HubDeviceLevelChangeResult ChangeDeviceLevel(
        Guid storeId, Guid deviceId, Guid ownerUserId, string operatorLevel, DateTimeOffset now)
    {
        if (!HubOperatorLevel.IsSupported(operatorLevel))
        {
            throw new ArgumentException("Unsupported operator level.", nameof(operatorLevel));
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            using var transaction = _connection.BeginTransaction();
            try
            {
                HubDevice? device;
                using (var command = AccessCommand(transaction, """
                    SELECT store_id, device_id, device_role, pairing_status, owner_user_id,
                           label, platform, device_public_key, credential_hash, operator_level
                    FROM devices WHERE store_id = $store AND device_id = $device;
                    """))
                {
                    AddAccess(command, "$store", AccessUuid(storeId));
                    AddAccess(command, "$device", AccessUuid(deviceId));
                    using var reader = command.ExecuteReader();
                    device = reader.Read() ? ReadAccessDevice(reader) : null;
                }

                if (device is null)
                {
                    return new HubDeviceLevelChangeResult("DEVICE_NOT_PAIRED");
                }

                if (device.DeviceRole != HubDeviceRole.Companion)
                {
                    return new HubDeviceLevelChangeResult("DEVICE_LEVEL_NOT_APPLICABLE");
                }

                if (device.PairingStatus == HubPairingStatus.Revoked)
                {
                    return new HubDeviceLevelChangeResult("DEVICE_REVOKED");
                }

                using (var update = AccessCommand(transaction, """
                    UPDATE devices SET operator_level = $level
                    WHERE store_id = $store AND device_id = $device
                      AND device_role = $companion AND pairing_status = $active;
                    """))
                {
                    AddAccess(update, "$level", operatorLevel);
                    AddAccess(update, "$store", AccessUuid(storeId));
                    AddAccess(update, "$device", AccessUuid(deviceId));
                    AddAccess(update, "$companion", (int)HubDeviceRole.Companion);
                    AddAccess(update, "$active", (int)HubPairingStatus.Active);
                    if (update.ExecuteNonQuery() != 1)
                    {
                        return new HubDeviceLevelChangeResult("DEVICE_NOT_PAIRED");
                    }
                }

                InsertAudit(transaction, storeId, "DEVICE_LEVEL_CHANGED", ownerUserId, deviceId, now);
                transaction.Commit();
                return new HubDeviceLevelChangeResult(
                    "DEVICE_LEVEL_CHANGED", device with { OperatorLevel = operatorLevel });
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    public bool RevokeDevice(Guid storeId, Guid deviceId, Guid ownerUserId, DateTimeOffset now)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var transaction = _connection.BeginTransaction();
            try
            {
                int changed;
                using (var command = AccessCommand(transaction, """
                    UPDATE devices SET pairing_status = $revoked, revoked_at = $now
                    WHERE store_id = $store AND device_id = $device AND device_role = $companion;
                    """))
                {
                    AddAccess(command, "$revoked", (int)HubPairingStatus.Revoked);
                    AddAccess(command, "$now", AccessDate(now));
                    AddAccess(command, "$store", AccessUuid(storeId));
                    AddAccess(command, "$device", AccessUuid(deviceId));
                    AddAccess(command, "$companion", (int)HubDeviceRole.Companion);
                    changed = command.ExecuteNonQuery();
                }

                if (changed == 0)
                {
                    return false;
                }

                using (var sessions = AccessCommand(transaction, "UPDATE sessions SET status = $revoked WHERE store_id = $store AND device_id = $device;"))
                {
                    AddAccess(sessions, "$revoked", (int)HubSessionStatus.Revoked);
                    AddAccess(sessions, "$store", AccessUuid(storeId));
                    AddAccess(sessions, "$device", AccessUuid(deviceId));
                    sessions.ExecuteNonQuery();
                }

                ReleaseDeviceLocks(transaction, storeId, deviceId, now);
                InsertAudit(transaction, storeId, "DEVICE_REVOKED", ownerUserId, deviceId, now);
                transaction.Commit();
                return true;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    public int DisconnectDevice(Guid storeId, Guid deviceId, DateTimeOffset now)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var transaction = _connection.BeginTransaction();
            try
            {
                var released = ReleaseDeviceLocks(transaction, storeId, deviceId, now);
                InsertAudit(transaction, storeId, "DEVICE_DISCONNECTED", null, deviceId, now);
                transaction.Commit();
                return released;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    private void InitializeAccessSchema()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS devices (
                store_id TEXT NOT NULL,
                device_id TEXT NOT NULL,
                device_role INTEGER NOT NULL,
                pairing_status INTEGER NOT NULL,
                owner_user_id TEXT NULL,
                label TEXT NOT NULL,
                platform TEXT NOT NULL,
                device_public_key TEXT NULL,
                credential_hash TEXT NOT NULL,
                paired_at TEXT NOT NULL,
                revoked_at TEXT NULL,
                operator_level TEXT NULL,
                PRIMARY KEY (store_id, device_id),
                UNIQUE (device_id)
            );
            CREATE TABLE IF NOT EXISTS pairing_grants (
                pairing_id TEXT PRIMARY KEY,
                store_id TEXT NOT NULL,
                hub_device_id TEXT NOT NULL,
                owner_user_id TEXT NOT NULL,
                hub_endpoint TEXT NOT NULL,
                proof_hash TEXT NOT NULL,
                issued_at TEXT NOT NULL,
                expires_at TEXT NOT NULL,
                consumed INTEGER NOT NULL,
                consumed_device_id TEXT NULL,
                consumed_at TEXT NULL,
                operator_level TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS sessions (
                session_id TEXT PRIMARY KEY,
                store_id TEXT NOT NULL,
                device_id TEXT NOT NULL,
                device_role INTEGER NOT NULL,
                owner_user_id TEXT NULL,
                issued_at TEXT NOT NULL,
                expires_at TEXT NOT NULL,
                status INTEGER NOT NULL,
                token_hash TEXT NOT NULL UNIQUE,
                FOREIGN KEY (store_id, device_id) REFERENCES devices(store_id, device_id)
            );
            CREATE TABLE IF NOT EXISTS access_audit_events (
                audit_id INTEGER PRIMARY KEY AUTOINCREMENT,
                store_id TEXT NOT NULL,
                event_type TEXT NOT NULL,
                actor_user_id TEXT NULL,
                device_id TEXT NOT NULL,
                occurred_at TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
        EnsureColumn("devices", "operator_level", "TEXT NULL");
        EnsureColumn("pairing_grants", "operator_level", "TEXT NULL");
    }

    private static HubSession InsertSession(
        SqliteTransaction transaction,
        string sessionId,
        Guid storeId,
        Guid deviceId,
        HubDeviceRole role,
        Guid? ownerUserId,
        string token,
        DateTimeOffset now,
        DateTimeOffset expiresAt)
    {
        using var command = AccessCommand(transaction, """
            INSERT INTO sessions
                (session_id, store_id, device_id, device_role, owner_user_id,
                 issued_at, expires_at, status, token_hash)
            VALUES ($id, $store, $device, $role, $owner, $issued, $expires, $status, $token);
            """);
        AddAccess(command, "$id", sessionId);
        AddAccess(command, "$store", AccessUuid(storeId));
        AddAccess(command, "$device", AccessUuid(deviceId));
        AddAccess(command, "$role", (int)role);
        AddAccess(command, "$owner", ownerUserId is Guid owner ? AccessUuid(owner) : null);
        AddAccess(command, "$issued", AccessDate(now));
        AddAccess(command, "$expires", AccessDate(expiresAt));
        AddAccess(command, "$status", (int)HubSessionStatus.Active);
        AddAccess(command, "$token", HubToken.Hash(token));
        command.ExecuteNonQuery();
        return new HubSession(sessionId, storeId, deviceId, role, ownerUserId, now, expiresAt, HubSessionStatus.Active);
    }

    private static int ReleaseDeviceLocks(SqliteTransaction transaction, Guid storeId, Guid deviceId, DateTimeOffset now)
    {
        using var command = AccessCommand(transaction, """
            UPDATE draft_locks SET state = $released, release_reason = $reason, released_at = $now
            WHERE store_id = $store AND device_id = $device AND state = $active;
            """);
        AddAccess(command, "$released", (int)DraftLockState.Released);
        AddAccess(command, "$reason", (int)LockReleaseReason.Disconnect);
        AddAccess(command, "$now", AccessDate(now));
        AddAccess(command, "$store", AccessUuid(storeId));
        AddAccess(command, "$device", AccessUuid(deviceId));
        AddAccess(command, "$active", (int)DraftLockState.Active);
        return command.ExecuteNonQuery();
    }

    private static void InsertAudit(
        SqliteTransaction transaction,
        Guid storeId,
        string eventType,
        Guid? actorUserId,
        Guid deviceId,
        DateTimeOffset now)
    {
        using var command = AccessCommand(transaction, """
            INSERT INTO access_audit_events (store_id, event_type, actor_user_id, device_id, occurred_at)
            VALUES ($store, $event, $actor, $device, $now);
            """);
        AddAccess(command, "$store", AccessUuid(storeId));
        AddAccess(command, "$event", eventType);
        AddAccess(command, "$actor", actorUserId is Guid actor ? AccessUuid(actor) : null);
        AddAccess(command, "$device", AccessUuid(deviceId));
        AddAccess(command, "$now", AccessDate(now));
        command.ExecuteNonQuery();
    }

    private static HubDevice ReadAccessDevice(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Guid.Parse(reader.GetString(1)),
        (HubDeviceRole)reader.GetInt32(2),
        (HubPairingStatus)reader.GetInt32(3),
        reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4)),
        reader.GetString(5),
        reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.IsDBNull(9) ? null : reader.GetString(9));

    private static SqliteCommand AccessCommand(SqliteTransaction transaction, string sql)
    {
        var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    private static void AddAccess(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static string AccessUuid(Guid value) => value.ToString("D");
    private static string AccessDate(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset AccessParseDate(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private sealed record GrantRow(
        string PairingId,
        Guid StoreId,
        Guid HubDeviceId,
        string HubEndpoint,
        string ProofHash,
        DateTimeOffset IssuedAt,
        DateTimeOffset ExpiresAt,
        bool Consumed,
        string? OperatorLevel);

    private sealed record SessionRow(
        string SessionId,
        Guid StoreId,
        Guid DeviceId,
        HubDeviceRole DeviceRole,
        Guid? OwnerUserId,
        DateTimeOffset IssuedAt,
        DateTimeOffset ExpiresAt,
        HubSessionStatus Status);
}
