# KANDOO — SYNC / REPLICATION PROTOCOL SPECIFICATION

**Document status:** P-02 specification, revised by owner-approved `P-0104r` for PM review

**Date:** `2026-10-23`

**Repository:** `majid-ghanbari-dost/Kandoo` (Parent Platform Repository)

**Starting state:** Arena commit `e8a935105bdfad7ab323b4a83ec83b8a4c44ecc2` (`stable-p-01`)

**Scope:** Logical protocol, failure behavior, and synthetic JSON test vectors only. This document does not implement C#, transport endpoints, a database schema, cryptography, or the Digital Invoice extraction pipeline.

---

## 1. Overview

### 1.1 Purpose

This specification defines three cooperating protocol paths:

1. **Device ↔ Store Hub over LAN:** owner-created QR pairing, sessions, revocation, idempotent operations, OCC, D11 locks, and Hub-off behavior.
2. **Store Hub → Cloud:** an append-only per-store operation log with ordered, idempotent, gap-sensitive, retryable replication and backpressure.
3. **Cloud → replacement Store Hub:** restore gated by SMS OTP **and** a second factor, followed by log verification, deterministic replay, and a replayed-state hash comparison before writer activation.

It refines `D7`, `D8`, `D10`, `D11`, `D12`, `D13`, and `SEC-1`; owner-approved `P-0104r` explicitly revises only the recorded D11 activity and D12 numbering semantics, while all other frozen decisions and the Canonical Invoice v0 contract remain unchanged. The 15 entities and operation/lock semantics are those in [`docs/domain-model.md`](domain-model.md).

All behavior below is a requirement to implement and test, not a claim that an implementation exists. Every security, concurrency, replication, and restore claim has at least one concrete synthetic vector in [`docs/sync-protocol-test-vectors.json`](sync-protocol-test-vectors.json) for P-05 and P-07.

### 1.2 Non-negotiable invariants

- Exactly one active Store Hub writer exists per store. Companion devices never maintain an authoritative peer-to-peer ledger.
- A companion whose Hub is unreachable cannot start a new authoritative store operation. A reachable Hub continues LAN work when internet/WAN is unavailable.
- The Hub assigns `operation_seq`, lock ownership, and Hub timestamps. It assigns `display_number` only at successful `ISSUE`; every pre-issuance/retired draft is null-numbered. Clients and Cloud do not reorder local operations or allocate numbers.
- Every accepted mutation has an `Operation` record and one durable log/outbox entry. Operation effects, idempotency response, lock changes, any successful-issue number/high-watermark change, and outbox append commit atomically.
- Cloud ingest is append-only and idempotent. It never silently fills gaps, overwrites a conflicting entry, or applies a partial batch.
- Restore never activates a replacement writer after OTP alone, after a failed second factor, or after a consistency mismatch.
- Unsynced data on a lost Hub disk is not claimed to be recoverable from Cloud. Divergent unsynced data from a fenced writer is quarantined for review, never automatically merged.

### 1.3 Terms

| Term | Meaning |
| --- | --- |
| `writer_device_id` | Device ID of the currently authorized Store Hub writer. It is distinct from the companion `device_id` that originated an operation. |
| `writer_epoch` | Positive fencing generation for a store. Authorized replacement-Hub activation increments it; ordinary companion pairing does not. |
| `operation_seq` | Hub-assigned, contiguous, strictly increasing sequence within a store log. It continues across authorized writer epochs after restore. |
| `accepted_through_seq` | Cloud high watermark: the last contiguous operation sequence durably ingested for the store. |
| `display_number_high_watermark` | Last display number allocated by a successful `ISSUE`, or `0` if none; drafts and retired drafts never advance it. |
| `next_display_number` | Test-fixture convenience equal to the issued-only high watermark plus one; it is only a candidate until a successful `ISSUE`, never a draft allocation. |
| `entry_hash` | SHA-256 of one canonical log entry, excluding the `entry_hash` field itself. |
| `previous_entry_hash` | Hash of the preceding store-log entry, linking sequence and content regardless of writer epoch. |
| `outbox` | Durable Hub-local suffix of log entries not yet acknowledged by Cloud. Entries are removed only after the required acknowledgment. |
| `cloud_cursor` | Opaque Cloud synchronization cursor assigned to newly ingested entries. It is not a substitute for `operation_seq` or a gap detector. |
| `state_hash` | SHA-256 of the canonical replayed store-state projection defined in Section 4.5. |
| `FENCED_READ_ONLY` | Terminal local writer mode after a Hub learns that its `writer_epoch` is stale. New mutations stop; unsynced entries remain available for review. |

### 1.4 Wire and hash conventions

The test-vector protocol identifier is `kandoo.sync.v0`, with `protocol_version: "0"` and `schema_version: "0"`. These versions identify this P-02 specification; they do not revise Canonical Invoice v0. `P-0104r` corrects this pre-implementation documentation/vector baseline in place; it does not authorize mixed decoders. Legacy `quantity` / `quantity_delta` names or draft-time display-number allocation are invalid after this revision and must fail validation/review rather than be silently coerced.

Canonical JSON used for hashes has this profile:

- UTF-8 encoding.
- Object keys sorted lexicographically by Unicode code point.
- Array order preserved. State entity collections are first sorted as specified in Section 4.5.
- No insignificant whitespace: separators are `,` and `:`.
- JSON integers in all hash-bearing vectors; NaN and Infinity are forbidden.
- Under `ASM-22`, quantities are signed scaled-integer thousandths: invoice-item payload/state uses `quantity_millis`, inventory-movement payload/state uses `quantity_delta_millis`, and derived stock projections use `stock_millis` (`1000 = 1.000`; `-2000 = -2.000`). Floating point, decimal strings, and locale-sensitive parsing are forbidden. Integer Rial fields remain unscaled.
- SHA-256 output is lowercase 64-character hexadecimal.

Hash inputs are exactly:

```text
request_hash = SHA256(canonical_json({
  store_id, device_id, operation_id, operation_type, base_version, payload
}))

entry_hash = SHA256(canonical_json(log_entry_without_entry_hash))

state_hash = SHA256(canonical_json(replayed_store_state_projection))
```

`session_id`, `client_sent_at`, transport headers, retry counts, and Cloud receive timestamps are excluded from operation identity. Therefore, a legitimate retry may use a new session without becoming a new operation.

All timestamps are RFC 3339 UTC instants in vectors. `client_sent_at` is informational. Lock expiry and local ordering use Hub time; Cloud ingest order uses sequence and hash chain, not timestamps.

### 1.5 Logical outcomes

Outcome names such as `VERSION_CONFLICT` and `WRITER_FENCED` are protocol-logical results. HTTP status mapping is deferred. A pre-authentication or malformed-request failure creates no domain operation. A well-authenticated domain rejection may be durably recorded as `Operation.result_status = REJECTED` so an exact retry receives the same result, but it has no prohibited domain effects.

---

## 2. Device ↔ Hub Protocol (LAN)

### 2.1 Transport and authority boundary

- Companions communicate only with their store's Hub over LAN-authenticated HTTPS. No Bluetooth, peer-to-peer ledger, direct companion-to-Cloud mutation, or internet dependency is introduced.
- The QR supplies the Hub endpoint and pairing proof; clients must not hard-code `localhost` or a loopback address for another device.
- The Hub validates the device pairing, session, explicit `store_id`, active user membership, and command authorization on every operation.
- Companion pairing can create only `Device.device_role = COMPANION`. A second Hub cannot be activated through this flow; replacement Hub activation is exclusively the restore flow in Section 4.

### 2.2 Owner-created QR pairing

#### QR issuance

An authenticated, active `OWNER` acting on the current Store Hub requests a pairing grant. The Hub generates a cryptographically random, single-use pairing secret and returns/prints a QR containing or securely referencing:

| Field | Required | Meaning |
| --- | --- | --- |
| `protocol_version` | Yes | Pairing message version. |
| `pairing_id` | Yes | Unique administrative pairing grant. |
| `store_id` | Yes | Store to which the companion will be bound. |
| `hub_device_id` | Yes | Current Hub issuing the grant. |
| `device_role` | Yes | Always `COMPANION` in this flow. |
| `hub_endpoint` | Yes | Reachable LAN Hub endpoint; no public endpoint is required. |
| `nonce` / pairing proof | Yes | One-time high-entropy proof. It is never logged or reused. |
| `issued_at`, `expires_at` | Yes | Hub-issued validity window. The conservative default is 300 seconds. |

The QR contains no customer records, invoice data, recovery key, owner password, or long-lived device credential. It expires at the Hub timestamp even if a companion clock differs.

#### QR consumption

The companion scans the QR and submits its newly generated `device_id`, label, platform, device public key, and pairing proof before expiry. The Hub must atomically:

1. Verify the grant is unconsumed, unexpired, store/hub bound, and owner-authorized.
2. Verify the new device is not actively paired to another store.
3. Consume the grant so it cannot be replayed.
4. Persist an active `COMPANION` Device and return an initial session bound to that device.

An expired, malformed, already consumed, or wrong-proof QR returns `PAIRING_EXPIRED`, `PAIRING_INVALID`, or `PAIRING_REPLAY_REJECTED` as applicable, with no Device or Session effect. A QR cannot grant `HUB` role.

**Vectors:** `LAN-PAIR-001`, `LAN-PAIR-002`, `LAN-PAIR-003`, `LAN-PAIR-004`.

### 2.3 Sessions

A session contains at least `session_id`, `store_id`, `device_id`, `device_role`, `issued_at`, `expires_at`, and status. It is bound to the paired device credential and is not transferable to another device.

- The Hub returns explicit session expiry; the one-hour values in vectors are fixtures, not a newly frozen product default.
- A session can be resumed/renewed only while the Device remains active and credential proof is valid. Renewing a session never changes `operation_id`, `request_hash`, `operation_seq`, or lock ownership by itself.
- An expired session returns `SESSION_EXPIRED`; the companion obtains a new session before retrying the unchanged operation.
- Session state is runtime state and is excluded from the restore state hash. Sessions themselves are not restored onto a replacement Hub.
- Every privileged pairing, revocation, and restore authorization produces an audit event under `SEC-1`; credentials and proofs are not written to logs.

**Vector:** `LAN-SESSION-001`.

### 2.4 Revocation

An active owner can revoke a Device. Revocation atomically:

- Sets `Device.pairing_status = REVOKED`.
- Revokes all sessions for that device.
- Releases any active lock owned by that device with `release_reason = DISCONNECT`.
- Prevents subsequent operations, session renewal, and lock reacquisition by that device.

Revocation does not delete historical Operations, InventoryEvents, Invoices, or Locks. A late request returns `DEVICE_REVOKED` or `LOCK_NOT_OWNED` and has no new domain effect.

**Vector:** `LAN-SESSION-002`.

### 2.5 Operation envelope and Hub commit

Every mutation uses this logical envelope:

| Field | Required | Rule |
| --- | --- | --- |
| `protocol_version` | Yes | Message version supported by both peers. |
| `session_id` | Yes | Active session bound to the same store/device. |
| `store_id` | Yes | Explicit tenant scope. |
| `device_id` | Yes | Active companion paired to `store_id`; must match session. |
| `operation_id` | Yes | Client-generated idempotency key, unique within the store. |
| `operation_type` | Yes | Logical domain command. |
| `base_version` | Conditional | Required for versioned edits such as product price/specification; `null` where OCC does not apply. |
| `payload` | Yes | Command data validated against `operation_type`. |
| `request_hash` | Yes | Recomputed by Hub using Section 1.4. |
| `client_sent_at` | No | Informational only; never authoritative for expiry or ordering. |

The Hub handles a mutation in this order:

1. Authenticate session, device, store, membership, and command permission.
2. Validate message shape and recompute `request_hash`.
3. Look up `(store_id, operation_id)`. An exact match of device, operation type, and request hash returns the stored response with zero new effects. Any mismatch returns `OPERATION_ID_CONFLICT` without replacing the original.
4. Evaluate `base_version`, lock ownership, lifecycle, and store invariants. Stale OCC returns `VERSION_CONFLICT` with current version and never silently overwrites.
5. Assign Hub time and, for a newly accepted operation, the next `operation_seq`.
6. In one durable transaction, write the Operation/result, domain effects, lock or display-number changes, response snapshot, and append-only outbox log entry.
7. Return the response only after durable commit. If the response is lost, the client retries the identical operation.

Draft creation and pre-issue changes carry `display_number=null` and leave the numbering high watermark unchanged. A successful `invoice.issue` validates first, then allocates the next gapless store number and advances the issued-only high watermark in the same transaction as the invoice, Operation response, lock release, and outbox entry. A failed issue allocates nothing; an exact issue replay returns the original number without advancing the watermark.

The Cloud never assigns a different `operation_id`, `request_hash`, display number, or local sequence for an accepted entry.

**Vectors:** `LAN-OP-001`, `LAN-OP-002`, `LAN-OP-003`, `LAN-OP-004`.

### 2.6 D11 per-phone lock protocol

#### Acquire and replay

A phone-associated `invoice.draft_create` operation includes the customer phone in its payload. The Hub normalizes the phone before evaluating `(store_id, customer_phone)` and atomically creates the null-numbered draft and active Lock without advancing the issued-only display-number high watermark.

- If another device owns an active, unexpired lock, a different acquisition operation returns `DRAFT_LOCK_CONFLICT`; no second draft/lock or display number is created.
- A different operation from the same device cannot create a second concurrent draft for that phone.
- An exact replay of the original acquisition follows Section 2.5 idempotency before lock-conflict evaluation and returns the original invoice/lock response.
- Only the owning device with a valid session and active lock may mutate, cancel, or issue the draft.

#### Activity and release

`timeout_seconds` is copied from store configuration at acquisition and defaults to exactly `300`. `expires_at = last_activity_at + timeout_seconds`, evaluated by the Hub. At exactly the expiry boundary, the lock is expired.

| Event | Required atomic result |
| --- | --- |
| Successful issue | Allocate the next gapless `display_number`, advance the issued-only high watermark, persist the immutable issued invoice, and release with `ISSUE` in one transaction; a later sequential purchase may acquire immediately. |
| Cancel | Retire the null-numbered pre-issuance draft with `CANCEL` and release with `CANCEL`; consume no display number and leave the high watermark unchanged. |
| Hub-observed disconnect or device revocation | Release with `DISCONNECT`; late writes cannot reactivate ownership or allocate a number. |
| Inactivity timeout | Release with `TIMEOUT`, retire the abandoned null-numbered draft with `TIMEOUT`, reject old-owner writes, and leave the high watermark unchanged. |

Only a newly Hub-accepted draft item, `quantity_millis`, or customer change updates `last_activity_at` to Hub acceptance time and recomputes `expires_at = last_activity_at + timeout_seconds`. Opening/viewing a screen, polling, a passive UI heartbeat, a rejected operation, or an exact replay that applies no new change does not restart the window. Session renewal, transport activity, and Cloud replica traffic likewise have no effect; no lock-extension endpoint exists. Sequential purchases after release remain allowed.

**Vectors:** `LAN-LOCK-001` through `LAN-LOCK-006`.

### 2.7 Hub-off and WAN-off behavior

These are deliberately different states:

| State | Companion behavior | Hub/outbox behavior |
| --- | --- | --- |
| Hub reachable on LAN, WAN available | Normal operations and asynchronous replication. | Commits locally and uploads ordered entries. |
| Hub reachable on LAN, WAN unavailable | Normal Hub-mediated operations continue. | Commits locally and retains a durable outbox; sync resumes after healing. |
| Hub powered off or unreachable on LAN | Return `HUB_UNAVAILABLE`; no new authoritative draft, sale, stock event, lock, operation sequence, or display number. | No companion-local authoritative outbox is created. |
| Hub returns after companion retry | Retry the unchanged `operation_id` and payload. | Exactly one acceptance is possible through normal idempotency. |

A client may retain unsent UI form data for user convenience, but that data is not an authoritative domain record and cannot bypass the Hub when connectivity returns.

**Vectors:** `LAN-HUB-001`, `FAIL-004`.

---

## 3. Hub → Cloud Replication

### 3.1 Append-only operation log

Each accepted Hub operation produces one immutable log entry with at least:

| Field group | Fields and rule |
| --- | --- |
| Protocol/tenant | `protocol_version`, `store_id`. |
| Writer fencing | `writer_device_id`, `writer_epoch`; must match the active Cloud writer for new ingest. |
| Ordering | `operation_seq`, `previous_entry_hash`, `entry_hash`. |
| Operation identity | `operation_id`, originating `device_id`, `operation_type`, `base_version`, `request_hash`, `payload`. |
| Authoritative outcome | `outcome`, including domain events/effects needed for deterministic restore; `response_snapshot`; `hub_recorded_at`. |

The entry preserves assigned IDs, versions, nullable display numbers, operation results, and domain effects. Draft/pre-issue outcomes preserve `display_number=null` and an unchanged issued-only high watermark; only a successful-issue outcome carries the newly allocated number and advanced watermark. Quantity payloads and resulting item/inventory state use `quantity_millis` / `quantity_delta_millis` / `stock_millis` exactly as hashed. Cloud does not re-execute a command to invent different values. It appends the entry and updates its replica/projection atomically.

`operation_seq` begins at `1` for a new store and is contiguous within each committed Hub log. Sequence 1 uses 64 zero characters as `previous_entry_hash`; every later entry references the preceding entry's hash. After an authorized restore through sequence `N`, the new writer continues at `N+1` with an incremented `writer_epoch`. The hash chain remains continuous across epochs.

Authenticated domain rejections can be logged with `result_status = REJECTED` and no domain events. Transport/authentication failures that never reached a Hub or Cloud transaction are not log entries.

### 3.2 Batch ingest

A Hub uploads one store at a time, with one active upload stream per store. A batch contains `batch_id`, store/writer identity, and entries ordered by increasing `operation_seq`.

Cloud processes a batch as one atomic transaction:

1. Authenticate store/writer and check active `writer_epoch`. A stale writer receives `WRITER_FENCED` before any new entry is accepted.
2. Validate batch completeness, ascending contiguous sequences, canonical entry hashes, hash-chain linkage, operation identity, and store scope.
3. Compare the first required new sequence with `accepted_through_seq + 1`.
4. For an entry at or below the high watermark, require the stored `entry_hash` to be identical. Identical entries are duplicates; different content at the same sequence is `LOG_CONFLICT`.
5. For an existing `operation_id`, apply the same strict identity rules as the Hub. A changed payload/device/type cannot overwrite the original.
6. Append the valid new suffix and update projections/high watermark in the same transaction. Assign Cloud cursor values only to newly appended entries.
7. Acknowledge `accepted_through_seq`, `ingested_count`, `duplicate_count`, and `head_entry_hash`.

A batch retry can contain an already accepted prefix. Cloud validates that prefix by hash and ingests only the valid contiguous new suffix. A completely duplicate batch returns `DUPLICATE` with zero new effects.

### 3.3 Ordering and gaps

- Entries within a batch must be strictly ascending and contiguous. `SEQUENCE_ORDER_VIOLATION` rejects an internally reordered batch.
- If the first required new entry is ahead of `accepted_through_seq + 1`, Cloud returns `GAP_DETECTED` with `expected_seq`, `received_seq`, and unchanged high watermark.
- Cloud never applies a later entry to “catch up” while an earlier sequence is missing.
- The Hub retries from the earliest missing sequence. A gap is a replication-order problem, not permission to create replacement operations with new IDs.
- `cloud_cursor` is for downstream change feeds only; restore ordering always uses `operation_seq` and the hash chain.

**Vectors:** `CLOUD-001`, `CLOUD-003`, `CLOUD-004`.

### 3.4 Duplicate delivery and lost responses

Duplicate detection uses both `(store_id, operation_seq)` and `(store_id, operation_id)` plus content hashes:

- Exact entry duplicates are acknowledged with no duplicate log, projection, inventory, invoice, or notification effect.
- Same sequence or operation ID with different content fails closed as a conflict and enters review; it never silently overwrites.
- If Cloud commits but the response is lost, the Hub retries the same batch with unchanged entries and hashes. The second response reports duplicates and the current high watermark.
- A Hub acknowledges/removes outbox entries only after Cloud confirms they are at or below `accepted_through_seq` with matching hashes.

**Vectors:** `CLOUD-002`, `CLOUD-005`, `LAN-OP-002`.

### 3.5 Retries

The durable outbox, not an in-memory retry queue, is the source of truth.

- Network errors, incomplete responses, temporary Cloud failures, and throttling are retryable with the same entry content.
- Conservative local retry timing starts at 1 second, doubles to a 60-second ceiling, and applies jitter. These are configurable operational defaults, not a reason to discard entries.
- `GAP_DETECTED` is retried only after supplying earlier missing entries.
- `ENTRY_HASH_MISMATCH`, `LOG_CONFLICT`, and `OPERATION_ID_CONFLICT` stop automatic advancement for review; repeated identical uploads cannot make conflicting content valid.
- `WRITER_FENCED` moves the old Hub to `FENCED_READ_ONLY`; it must not keep retrying as a writer.

No retry creates a new operation ID, device ID, sequence, display number, or request hash for the same accepted operation.

### 3.6 Backpressure

When Cloud cannot accept the current load, it returns `THROTTLED` with at least:

- `retry_after_ms`.
- `max_batch_entries` and `max_batch_bytes`.
- Unchanged `accepted_through_seq`.

The Hub retains every unacknowledged entry, waits for the indicated delay, reduces subsequent batches to the advertised limits, and keeps one ordered upload stream per store. It does not parallelize around backpressure or discard old entries.

LAN operations can continue while the durable outbox has capacity. A configurable soft threshold signals degraded sync; a hard local durability limit rejects new mutations explicitly before data loss rather than overwriting the log. Threshold selection remains open, but silent log truncation is never permitted.

**Vector:** `CLOUD-006`.

### 3.7 Writer fencing and two-Hub prevention

Cloud stores one `(active_writer_device_id, active_writer_epoch)` per store.

- Ordinary activation of another Hub while one is active returns `HUB_ALREADY_ACTIVE`.
- Only the authorized restore flow can increment the epoch and replace the writer.
- Once replaced, the old writer's ingest returns `WRITER_FENCED`. On learning this, the old Hub stops accepting new mutations, releases active sessions/locks for write purposes, and preserves unsynced entries for review.
- Cloud never automatically merges entries from different writer epochs at the same sequence. Any divergent suffix is quarantined for an explicit recovery decision.

**Vectors:** `FAIL-002`, `CLOUD-008`.

---

## 4. Restore Protocol

### 4.1 Preconditions

A restore starts only when:

- A clean replacement Hub has a new `device_id` and no pre-existing authoritative store database.
- The requester has an active owner-authorized restore request for the store.
- The Cloud manifest can identify a contiguous, hash-valid log/snapshot through `accepted_through_seq = N`.
- The store's key envelope is available under the `SEC-1` MVP model.

If the Cloud log itself has a gap or invalid head hash, restore fails closed as `CLOUD_REPLICA_INVALID`; it does not activate a partial writer.

### 4.2 Phone verification plus mandatory second factor

Restore requires **both**:

1. SMS OTP verification of the owner/restore phone; and
2. One of:
   - proof of the install-time **recovery key**, or
   - signed approval from a **previously paired, currently authorized device**.

OTP alone returns `SECOND_FACTOR_REQUIRED`. No restore export, key-envelope release, writer epoch change, or new Hub session is permitted at that point. Failures produce a privileged audit event without logging OTP, recovery key, approval signature, or decrypted customer data.

A paired-device approval is bound to `restore_request_id`, `store_id`, approving `device_id`, replacement `device_id`, manifest `state_hash`, expiry, and signature. The replacement device cannot approve its own restore. Approval does not authorize a different manifest or replacement device.

**Vectors:** `RESTORE-001`, `RESTORE-003`.

### 4.3 Manifest and staged download

After both factors pass, Cloud releases an audited restore manifest containing at least:

- `store_id` and current writer identity/epoch.
- `through_operation_seq = N`.
- `head_entry_hash` for sequence `N`.
- `state_hash` for the corresponding canonical replayed state.
- Snapshot metadata when a snapshot is used, including its covered sequence and hash.
- The store key envelope under the existing `SEC-1` encryption model.

The replacement Hub downloads to a temporary staging area. Production data remains encrypted in transit and at rest; the MVP does **not** claim end-to-end encryption or that the server can never read private data.

### 4.4 Verify and replay

The replacement Hub must:

1. Verify the manifest authorization and replacement-device binding.
2. Verify every log entry hash and `previous_entry_hash` linkage from genesis or from a verified snapshot boundary.
3. Verify snapshot coverage and replay all later entries in ascending `operation_seq` order.
4. Apply authoritative outcomes/domain events without generating new IDs, versions, display numbers, operation results, quantities, or timestamps. Restore pre-issuance/retired drafts with `display_number=null`; only replayed successful-issue outcomes contain assigned numbers.
5. Preserve Operation idempotency records and the issued-only display-number high watermark: the last successfully issued number, or `0` when none exists. Never derive it from draft count, operation sequence, or cancelled/timed-out drafts.
6. Build the canonical replayed state projection in temporary storage and compute its `state_hash`.

No new local operation is accepted during staging.

### 4.5 Replayed-state consistency hash

The state projection contains:

- One `store` object.
- Store-owned collections `devices`, `memberships`, `customers`, `products`, `inventory_events`, `sales`, `invoices`, `invoice_items`, `digital_invoices`, `operations`, and `locks`, including empty collections.
- `through_operation_seq`, `writer_epoch`, and `display_number_high_watermark`. The last field is the last number allocated by a successful `ISSUE` (`0` if none), never a next-number counter or a count of persisted drafts; the next successful issue uses high watermark + 1.

Before canonical serialization:

- Entity collections are sorted by their immutable entity IDs. Operations are sorted by `operation_seq`; InventoryEvents use `inventory_event_id` for collection ordering.
- Object keys use the Section 1.4 canonical JSON profile.
- Runtime sessions, upload retry state, device last-seen runtime data not part of the stored entity snapshot, and Cloud-only cursor values are excluded.
- Global `Business`, `User`, and `CustomerProfile` contents are not duplicated into the store hash; their immutable references inside store-owned records remain covered.

`state_hash = SHA256(canonical_json(projection))` must equal the manifest value exactly. A mismatch returns `CONSISTENCY_MISMATCH`, destroys/quarantines temporary state, leaves the current writer and Cloud log unchanged, and blocks activation.

**Vectors:** `RESTORE-002`, `RESTORE-004`.

### 4.6 Atomic writer activation

Only after authorization and hash match:

1. Cloud and replacement Hub atomically record the restored high watermark `N` and head hash.
2. Cloud increments `writer_epoch`, binds it to the replacement `writer_device_id`, and fences the previous writer.
3. The replacement promotes staged state to authoritative local state and sets `next_operation_seq = N+1`.
4. New device credentials/sessions are issued. Old sessions are not restored.
5. Local operations and ordered replication may now start under the new epoch.

A crash before activation leaves the old writer unchanged and permits a clean retry. A crash after activation causes the replacement to resume from its durable restored high watermark; it must not replay into a second copy of state.

### 4.7 Disk-loss truthfulness

If the failed Hub had local operations after the Cloud high watermark, those entries are absent from the restore. The result must report `RESTORED_WITH_UNSYNCED_LOSS`, enumerate the unknown/unrecovered sequence window where knowable, and require owner acknowledgement. It must never claim a lossless restore.

If an old disk later becomes available, its divergent suffix is read-only review material. It is not automatically appended over the new writer's sequence.

**Vector:** `FAIL-001`.

---

## 5. Failure Table

Every failure has a fail-closed expected outcome and at least one concrete vector. “Recovery” never means silently discarding or rewriting accepted operations.

| Failure | Detection | Expected outcome | Recovery | Vector(s) |
| --- | --- | --- | --- | --- |
| **Disk loss** | Local Hub database/device is unavailable; Cloud manifest has high watermark `N` below former local state. | Restore only after OTP + second factor + replay/hash match. Report `RESTORED_WITH_UNSYNCED_LOSS` for entries after `N`; never claim they were recovered. | Activate a new writer epoch at `N+1`; quarantine any later recovered old suffix for review. | `FAIL-001`, `RESTORE-002`, `RESTORE-004` |
| **Hub off** | Companion LAN health check/request cannot reach the Hub. | Return `HUB_UNAVAILABLE`; no authoritative local draft, stock event, lock, operation sequence, display number, or companion outbox entry. | Retry the unchanged operation after Hub recovery; idempotency permits exactly one acceptance. | `LAN-HUB-001` |
| **Two Hubs for one store** | Normal second activation finds an active writer, or Cloud sees stale `writer_epoch`/conflicting sequence. | Reject ordinary second activation. Fence stale writer to read-only; prohibit new writes and automatic merge of divergent entries. | Only authorized restore can replace the writer; review quarantined suffixes explicitly. | `FAIL-002`, `CLOUD-008` |
| **Clock skew** | Companion timestamp differs from Hub, or Hub/Cloud timestamps disagree. | Client time never changes lock expiry or sequence. Hub time governs LAN behavior; sequence/hash governs replication. Excessive Hub/Cloud skew raises review without reordering. | Use controlled Hub time/monotonic elapsed duration; correct clock configuration without rewriting accepted timestamps or hashes. | `FAIL-003` |
| **Duplicate delivery** | Same operation ID/sequence and identical request/entry hash arrives again. | Return original result or `DUPLICATE`; zero additional domain/projection/outbox effects. Different content under the same identity is a hard conflict. | Retry safely with unchanged content; investigate conflicts rather than overwriting. | `LAN-OP-002`, `CLOUD-002`, `CLOUD-005` |
| **Network partition** | WAN is down while Hub LAN remains up, or companion-to-Hub LAN is down. | WAN partition: Hub-mediated LAN operations continue and outbox grows durably. Hub partition: affected companions cannot start work. | After healing, upload ordered entries exactly once; stale writers fence instead of merging a split brain. | `FAIL-004`, `LAN-HUB-001`, `CLOUD-008` |
| **Partial delivery** | Batch is incomplete, a frame is truncated, or a response is lost after commit. | No partial batch/projection is applied. A committed batch with a lost response remains committed and is safely retried. | Retry the complete unchanged batch; duplicates are acknowledged by hash without new effects. | `FAIL-005`, `CLOUD-005` |

---

## 6. Test Vectors

### 6.1 File and harness contract

The normative synthetic vectors are in [`docs/sync-protocol-test-vectors.json`](sync-protocol-test-vectors.json). The file contains **34** unique vectors, each with:

```text
id, scenario, input: { given, steps }, expected_output
```

For P-05 and P-07:

1. Arrange exactly `input.given` with fixed IDs and clocks.
2. Execute `input.steps` in array order; `action` selects the logical protocol operation and fault-injection fields control commit/response loss where present.
3. Compare `expected_output` as the exact logical result projection. Transport headers and uncontrolled wall-clock timing are not compared.
4. Recompute all supplied valid request/entry/state hashes using Section 1.4. The deliberately corrupted `CLOUD-007` entry must produce its documented `recomputed_entry_hash`, which differs from `supplied_entry_hash`.
5. Never replace an expected hash with the implementation's output before comparing. Never enable the synthetic proofs, nonces, signatures, keys, or phone values in production.

The JSON also declares `vector_count = 34`, `target_tasks = ["P-05", "P-07"]`, common fixtures, canonicalization, and harness rules.

### 6.2 Coverage index

| Category | Count | Vector IDs |
| --- | ---: | --- |
| QR pairing | 4 | `LAN-PAIR-001`, `LAN-PAIR-002`, `LAN-PAIR-003`, `LAN-PAIR-004` |
| Session and revocation | 2 | `LAN-SESSION-001`, `LAN-SESSION-002` |
| Operations/idempotency/OCC | 4 | `LAN-OP-001`, `LAN-OP-002`, `LAN-OP-003`, `LAN-OP-004` |
| D11 locks | 6 | `LAN-LOCK-001`, `LAN-LOCK-002`, `LAN-LOCK-003`, `LAN-LOCK-004`, `LAN-LOCK-005`, `LAN-LOCK-006` |
| Hub-off behavior | 1 | `LAN-HUB-001` |
| Cloud replication | 8 | `CLOUD-001`, `CLOUD-002`, `CLOUD-003`, `CLOUD-004`, `CLOUD-005`, `CLOUD-006`, `CLOUD-007`, `CLOUD-008` |
| Restore and consistency | 4 | `RESTORE-001`, `RESTORE-002`, `RESTORE-003`, `RESTORE-004` |
| Additional failure fixtures | 5 | `FAIL-001`, `FAIL-002`, `FAIL-003`, `FAIL-004`, `FAIL-005` |
| **Total** | **34** | All IDs are unique. |

### 6.3 Hash-bearing golden fixtures

The vector file includes real, reproducible SHA-256 values, including:

| Fixture | Purpose |
| --- | --- |
| `product_request_hash` | Product operation request identity and exact replay. |
| `entry_41_hash`, `entry_42_hash` | Ordered log-chain ingest, duplicate detection, gap/order failures, and manifest head verification; entry 42 uses scaled `quantity_delta_millis`. |
| `restore_state_hash` | Successful restore consistency comparison, including the issued-only display-number high watermark. |
| `restore_bad_state_hash` | Negative restore comparison after one product price changes. |

These values are expected outputs, not implementation hints to bypass. Tests must independently canonicalize and hash the supplied objects.

---

## 7. Relationship to D7, D11, and SEC-1

| Decision | Protocol realization | Required proof |
| --- | --- | --- |
| **D7 — Single Store Hub on LAN and offline-first operation** | Companion-only QR pairing, one Hub writer, no peer ledger, `HUB_UNAVAILABLE` when LAN Hub is absent, continued Hub work during WAN outage, durable ordered outbox, and writer fencing. | `FAIL-002`, `LAN-HUB-001`, `FAIL-004`, `CLOUD-001`, `CLOUD-008` |
| **D11 — Per-phone DRAFT lock** | Atomic normalized-phone acquisition tied to an unnumbered invoice/device; hard competing-device conflict; exact-operation replay without renewal; only newly Hub-accepted draft item/quantity/customer changes restart the configurable Hub-time inactivity window (default 300 seconds); passive screen/heartbeat activity does not; release on issue, cancel, disconnect/revoke, and timeout; sequential purchases allowed. | `LAN-LOCK-001` through `LAN-LOCK-006`, plus revocation in `LAN-SESSION-002` |
| **SEC-1 / Restore Second Factor** | OTP alone is insufficient; recovery key or previously paired-device approval is mandatory; privileged restore is audited; transport/at-rest protection follows MVP server-side encryption without an E2EE claim; writer activation waits for replay/hash verification. | `RESTORE-001`, `RESTORE-002`, `RESTORE-003`, `RESTORE-004` |

Supporting decisions remain coupled:

- **D8:** Cloud restore honestly excludes unsynced disk-only operations (`FAIL-001`).
- **D10 / ASM-08:** Strict operation identity, OCC, immutable effects, and exact replay (`LAN-OP-001`–`LAN-OP-004`).
- **ASM-22 (P-0104r):** All quantity-bearing payloads, outcomes, and derived stock projections use signed integer thousandths with the `quantity_millis` / `quantity_delta_millis` / `stock_millis` names; Rial amounts remain unscaled (`LAN-SESSION-002`, `LAN-LOCK-006`, `FAIL-003`, and the entry-42 Cloud/failure vectors).
- **D12 / ASM-10 (P-0104r):** Restore preserves the issued-only display-number high watermark, restores drafts as null-numbered, and does not renumber replayed invoices or consume numbers for retired drafts.
- **D13:** Two distinct companion devices appear in pairing, lock, OCC, and replication vectors from the outset.

---

## 8. Open Questions

The conservative behavior above is recorded under `ASM-13`–`ASM-17` and `ASM-22` in [`docs/DECISIONS.md`](DECISIONS.md). Owner-approved `P-0104r` closed former Open item 3: the default remains 300 seconds and only newly Hub-accepted draft item/quantity/customer changes restart inactivity; passive UI heartbeat does not. The following questions still require Owner/PM or operational confirmation:

| Question | Conservative posture until decided | Register |
| --- | --- | --- |
| Are 300-second QR expiry and Hub-issued session lifetimes acceptable operationally? | QR is single-use, companion-only, store/hub bound, and expires after 300 seconds. Session expiry is explicit and configurable; vector lifetimes are not product defaults. | New Open item 7 / `ASM-14` |
| What retry, batch-size, and local outbox capacity thresholds are appropriate for target hardware? | Retry with 1-second doubling to 60 seconds plus jitter; obey Cloud limits; never discard unacknowledged entries; reject new mutations before durability is exhausted. | New Open item 8 / `ASM-15` |
| What Hub/Cloud clock-skew threshold should trigger an operational incident? | Client timestamps never affect ordering/expiry. Sequence/hash remains authoritative; excessive Hub/Cloud skew is review, not a license to rewrite history. | New Open item 9 / `ASM-17` |
| How should an owner reconcile a later recovered, divergent suffix from a fenced Hub? | Keep the old writer read-only and quarantine the suffix; never auto-merge or reuse its sequence. A separate reviewed recovery procedure is required. | New Open item 10 / `ASM-16` |

No open item permits OTP-only restore, split-brain writes, silent gap filling, automatic conflict overwrite, or loss of unacknowledged log entries.
