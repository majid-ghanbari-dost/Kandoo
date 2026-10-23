# KANDOO — CLOUD API CONTRACT (P-04)

**Document status:** P-04 specification; owner-approved `P-0104r` unified revision for PM review
**Date:** `2026-10-23`
**Normative machine contract:** [`docs/openapi.yaml`](openapi.yaml) (`OpenAPI 3.1.0`, API `v1`)
**Starting state:** `2b08caa16a794311c8f50cafb40c19a30919b69f` (`stable-p-02`)
**Scope:** Documentation and YAML only. No API implementation, physical schema, production host, credentials, live SMS delivery, or legal copy is claimed.

---

## 1. Overview for P-05/P-07 implementers

This contract is the HTTP boundary for the Cloud replica/control plane and its Store Hub clients. The **Store Hub remains the only ordinary domain-write authority**. Cloud registration bootstraps the first Hub; after that, device pairing/revocation, lock release, and delivery mutations carry Hub-authored P-02 log entries and follow ordered ingest. Authorized restore is the only writer-replacement path. Cloud never becomes a second Hub and companions never mutate Cloud directly.

The API has **22 operations across six groups**, with **15 mutating operations**. All 15 require `Idempotency-Key`. Every response uses the three rate-limit headers in Section 7. All list endpoints use opaque cursor pagination: `cursor` plus `limit` (default 50, maximum 100), ascending immutable ID/sequence order, and `{items, page: {next_cursor, has_more, limit}}` (plus group-specific watermark fields). A cursor is bound to store, filters, and query; restore cursors also bind `restore_request_id` and `state_hash`. `CURSOR_INVALID` means restart without a cursor, never guess an offset.

| Group | Operations | Mutations |
| --- | ---: | ---: |
| Store Registration | 4 | 3 |
| Device Pairing | 5 | 4 |
| Operation Ingest | 2 | 1 |
| Locks | 2 | 1 |
| Restore | 5 | 3 |
| Invoice Delivery | 4 | 3 |
| **Total** | **22** | **15** |

| Downstream task | Contract surface supplied here (implementation still requires its own PM task) |
| --- | --- |
| **P-05 Store Hub** | Registration/bootstrap, device credentials, operation batches, lock release entries, restore replay/activation, and D11-compatible 300-second Lock schema. |
| **P-06 clients / administration** | Store/device reads, QR grant data, explicit device revocation, lock query projections, and share/QR link results; companion LAN authority remains with the Hub. |
| **P-07 Cloud replication** | Atomic ordered ingest, writer fencing, duplicate/gap/hash semantics, cursor log reads, manifest/export, consistency verification, and activation. |
| **P-08 delivery** | Deterministic SMS stub plus idempotent QR/share delivery records. No real provider is selected or called. |
| **P-09 customer view** | Anonymous one-invoice public-token read, uniform invalid-token 404, and a hard lawyer-approved-copy gate. |

JSON uses UTF-8. IDs/timestamps/hashes use the formats in `openapi.yaml`; P-02 hash inputs use its canonical JSON profile, not arbitrary HTTP serialization. Quantity fields in generic P-02 payloads, outcomes, and response snapshots follow `ASM-22`: signed integer thousandths named `quantity_millis`, `quantity_delta_millis`, and derived `stock_millis`; Rial monetary amounts remain unscaled integers. Every store-scoped request is authorized against that store only. `STORE_NOT_FOUND` may conceal both missing and invisible stores; shared Business ownership never widens access.

## 2. Authentication

### 2.1 Credential types

| Credential | Use | Conservative rules |
| --- | --- | --- |
| **DeviceBearer** | Default OpenAPI security scheme; opaque per-device token returned at registration, pairing, rotation, and restore activation. | Bound to `store_id`, `device_id`, role, token ID, and expiry. Hub credentials also bind writer epoch. Only the active HUB credential authorizes replicated domain mutations. Companion tokens never authorize direct Cloud domain writes. Servers store only hashed opaque tokens; this contract does not prescribe JWT. |
| **RestoreBearer** | Manifest, entry download, replay verification, and activation. | Short-lived; bound to `restore_request_id`, replacement `device_id`, store, and manifest `state_hash`. Issued only after OTP **and** SEC-1 second factor. It is not a Hub writer token until activation succeeds. |
| **OTP verification token** | Store registration or restore authorization request body. | Short-lived, single-use, bound to normalized phone, purpose, and store where applicable. It is not a device bearer and never substitutes for restore second factor. |
| **Pairing proof / QR** | Hub-mediated companion bootstrap. | Single-use, store/Hub bound, COMPANION-only, maximum 300 seconds in v1. QR consumption creates no Cloud effect without active-Hub proof and a valid Hub-authored pairing entry. |
| **Public token** | Anonymous single-invoice read only. | Unpadded base64url, at least 22 characters/128 bits. It grants no account, store, customer-history, or other-invoice access. |

OTP challenge/verify, store registration, restore authorization, and public invoice view explicitly override the default security as bootstrap/anonymous operations. Their bodies carry the required proof; no proof value is logged. Restore OTP-only is a preflight returning `403 SECOND_FACTOR_REQUIRED`: no export, key envelope, restore token, or writer change is released, and the OTP verification token is not consumed. Only successful two-factor authorization consumes it.

### 2.2 Rotation and revocation

`rotateDeviceToken` preserves device identity, operation identity, and writer epoch. The previous token is immediately invalid for ordinary calls. For at most 60 seconds it may repeat **only the same rotation request with the same idempotency key** to recover a lost response; it cannot read or mutate anything else. Suspected compromise uses `revokeDevice`, not this replay grace. Revocation is companion-only and uses a Hub-authored entry, invalidates Cloud sessions/tokens, and projects owned-lock releases as `DISCONNECT`. A HUB target returns `FORBIDDEN`; active-Hub replacement requires authorized restore.

### 2.3 Authorization invariants

- Store membership and active device pairing are both checked where applicable; a device from store A cannot read or mutate store B.
- Active Hub checks compare token device/writer epoch with Cloud's active writer. A stale writer gets `WRITER_FENCED` on writes but may use read-only endpoints.
- Registration is the sole pre-Hub bootstrap. Restore activation is the sole writer replacement. Pairing can never grant HUB role.
- OTP, recovery proofs, approval signatures, access tokens, wrapped keys, QR nonces, and customer payloads are never written to logs or error messages.

## 3. Endpoint groups

### 3.1 Store registration — 4 operations

| Method | Path | Operation ID | Authentication | Success |
| --- | --- | --- | --- | --- |
| `POST` | `/v1/otp/challenges` | `requestOtpChallenge` | Bootstrap proof / anonymous | `201` |
| `POST` | `/v1/otp/challenges/{challenge_id}/verify` | `verifyOtpChallenge` | Bootstrap proof / anonymous | `200` |
| `POST` | `/v1/stores` | `registerStore` | Bootstrap proof / anonymous | `201` |
| `GET` | `/v1/stores/{store_id}` | `getStore` | Device bearer | `200` |

Registration allocates `store_id`; clients never supply it. The request includes `agreement_acceptance`, which binds the configured lawyer-approved onboarding resource ID, version, content SHA-256, acceptance time, and digital signature. Missing approved agreement configuration returns `LEGAL_COPY_UNAVAILABLE`; invalid acceptance returns `VALIDATION_ERROR` with no effects. The initial transaction creates the Business/Store, owner User and OWNER Membership, initial HUB Device, writer epoch 1, and initial device credential. `draft_lock_timeout_seconds` defaults to exactly 300. OTP `purpose=STORE_REGISTRATION` cannot be reused for restore. Live OTP/SMS remains provider-gated; the deterministic stub is for local synthetic development only.

### 3.2 Device pairing — 5 operations

| Method | Path | Operation ID | Authentication | Success |
| --- | --- | --- | --- | --- |
| `POST` | `/v1/stores/{store_id}/pairing-grants` | `createPairingGrant` | Device bearer | `201` |
| `POST` | `/v1/pairing-grants/{pairing_id}/consume` | `consumePairingGrant` | Device bearer | `201` |
| `GET` | `/v1/stores/{store_id}/devices` | `listDevices` | Device bearer | `200` |
| `POST` | `/v1/stores/{store_id}/devices/{device_id}/revoke` | `revokeDevice` | Device bearer | `200` |
| `POST` | `/v1/stores/{store_id}/devices/{device_id}/token-rotation` | `rotateDeviceToken` | Device bearer | `200` |

The active Hub generates the raw single-use QR/nonce locally and registers only its SHA-256 fingerprints and expiry with Cloud through `createPairingGrant`; raw nonce generation and LAN pairing do not require Cloud connectivity. The companion consumes the QR with the Hub over LAN (P-02). After committing `device.pair`, the Hub calls `consumePairingGrant` with its authoritative pairing entry. Cloud validates the fingerprints and applies duplicate/hash/sequence checks, then issues a Cloud device token, not a Hub LAN session. QR expiry is evaluated against the authoritative Hub pairing outcome, not Cloud receipt time, so late replication is not rejected merely because the QR has since expired. Device listing includes revoked records for audit. Rotation changes credentials only.

### 3.3 Operation ingest — 2 operations

| Method | Path | Operation ID | Authentication | Success |
| --- | --- | --- | --- | --- |
| `POST` | `/v1/stores/{store_id}/operation-batches` | `ingestOperationBatch` | Device bearer | `200` |
| `GET` | `/v1/stores/{store_id}/operations` | `listOperationLog` | Device bearer | `200` |

`ingestOperationBatch` is one atomic transaction per batch. Writer fencing is evaluated before batch-shape acceptance, including for an empty wire batch: a stale writer receives `WRITER_FENCED` (`CLOUD-008`), while an active writer submitting zero entries receives `INCOMPLETE_BATCH` with no effects. It then checks full batch shape, contiguous ascending sequences, P-02 request/entry hashes, previous-hash linkage, store scope, and operation identity. An accepted prefix is verified by hash; only a valid new suffix is appended. The acknowledgment contains `accepted_through_seq`, `ingested_count`, `duplicate_count`, `head_entry_hash`, and next sequence/cursor information. `Idempotency-Key = batch_id`; inner `operation_id` semantics remain unchanged. Single-entry convenience mutations use the same watermark acknowledgment shape keyed by `operation_id`, not `batch_id`. `GAP_DETECTED` supplies resume data; `THROTTLED` supplies delay and batch limits. Neither permits dropping outbox entries.

### 3.4 Locks — 2 operations

| Method | Path | Operation ID | Authentication | Success |
| --- | --- | --- | --- | --- |
| `GET` | `/v1/stores/{store_id}/locks/current` | `queryCurrentLock` | Device bearer | `200` |
| `POST` | `/v1/stores/{store_id}/locks/{lock_id}/release` | `releaseLock` | Device bearer | `200` |

`queryCurrentLock` returns the lock or `null` at a Cloud replica watermark; it is not permission to operate while the LAN Hub is unavailable. `releaseLock` accepts a Hub-authored entry and never acts as a Cloud override. Only Hub-accepted draft item, quantity, or customer changes restart the 300-second inactivity window, and no API endpoint extends it. API release reasons map conservatively:

| API reason | Domain result |
| --- | --- |
| `TIMEOUT` | Hub-time inactivity expiry; domain `TIMEOUT`, with abandoned pre-issuance draft retirement. Cloud does not compute the timeout. |
| `MANUAL` | Owner/operator cancel mediated by the Hub; domain `CANCEL`, including pre-issuance retirement. |
| `HUB_OFF` | Domain `DISCONNECT` only for a Hub-recorded orderly shutdown/fencing release. Cloud never infers it from a failed heartbeat; unexpected Hub loss leaves the replica unchanged pending Hub log or authorized restore. |

### 3.5 Restore — 5 operations

| Method | Path | Operation ID | Authentication | Success |
| --- | --- | --- | --- | --- |
| `POST` | `/v1/stores/{store_id}/restore-requests` | `createRestoreRequest` | Bootstrap proof / anonymous | `201` |
| `GET` | `/v1/stores/{store_id}/restore-requests/{restore_request_id}/manifest` | `getRestoreManifest` | Restore bearer | `200` |
| `GET` | `/v1/stores/{store_id}/restore-requests/{restore_request_id}/entries` | `listRestoreEntries` | Restore bearer | `200` |
| `POST` | `/v1/stores/{store_id}/restore-requests/{restore_request_id}/verification` | `verifyRestoreReplay` | Restore bearer | `200` |
| `POST` | `/v1/stores/{store_id}/restore-requests/{restore_request_id}/activation` | `activateRestoredHub` | Restore bearer | `200` |

1. Obtain RESTORE OTP proof. Submit a client-generated `restore_request_id`, replacement device, and second factor. For paired approval, an OTP-only preflight returns `SECOND_FACTOR_REQUIRED` plus `restore_request_id` and `manifest_state_hash` in `retry_hint`, allowing the approval to bind exactly those values without consuming the OTP verification token; retry with a **new** idempotency key because the body changes. Only successful two-factor authorization consumes the token.
2. Download the manifest and full log into staging. v1 uses full-log replay from genesis; snapshot transport/optimization is not silently introduced.
3. Verify every entry and previous hash, replay authoritative outcomes without inventing IDs/versions/numbers/timestamps, preserve idempotency and display-number high watermarks, then submit the computed P-02 `state_hash`.
4. `CONSISTENCY_MISMATCH` leaves writer and Cloud log unchanged. MATCH returns a verification ID but does not activate.
5. Activation fences the old writer, increments `writer_epoch`, continues at `N+1`, issues a new Hub credential, and restores no sessions. Known unsynced sequences require owner acknowledgement. `RESTORED` means exact restoration **through the Cloud high watermark only**, never proof that disk-only operations did not exist. For 60 seconds after success, the original restore token may repeat only the identical activation request with the same idempotency key to recover a lost response; `replay_grace_expires_at` advertises that boundary. All other restore-token calls stop at activation.

### 3.6 Invoice delivery — 4 operations

| Method | Path | Operation ID | Authentication | Success |
| --- | --- | --- | --- | --- |
| `POST` | `/v1/stores/{store_id}/invoices/{invoice_id}/delivery/sms` | `sendInvoiceSmsStub` | Device bearer | `201` |
| `POST` | `/v1/stores/{store_id}/invoices/{invoice_id}/delivery/qr-link` | `createInvoiceQrLink` | Device bearer | `201` |
| `POST` | `/v1/stores/{store_id}/invoices/{invoice_id}/delivery/share-link` | `createInvoiceShareLink` | Device bearer | `201` |
| `GET` | `/v1/public/invoices/{public_token}` | `getPublicInvoice` | Bootstrap proof / anonymous | `200` |

All three delivery mutations ingest Hub-authored operation entries and are idempotent. SMS returns `STUB_RECORDED` with `external_delivery=false`; it never calls a gateway. QR/share return the existing DigitalInvoice public URL and never rotate `public_token` per channel. Delivery creation requires an issued invoice/DigitalInvoice; it does not itself render customer-facing legal copy. Public rendering is separately gated by signed lawyer-approved copy. The public endpoint exposes only one issued snapshot; every malformed, invalid, or unknown token intentionally shares `PUBLIC_INVOICE_NOT_FOUND`, and revoked/superseded snapshots remain immutable with explicit lifecycle status.

## 4. Error contract and complete error table

Every error is JSON with exactly the required top-level fields `code`, `message`, and `retry_hint`:

```json
{
  "code": "GAP_DETECTED",
  "message": "Upload the earliest missing operation sequence.",
  "retry_hint": {
    "action": "RESUME_FROM_SEQUENCE",
    "expected_seq": 42,
    "received_seq": 44,
    "accepted_through_seq": 41
  }
}
```

`retry_hint.action` is stable and machine-actionable. Optional fields carry structured recovery data (delay, expected/received sequence, watermark, batch limits, restore binding, current version, or safe details). Messages never include secrets or customer payload. The table lists **all 51 v1 codes and every operation that may return each**. The same exact sets are on each OpenAPI operation as `x-error-codes`; HTTP responses use the common envelope and rate headers.

| Code | HTTP | Default retry action | Meaning / fail-closed effect | Operations |
| --- | ---: | --- | --- | --- |
| `VALIDATION_ERROR` | 400 | `FIX_REQUEST` | A path, query, header, or JSON field is malformed or fails a conditional rule. | `requestOtpChallenge`, `verifyOtpChallenge`, `registerStore`, `getStore`, `createPairingGrant`, `consumePairingGrant`, `listDevices`, `revokeDevice`, `rotateDeviceToken`, `ingestOperationBatch`, `listOperationLog`, `queryCurrentLock`, `releaseLock`, `createRestoreRequest`, `getRestoreManifest`, `listRestoreEntries`, `verifyRestoreReplay`, `activateRestoredHub`, `sendInvoiceSmsStub`, `createInvoiceQrLink`, `createInvoiceShareLink` |
| `IDEMPOTENCY_KEY_REQUIRED` | 400 | `FIX_REQUEST` | A mutating request omitted the required Idempotency-Key header. | `requestOtpChallenge`, `verifyOtpChallenge`, `registerStore`, `createPairingGrant`, `consumePairingGrant`, `revokeDevice`, `rotateDeviceToken`, `ingestOperationBatch`, `releaseLock`, `createRestoreRequest`, `verifyRestoreReplay`, `activateRestoredHub`, `sendInvoiceSmsStub`, `createInvoiceQrLink`, `createInvoiceShareLink` |
| `REQUEST_HASH_MISMATCH` | 400 | `REVIEW` | A Hub operation request_hash does not match the P-02 canonical request profile. | `consumePairingGrant`, `revokeDevice`, `ingestOperationBatch`, `releaseLock`, `sendInvoiceSmsStub`, `createInvoiceQrLink`, `createInvoiceShareLink` |
| `INCOMPLETE_BATCH` | 400 | `RETRY_SAME` | The batch is truncated or incomplete; no entry or projection was applied. | `ingestOperationBatch` |
| `SEQUENCE_ORDER_VIOLATION` | 400 | `RESUME_FROM_SEQUENCE` | Entries inside a batch are not strictly ascending and contiguous. | `ingestOperationBatch` |
| `ENTRY_HASH_MISMATCH` | 400 | `REVIEW` | An entry_hash or previous_entry_hash fails the P-02 canonical hash-chain check. | `consumePairingGrant`, `revokeDevice`, `ingestOperationBatch`, `releaseLock`, `sendInvoiceSmsStub`, `createInvoiceQrLink`, `createInvoiceShareLink` |
| `OTP_INVALID` | 400 | `FIX_REQUEST` | The OTP code or OTP verification proof is invalid; no privileged authorization is granted. | `verifyOtpChallenge` |
| `OTP_EXPIRED` | 410 | `REAUTHENTICATE` | The OTP challenge or verification token expired; request a fresh challenge and proof. | `verifyOtpChallenge`, `registerStore`, `createRestoreRequest` |
| `VERIFICATION_TOKEN_INVALID` | 400 | `REAUTHENTICATE` | The OTP verification token is unknown, already consumed, or does not match the requester. | `registerStore`, `createRestoreRequest` |
| `OTP_PURPOSE_MISMATCH` | 400 | `REAUTHENTICATE` | An OTP verification token was issued for a different purpose, store, or registration flow. | `registerStore`, `createRestoreRequest` |
| `PAIRING_INVALID` | 400 | `FIX_REQUEST` | The pairing proof, Hub binding, device data, or pairing entry is invalid. | `consumePairingGrant` |
| `CURSOR_INVALID` | 400 | `FIX_REQUEST` | The opaque cursor is malformed, expired, or belongs to another store, query, or restore manifest. | `listDevices`, `listOperationLog`, `listRestoreEntries` |
| `UNAUTHENTICATED` | 401 | `REAUTHENTICATE` | A bearer token is absent, malformed, unknown, or outside its permitted replay exception. | `getStore`, `createPairingGrant`, `consumePairingGrant`, `listDevices`, `revokeDevice`, `rotateDeviceToken`, `ingestOperationBatch`, `listOperationLog`, `queryCurrentLock`, `releaseLock`, `getRestoreManifest`, `listRestoreEntries`, `verifyRestoreReplay`, `activateRestoredHub`, `sendInvoiceSmsStub`, `createInvoiceQrLink`, `createInvoiceShareLink` |
| `TOKEN_EXPIRED` | 401 | `ROTATE_TOKEN` | The device or restore token has expired. | `getStore`, `createPairingGrant`, `consumePairingGrant`, `listDevices`, `revokeDevice`, `rotateDeviceToken`, `ingestOperationBatch`, `listOperationLog`, `queryCurrentLock`, `releaseLock`, `getRestoreManifest`, `listRestoreEntries`, `verifyRestoreReplay`, `activateRestoredHub`, `sendInvoiceSmsStub`, `createInvoiceQrLink`, `createInvoiceShareLink` |
| `TOKEN_ROTATED` | 401 | `ROTATE_TOKEN` | The presented device token was superseded; use the newest credential. | `getStore`, `createPairingGrant`, `consumePairingGrant`, `listDevices`, `revokeDevice`, `rotateDeviceToken`, `ingestOperationBatch`, `listOperationLog`, `queryCurrentLock`, `releaseLock`, `sendInvoiceSmsStub`, `createInvoiceQrLink`, `createInvoiceShareLink` |
| `FORBIDDEN` | 403 | `NONE` | The authenticated principal lacks the required store, membership, role, or restore scope. | `getStore`, `createPairingGrant`, `consumePairingGrant`, `listDevices`, `revokeDevice`, `rotateDeviceToken`, `ingestOperationBatch`, `listOperationLog`, `queryCurrentLock`, `releaseLock`, `getRestoreManifest`, `listRestoreEntries`, `verifyRestoreReplay`, `activateRestoredHub`, `sendInvoiceSmsStub`, `createInvoiceQrLink`, `createInvoiceShareLink` |
| `DEVICE_REVOKED` | 403 | `REAUTHENTICATE` | The presented device is revoked and cannot authorize the requested action. | `getStore`, `createPairingGrant`, `consumePairingGrant`, `listDevices`, `revokeDevice`, `rotateDeviceToken`, `ingestOperationBatch`, `listOperationLog`, `queryCurrentLock`, `releaseLock`, `sendInvoiceSmsStub`, `createInvoiceQrLink`, `createInvoiceShareLink` |
| `STORE_SUSPENDED` | 403 | `NONE` | The store is not ACTIVE for this operation; no cross-store fallback is permitted. | `getStore`, `createPairingGrant`, `consumePairingGrant`, `listDevices`, `revokeDevice`, `rotateDeviceToken`, `ingestOperationBatch`, `listOperationLog`, `queryCurrentLock`, `releaseLock`, `createRestoreRequest`, `getRestoreManifest`, `listRestoreEntries`, `verifyRestoreReplay`, `activateRestoredHub`, `sendInvoiceSmsStub`, `createInvoiceQrLink`, `createInvoiceShareLink` |
| `HUB_AUTHORITY_REQUIRED` | 403 | `NONE` | The operation requires the active Hub writer; a companion or Cloud-only override is forbidden. | `createPairingGrant`, `consumePairingGrant`, `revokeDevice`, `ingestOperationBatch`, `releaseLock`, `sendInvoiceSmsStub`, `createInvoiceQrLink`, `createInvoiceShareLink` |
| `SECOND_FACTOR_REQUIRED` | 403 | `PROVIDE_SECOND_FACTOR` | OTP alone was presented; recovery-key proof or paired-device approval is still required. | `createRestoreRequest` |
| `SECOND_FACTOR_INVALID` | 403 | `PROVIDE_SECOND_FACTOR` | The recovery-key proof or paired-device approval is invalid, expired, or bound to another restore. | `createRestoreRequest` |
| `STORE_NOT_FOUND` | 404 | `NONE` | The store does not exist or is not visible to this principal; no sibling-store data is revealed. | `getStore`, `createPairingGrant`, `consumePairingGrant`, `listDevices`, `revokeDevice`, `rotateDeviceToken`, `ingestOperationBatch`, `listOperationLog`, `queryCurrentLock`, `releaseLock`, `createRestoreRequest`, `getRestoreManifest`, `listRestoreEntries`, `verifyRestoreReplay`, `activateRestoredHub`, `sendInvoiceSmsStub`, `createInvoiceQrLink`, `createInvoiceShareLink` |
| `DEVICE_NOT_FOUND` | 404 | `NONE` | The device does not exist in the authenticated store scope. | `revokeDevice`, `rotateDeviceToken` |
| `LOCK_NOT_FOUND` | 404 | `NONE` | The lock does not exist in the authenticated store scope. | `releaseLock` |
| `RESTORE_NOT_FOUND` | 404 | `NONE` | The restore request does not exist or is not bound to the presented restore token. | `getRestoreManifest`, `listRestoreEntries`, `verifyRestoreReplay`, `activateRestoredHub` |
| `INVOICE_NOT_FOUND` | 404 | `NONE` | The invoice does not exist in the authenticated store scope. | `sendInvoiceSmsStub`, `createInvoiceQrLink`, `createInvoiceShareLink` |
| `PUBLIC_INVOICE_NOT_FOUND` | 404 | `NONE` | The public token is invalid or unknown; the response is deliberately indistinguishable. | `getPublicInvoice` |
| `PAIRING_EXPIRED` | 410 | `FIX_REQUEST` | The single-use pairing grant expired; the owner must create a new grant. | `consumePairingGrant` |
| `RESTORE_REQUEST_EXPIRED` | 410 | `REAUTHENTICATE` | The restore authorization or its replay window expired; start a new authorized restore. | `getRestoreManifest`, `listRestoreEntries`, `verifyRestoreReplay`, `activateRestoredHub` |
| `IDEMPOTENCY_CONFLICT` | 409 | `REVIEW` | The idempotency scope and key were already used with a different canonical request. | `requestOtpChallenge`, `verifyOtpChallenge`, `registerStore`, `createPairingGrant`, `consumePairingGrant`, `revokeDevice`, `rotateDeviceToken`, `ingestOperationBatch`, `releaseLock`, `createRestoreRequest`, `verifyRestoreReplay`, `activateRestoredHub`, `sendInvoiceSmsStub`, `createInvoiceQrLink`, `createInvoiceShareLink` |
| `OPERATION_ID_CONFLICT` | 409 | `REVIEW` | A store-scoped operation_id was reused with different device, type, payload, or request hash. | `consumePairingGrant`, `revokeDevice`, `ingestOperationBatch`, `releaseLock`, `sendInvoiceSmsStub`, `createInvoiceQrLink`, `createInvoiceShareLink` |
| `DEVICE_STORE_CONFLICT` | 409 | `REVIEW` | The device is actively bound to another store or conflicts with the requested store/device binding. | `registerStore`, `consumePairingGrant`, `revokeDevice`, `createRestoreRequest`, `activateRestoredHub` |
| `HUB_ALREADY_ACTIVE` | 409 | `REVIEW` | The requested action would activate a second Hub without an authorized restore. | `consumePairingGrant` |
| `PAIRING_REPLAY_REJECTED` | 409 | `REVIEW` | The single-use pairing grant was already consumed by a different request. | `consumePairingGrant` |
| `WRITER_FENCED` | 409 | `STOP_WRITER` | The writer epoch is stale; the old Hub must become FENCED_READ_ONLY and stop new mutations. | `createPairingGrant`, `consumePairingGrant`, `revokeDevice`, `ingestOperationBatch`, `releaseLock`, `sendInvoiceSmsStub`, `createInvoiceQrLink`, `createInvoiceShareLink` |
| `GAP_DETECTED` | 409 | `RESUME_FROM_SEQUENCE` | The first required new entry is ahead of accepted_through_seq + 1; earlier entries are required. | `consumePairingGrant`, `revokeDevice`, `ingestOperationBatch`, `releaseLock`, `sendInvoiceSmsStub`, `createInvoiceQrLink`, `createInvoiceShareLink` |
| `LOG_CONFLICT` | 409 | `REVIEW` | An operation sequence or existing log identity has different content; Cloud never overwrites it. | `consumePairingGrant`, `revokeDevice`, `ingestOperationBatch`, `releaseLock`, `sendInvoiceSmsStub`, `createInvoiceQrLink`, `createInvoiceShareLink` |
| `LOCK_STATE_CONFLICT` | 409 | `REVIEW` | The lock is already released, expired, or inconsistent with the Hub-authored release entry. | `releaseLock` |
| `RESTORE_STATE_INVALID` | 409 | `REVIEW` | The restore request is in the wrong state for manifest download, verification, or activation. | `getRestoreManifest`, `listRestoreEntries`, `verifyRestoreReplay`, `activateRestoredHub` |
| `CONSISTENCY_MISMATCH` | 409 | `REVIEW` | The replayed sequence, head hash, or state hash differs from the authorized manifest; activation is blocked. | `verifyRestoreReplay`, `activateRestoredHub` |
| `LOSS_ACKNOWLEDGEMENT_REQUIRED` | 409 | `ACKNOWLEDGE_LOSS` | Known unsynced sequence loss was reported without the required owner acknowledgement. | `activateRestoredHub` |
| `INVOICE_NOT_ISSUED` | 409 | `NONE` | Delivery requires an issued invoice with an immutable DigitalInvoice snapshot. | `sendInvoiceSmsStub`, `createInvoiceQrLink`, `createInvoiceShareLink` |
| `PAYLOAD_TOO_LARGE` | 413 | `FIX_REQUEST` | The request exceeds the advertised body or batch byte limit; split only at valid batch boundaries. | `consumePairingGrant`, `revokeDevice`, `ingestOperationBatch`, `releaseLock`, `sendInvoiceSmsStub`, `createInvoiceQrLink`, `createInvoiceShareLink` |
| `RATE_LIMITED` | 429 | `RETRY_AFTER` | The endpoint rate bucket is exhausted; retry the unchanged request after the advertised delay. | `requestOtpChallenge`, `verifyOtpChallenge`, `registerStore`, `getStore`, `createPairingGrant`, `consumePairingGrant`, `listDevices`, `revokeDevice`, `rotateDeviceToken`, `ingestOperationBatch`, `listOperationLog`, `queryCurrentLock`, `releaseLock`, `createRestoreRequest`, `getRestoreManifest`, `listRestoreEntries`, `verifyRestoreReplay`, `activateRestoredHub`, `sendInvoiceSmsStub`, `createInvoiceQrLink`, `createInvoiceShareLink`, `getPublicInvoice` |
| `THROTTLED` | 429 | `RETRY_AFTER` | Cloud ingest backpressure is active; retain the outbox and obey the advertised batch limits. | `consumePairingGrant`, `revokeDevice`, `ingestOperationBatch`, `releaseLock`, `sendInvoiceSmsStub`, `createInvoiceQrLink`, `createInvoiceShareLink` |
| `OTP_ATTEMPTS_EXCEEDED` | 429 | `REAUTHENTICATE` | Too many OTP guesses were made; wait and request a new challenge. | `verifyOtpChallenge` |
| `SMS_PROVIDER_NOT_CONFIGURED` | 503 | `ENABLE_PROVIDER` | A live SMS flow was requested without an Owner-selected provider; the stub cannot enable production delivery. | `requestOtpChallenge`, `sendInvoiceSmsStub` |
| `CLOUD_REPLICA_INVALID` | 503 | `REVIEW` | The Cloud log, snapshot boundary, or head hash is not contiguous and valid; restore or replay fails closed. | `listOperationLog`, `createRestoreRequest`, `getRestoreManifest`, `listRestoreEntries`, `verifyRestoreReplay`, `activateRestoredHub` |
| `LEGAL_COPY_UNAVAILABLE` | 503 | `ENABLE_LEGAL_COPY` | The configured lawyer-approved onboarding agreement or public-rendering copy is absent; the associated bootstrap/rendering operation fails closed. | `registerStore`, `getPublicInvoice` |
| `SERVICE_UNAVAILABLE` | 503 | `RETRY_AFTER` | A temporary dependency or capacity failure occurred; retry the unchanged idempotent request. | `requestOtpChallenge`, `verifyOtpChallenge`, `registerStore`, `getStore`, `createPairingGrant`, `consumePairingGrant`, `listDevices`, `revokeDevice`, `rotateDeviceToken`, `ingestOperationBatch`, `listOperationLog`, `queryCurrentLock`, `releaseLock`, `createRestoreRequest`, `getRestoreManifest`, `listRestoreEntries`, `verifyRestoreReplay`, `activateRestoredHub`, `sendInvoiceSmsStub`, `createInvoiceQrLink`, `createInvoiceShareLink`, `getPublicInvoice` |
| `INTERNAL_ERROR` | 500 | `RETRY_AFTER` | An unexpected server failure occurred; no partial success should be inferred. | `requestOtpChallenge`, `verifyOtpChallenge`, `registerStore`, `getStore`, `createPairingGrant`, `consumePairingGrant`, `listDevices`, `revokeDevice`, `rotateDeviceToken`, `ingestOperationBatch`, `listOperationLog`, `queryCurrentLock`, `releaseLock`, `createRestoreRequest`, `getRestoreManifest`, `listRestoreEntries`, `verifyRestoreReplay`, `activateRestoredHub`, `sendInvoiceSmsStub`, `createInvoiceQrLink`, `createInvoiceShareLink`, `getPublicInvoice` |

Transport/auth failures do not create Hub log entries. Authenticated domain outcomes such as `VERSION_CONFLICT`, `DRAFT_LOCK_CONFLICT`, or `DRAFT_RETIRED` remain inside P-02 `outcome` / `response_snapshot`; Cloud does not rewrite them into new HTTP-side domain decisions. `HUB_UNAVAILABLE` is a companion-to-Hub LAN result, not permission for a companion to call these Cloud mutation endpoints.

## 5. Versioning

The HTTP API uses **path versioning**: every route starts with `/v1`, and `info.version` is `1.0.0`.

- Paths make the contract version visible in routing, logs, access policy, client base URLs, and parallel `/v2` migration without depending on a custom header that intermediaries may drop.
- Backward-compatible additive optional fields/endpoints may remain in v1. Removing/renaming fields, tightening existing semantics, changing error meaning, or changing canonical/hash behavior requires a new path version (and, where applicable, a new protocol version).
- `kandoo.sync.v0` / `protocol_version: "0"`, Canonical Invoice `schema_version: "0"`, and HTTP `/v1` are independent versions. None silently upgrades another.
- A future deprecated version needs explicit migration and shutdown communication before removal; no `Sunset` date is invented here.

`P-0104r` is an owner-approved pre-implementation correction to this documentation baseline, not a claim that a deployed `/v1` service has migrated. New implementations must use required-but-nullable `Invoice.display_number` (`null` before successful `ISSUE` and for retired drafts) while retaining required positive `PublicInvoiceView.display_number` for issued-only public views. They must use the `ASM-22` scaled quantity names from the outset; no legacy first-persistence number allocation or unscaled quantity spelling is a compatibility mode. Any later change to a deployed contract still follows the versioning rule above.

## 6. Idempotency

Every POST in v1 requires `Idempotency-Key` (1–128 safe characters, matching the non-empty P-01 operation-ID baseline). The middleware stores a canonical request fingerprint with principal, method, path, and key. An exact retry returns the original status/body with no additional effect; the same scoped key with different content returns `IDEMPOTENCY_CONFLICT`. Administrative/bootstrap idempotency records are retained for at least 24 hours until production retention is decided; domain Operation identity and append-only log deduplication are permanent store records, not a 24-hour cache.

| Operation kind | Key binding and replay rule |
| --- | --- |
| Batch ingest | Key equals `batch_id`. Batch replay does not replace inner operation IDs. Duplicate prefixes are hash-checked; changed entries use `LOG_CONFLICT` / `OPERATION_ID_CONFLICT`, not a new operation. |
| Pair/revoke/lock/delivery single-entry mutations | Key equals the contained entry `operation_id`; the entry remains P-02 hash-compatible. Exact replay returns the original credential/projection/delivery result without another external or domain effect. |
| OTP/registration/bootstrap | Key is a client-generated administrative operation ID. Store allocation and OTP consumption occur once. A changed second-factor body uses a new key but retains the same client `restore_request_id`. |
| Restore verification/activation | Key is stable for that exact verification/activation request. Activation increments epoch and issues the replacement credential once. Restore-token exact activation replay is allowed only for the original activation request with the same key during the 60-second replay window ending at `replay_grace_expires_at`. |
| Token rotation | Key binds path device and current token. Exact replay returns the original rotated credential during the 60-second old-token, rotation-only replay grace. Other old-token calls return `TOKEN_ROTATED`. |
| SMS/QR/share | One operation creates at most one delivery record/stub capture/link result. Channels reuse the existing public token; retries never mint extra tokens or send duplicate SMS. |

A lost response is always retried unchanged. Clients must not “repair” a retry with a new operation ID, payload, sequence, request hash, display number, or device ID. Storage compaction of administrative keys must preserve conflict-safe tombstones; permanent domain deduplication cannot be compacted away.

## 7. Rate limiting

All success and error responses include:

| Header | Meaning |
| --- | --- |
| `RateLimit-Limit` | Request budget for the matched endpoint/principal bucket. |
| `RateLimit-Remaining` | Budget remaining after the current response. |
| `RateLimit-Reset` | Delta-seconds until the current window resets, not an absolute timestamp. |

`429` responses also use `Retry-After` and `retry_hint.retry_after_ms`. Defaults below are conservative local/MVP configuration, not measured production capacity. Exact per-operation values are machine-readable in `x-rate-limit`; production tuning remains open.

| Group | Default bucket and limit | Important override / behavior |
| --- | --- | --- |
| Store registration | 60 requests/min per store + device for reads | OTP request 5/15 min per phone+IP+purpose; OTP verify 10/15 min per challenge+IP; registration 10/hour per proof+IP. OTP guessing also has attempt limits. |
| Device pairing | 30 mutations/min and 60 reads/min per store + Hub/device | Grant consumption 10/5 min per grant+Hub; token rotation 5/15 min per device. |
| Operation ingest | 120 batches/min per store + active Hub; log reads 60/min | Conservative batch ceiling 100 entries and 1 MiB. `THROTTLED` advertises `retry_after_ms`, `max_batch_entries`, `max_batch_bytes`, and unchanged watermark. |
| Locks | 120 queries/min per store+device+phone; 60 releases/min per store+Hub | Rate limiting never advances Hub inactivity time or releases a lock. |
| Restore | Authorization 5/15 min per store+phone+replacement; manifest 30/min; entries 60/min; verification 10/15 min; activation 5/15 min | Restore throttling never bypasses either factor or consistency verification. |
| Invoice delivery | SMS 30/min and QR/share 120/min per store+Hub+invoice; public read 60/min per token+IP | Public invalid-token responses use the same shape/status and do not reveal whether a token exists. |

When limited, retain the durable outbox and retry the identical request after the advertised delay. Do not parallelize around a per-store ingest limit, create replacement operations, or discard unacknowledged entries.

## 8. Relationship to P-01 and P-02

| Decision / source | How this API preserves it |
| --- | --- |
| **D7 single Hub / offline-first** | Only active Hub credentials perform replicated domain mutations. Pairing and lock/delivery conveniences still ingest Hub entries. Cloud lock reads are watermark-qualified, and no companion Cloud write path exists. Writer replacement is restore-only. |
| **D10 idempotency / OCC / immutability** | `LogEntry` carries `operation_id`, `device_id`, `operation_type`, `base_version`, `payload`, and `request_hash`; strict replay/conflict behavior and authoritative outcome snapshots are preserved. Cloud never re-executes a command to invent new values or edits issued invoices. |
| **D11 lock semantics** | `Lock` mirrors P-01 fields, positive `timeout_seconds` default 300, Hub `expires_at`, device ownership, and P-01 release reasons. MANUAL/HUB_OFF transport reasons map only as specified in Section 3.4. |
| **D8 / SEC-1 restore** | OTP plus recovery proof or paired approval gates manifest/key-envelope release. Full log verification and canonical state-hash MATCH precede epoch increment. Old sessions never return; unsynced disk loss is explicit. |
| **D12 numbering** | Restore and ingest preserve Hub `operation_seq`, assigned IDs, versions, nullable pre-issue display numbers, issued numbers, and the issued-only numbering high watermark (last successful `ISSUE`, or `0`). Cloud allocates no invoice display number; drafts and failed/cancelled/timed-out issue paths consume none. |
| **D4 delivery/token rules** | SMS is a deterministic stub, public tokens are 22–128 base64url characters (at least 128 bits at generation), and anonymous access is exactly one invoice. Full customer history is not exposed by this contract. |
| **D14 legal copy** | Store registration requires digital acceptance of the configured lawyer-approved onboarding agreement, and public rendering fails closed with `LEGAL_COPY_UNAVAILABLE` until approved copy exists. SMS-stub/QR/share-link creation is not a public-rendering gate. No developer-authored legal notice is included. |
| **ASM-08 / ASM-15 / ASM-17** | Strict operation identity, atomic ordered batches, durable retry, backpressure, and partial-delivery behavior map directly to request/response and error fields. |
| **ASM-22 quantity encoding** | Generic P-02 `payload`, `outcome`, and `response_snapshot` data use signed integer thousandths for `quantity_millis`, `quantity_delta_millis`, and derived `stock_millis`; Rial amounts remain unscaled integer Rials, and Cloud verifies the canonical bytes without re-encoding quantities. |
| **P-02 vectors** | `CLOUD-001`–`CLOUD-008` exercise batch acceptance, duplicates, gaps/order, throttling, hash corruption, and fencing; `RESTORE-001`–`RESTORE-004` exercise second factor, replay/hash activation and mismatch; `FAIL-001`, `FAIL-002`, and `FAIL-005` cover disk loss, two-Hub prevention, and partial delivery. Intentional `CLOUD-007` corruption must remain a negative fixture. |

Schema provenance is explicit through `x-domain-entity`, `x-domain-ref`, and `x-protocol-ref`. `LogEntry` is the P-02 replication envelope, not a replacement for the P-01 Operation record. `CanonicalInvoice` is only a reference shape; the frozen external Canonical Invoice v0 validator remains authoritative. Nothing here copies extraction implementation or changes its schema.

## 9. P-03 technology open questions

The frozen D2 platform stack (ASP.NET Core/.NET with PostgreSQL for Cloud) is not reopened. The following implementation choices remain for P-03/next-increment technical planning and require explicit decisions rather than silent library defaults:

| Question | Conservative contract posture until decided | Decision link / gate |
| --- | --- | --- |
| Which ASP.NET OpenAPI generator, validation middleware, and CI linter versions are authoritative? | This YAML is the source contract; Redocly validates the document. Generated types must be checked against it, not replace it. | New Open item 13; before Cloud implementation. |
| Which .NET canonical-JSON implementation reproduces all P-02 hashes? | Use the P-02 profile and all 34 vectors, including intentional `CLOUD-007`; never rely on default serializer property order. | Existing P-02 vectors; before P-05/P-07 hash code. |
| What opaque-token hash/storage, TTL, key rotation, and restore-token replay implementation is approved? | Short-lived scoped credentials, hashed token storage, 60-second rotation-only replay grace, no JWT requirement, no private-key transport. | New Open items 11 and 13; before production pairing/restore. |
| Which SEC-1 key-envelope algorithms and paired-approval signature profile are approved? | `algorithm` is deliberately not invented. Test-only signatures/keys from vectors are never production defaults; OTP alone always fails. | New Open item 13 and SEC-1; before P-07/P-12/P-16 security implementation. |
| Which OTP/SMS provider and sender templates are approved? | Deterministic stub only, no external calls or credentials; production auth/delivery is gated. | Existing Open item 1; before real P-08 delivery or live OTP. |
| What cursor signing/encoding, idempotency tombstone retention, and distributed rate-limit store should be used? | Opaque store/filter-bound cursors, at least 24-hour administrative idempotency with conflict-safe tombstones, permanent domain dedup, conservative documented limits. | New Open item 12; before multi-replica/production deployment. |
| What signed lawyer-approved onboarding and customer-page resources are configured? | No legal text in this contract; registration requires verifiable acceptance of the configured onboarding resource, and public rendering remains gated. Delivery-link/stub creation does not itself render legal copy. | Existing Open item 4; hard onboarding/P-09 release gate. |
| When should snapshot restore transport be introduced? | v1 downloads the full log from genesis; no partial or unverified snapshot activation. | Future backward-compatible optimization only after explicit review. |

Open questions never authorize weaker authentication, Cloud lock overrides, OTP-only restore, lossy retry, live stub SMS, or unapproved legal claims.

### Validation record

From the repository root on `2026-10-23`:

```sh
npx --yes @redocly/cli lint docs/openapi.yaml
```

Result: **exit code 0; zero errors and zero warnings** (`@redocly/cli 2.69.0`, `Node v22.22.3`, npm `10.9.8`). A separate structural/P-02 compatibility check validated all 34 vectors, 11 wire-shaped batches and 18 contained entries, 34 request-hash occurrences, 21 entry-hash occurrences, and two independently recomputed replayed-state hashes; `CLOUD-006.entry_count` is harness metadata, and `CLOUD-007` remains an intentional semantic hash-corruption negative rather than a schema failure. The narrative and YAML error matrices also match exactly at 51 codes, with 22 operations and 15 POST mutations preserved.
