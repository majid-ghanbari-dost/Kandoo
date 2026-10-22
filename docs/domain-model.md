# KANDOO — DOMAIN MODEL SPECIFICATION

**Document status:** P-01 specification for PM review

**Date:** `2026-10-22`

**Repository:** `majid-ghanbari-dost/Kandoo` (Parent Platform Repository)

**Baseline:** Board v4 + Amendments v4.1 and v4.2; `main` at `6409de18074f3ca71673d017ef671b51cc70c05c`
**Scope:** Logical domain model and executable-test scenarios only. This document does not implement C#, a database schema, API routes, or the Digital Invoice extraction pipeline.

---

## 1. Overview

### 1.1 Purpose and authority

This specification defines the 15 platform entities, their relationships and constraints, and the testable meaning of `D9`–`D13`, including store isolation. It refines — but does not reopen — the frozen decisions in [`docs/DECISIONS.md`](DECISIONS.md).

The Store Hub is the single authority for store mutations, locks, idempotency, ordering, and invoice display numbers. The Cloud API replicates store state asynchronously; it is not a second local authority. Mode `B` integration crosses the repository boundary only as Canonical Invoice v0 JSON through the defined CLI seam. No extraction implementation is part of this model.

All requirements below are **specified behavior**, not claims that behavior already exists. Each concurrency, restore, isolation, or security requirement has a concrete test in Section 7 that must be implemented and shown to fail before the corresponding implementation is accepted.

### 1.2 Modeling conventions

- Field types are logical types, not SQL or C# types. `UUID` means an opaque 128-bit identifier; `Instant` means a UTC timestamp; `Rials` means an integer amount with no fractional unit; `JSON` means a structured payload governed by its stated contract.
- `Required = Derived` means the value is mandatory but is computed by the platform rather than supplied as independent business data.
- Every store-owned record carries `store_id`, directly or through its owning aggregate. Cross-store references are invalid even when both records belong to the same `Business`.
- Except for global `User` and `CustomerProfile`, identifiers used by store APIs are always evaluated inside an explicit store scope. A globally unique identifier never grants access by itself.
- Phone values are normalized to E.164 syntax before customer matching or lock acquisition. All phone values in this document are synthetic test fixtures.
- Product `normalized_name` is derived by Unicode NFKC normalization, mapping Arabic `ي` → Persian `ی` and Arabic `ك` → Persian `ک`, applying Latin case-folding, converting zero-width non-joiner (ZWNJ) to a space, trimming, and collapsing whitespace to one space. Product `similarity_key` additionally removes all Unicode whitespace and Unicode dash-punctuation (category `Pd`) characters.
- The canonical invoice lifecycle values remain `DRAFT`, `EXTRACTED`, `VALIDATED`, `ISSUED`, `REVOKED`, and `SUPERSEDED`. Pre-issuance cancellation/timeout is platform retirement metadata, not a new Canonical Invoice status.
- The words **must**, **must not**, and **only** denote mandatory, testable behavior. Error names in tests are logical error codes; transport status mapping is deferred.

### 1.3 Common test fixtures

Section 7 uses these synthetic fixtures unless a test overrides them:

| Fixture | Meaning |
| --- | --- |
| `B1` | One business. |
| `S1`, `S2` | Two stores, both initially under `B1`; each has its own Hub and numbering sequence. |
| `H1`, `H2` | The active Hub devices for `S1` and `S2`, respectively. |
| `D1`, `D2` | Active companion devices paired to `S1`. |
| `D3` | Active companion device paired to `S2`. |
| `U-A` | User with an active `OWNER` membership in `S1` only. |
| `U-BOTH` | User with active `OWNER` memberships in both `S1` and `S2`. |
| `P`, `P2` | Synthetic normalized phones `+989120000001` and `+989120000002`. |
| `T0` | A fixed, test-controlled UTC instant. |
| `op-1`, `op-2`, … | Client-generated operation identifiers; each distinct command uses a new identifier unless testing replay. |

### 1.4 Test identifier convention and count

- `TC-E01`–`TC-E15`: entity construction and relationship scenarios.
- `TC-D9-*`, `TC-D10-*`, `TC-D11-*`, `TC-D12-*`: board-rule scenarios.
- `TC-SI-*`: store-isolation scenarios.
- `TC-MD-*`: multi-device (`D13`) scenarios.

There are **67 concrete test cases**: 15 entity cases and 52 numbered-rule cases. No case depends on real customer data, an external service, or a secret.

---

## 2. Entities

### 2.1 Business

**Description:** A commercial owner of one or more stores. `Business` groups stores administratively; it does not collapse their security or data boundaries.

**Key fields**

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `business_id` | UUID | Yes | Immutable global business identifier. |
| `name` | String | Yes | Non-blank administrative display name. |
| `status` | `ACTIVE \| SUSPENDED \| ARCHIVED` | Yes | Administrative lifecycle state. |
| `created_at` | Instant | Yes | Creation time recorded by the platform. |

**Relationships**

- One `Business` owns zero or more `Store` records.
- Access to a business does not replace store-level `Membership` authorization.

**Constraints**

- `business_id` is globally unique and never reused.
- A store references exactly one existing business.
- Business-level administration must not produce cross-store reads without the required store memberships.

**Tests:** `TC-E01`, `TC-SI-R11`.

### 2.2 Store

**Description:** The tenant, operating-mode, inventory, sales, numbering, and lock boundary. One Store Hub serves one store.

**Key fields**

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `store_id` | UUID | Yes | Immutable global store identifier and mandatory tenant scope. |
| `business_id` | UUID | Yes | Owning business. |
| `name` | String | Yes | Non-blank store display name. |
| `operating_mode` | `A \| B` | Yes | Native store software (`A`) or sidecar capture (`B`). |
| `status` | `ACTIVE \| SUSPENDED \| ARCHIVED` | Yes | Store operational state. |
| `draft_lock_timeout_seconds` | Integer | Yes | Positive inactivity timeout; default `300` seconds. |
| `created_at` | Instant | Yes | Creation time recorded by the platform. |

**Relationships**

- Belongs to one `Business`.
- Owns `Device`, `Membership`, `Customer`, `Product`, `InventoryEvent`, `Sale`, `Invoice`, `Operation`, and `Lock` records.
- Has exactly one active Hub-role `Device` and zero or more active companion devices.

**Constraints**

- `store_id` is globally unique and immutable.
- The default lock timeout is 300 seconds; a configured value must be positive.
- Exactly one active Hub-role device is permitted per store.
- Store data is never implicitly shared with another store in the same business.

**Tests:** `TC-E02`, `TC-D11-R8`, `TC-MD-R2`, `TC-SI-R11`.

### 2.3 User

**Description:** A global authenticated identity that can hold memberships in multiple stores and businesses. A user is not itself store-owned data.

**Key fields**

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `user_id` | UUID | Yes | Immutable global user identifier. |
| `display_name` | String | Yes | Non-blank display name. |
| `status` | `ACTIVE \| SUSPENDED \| DISABLED` | Yes | Account state; only active users can acquire new authorizations. |
| `created_at` | Instant | Yes | Account creation time. |

**Relationships**

- Has zero or more store-scoped `Membership` records.
- May be linked to at most one verified `CustomerProfile` for customer self-service.

**Constraints**

- `user_id` is globally unique and never reused.
- `User` has no `store_id`; all store authorization comes from active memberships plus device pairing for device-originated mutations.
- Suspending a user does not suspend other users or alter store ownership.

**Tests:** `TC-E03`, `TC-SI-R1`, `TC-SI-R2`, `TC-SI-R5`.

### 2.4 Device

**Description:** A Store Hub or companion installation paired to exactly one store. Device identity is mandatory on every mutating operation.

**Key fields**

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `device_id` | UUID | Yes | Immutable global device identifier. |
| `store_id` | UUID | Yes | Store to which this installation is paired. |
| `device_role` | `HUB \| COMPANION` | Yes | Hub authority or LAN companion. |
| `label` | String | Yes | Operator-recognizable device name. |
| `platform` | `WINDOWS \| ANDROID` | Yes | Client platform. |
| `pairing_status` | `ACTIVE \| REVOKED` | Yes | Whether the pairing may authorize operations. |
| `paired_at` | Instant | Yes | Pairing time. |
| `last_seen_at` | Instant | No | Last Hub-observed contact time. |

**Relationships**

- Belongs to one `Store`.
- Originates zero or more `Operation` records.
- Owns zero or more active or historical `Lock` records.

**Constraints**

- A device cannot be actively paired to two stores.
- A revoked device cannot mutate or acquire a lock.
- Exactly one active `HUB` device exists per store; companions never become independent write authorities.
- `device_id` attribution does not replace user/membership authorization.

**Tests:** `TC-E04`, `TC-D10-R7`, `TC-MD-R1`, `TC-MD-R2`.

### 2.5 Membership

**Description:** A user's role assignment within one store. Roles are `OWNER`, `OPERATOR`, and `CUSTOMER`.

**Key fields**

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `membership_id` | UUID | Yes | Immutable membership identifier. |
| `user_id` | UUID | Yes | Global user receiving the role. |
| `store_id` | UUID | Yes | Store in which the role applies. |
| `role` | `OWNER \| OPERATOR \| CUSTOMER` | Yes | Store-scoped role. |
| `customer_id` | UUID | Conditional | Required for `CUSTOMER`; identifies that user's store-level customer record. Null for other roles. |
| `status` | `ACTIVE \| SUSPENDED \| REVOKED` | Yes | Authorization state. |
| `created_at` | Instant | Yes | Membership creation time. |

**Relationships**

- Belongs to one `User` and one `Store`.
- A customer-role membership references one `Customer` in the same store.

**Constraints**

- At most one active membership exists for `(user_id, store_id)`.
- `customer_id` is required if and only if `role = CUSTOMER`, and its store must equal `store_id`.
- Membership in one store grants zero permissions in another store, including a store under the same business.

**Tests:** `TC-E05`, `TC-SI-R1`, `TC-SI-R2`, `TC-SI-R11`.

### 2.6 Customer

**Description:** A store-level customer identity and contact record. It is the only customer record directly visible to store operators.

**Key fields**

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `customer_id` | UUID | Yes | Immutable customer identifier. |
| `store_id` | UUID | Yes | Owning store and tenant boundary. |
| `customer_profile_id` | UUID | Conditional | Global profile link; required for `VERIFIED`, null while only `CLAIMED`. |
| `phone` | String | Yes | Normalized customer phone used for delivery and `D11` locking. |
| `phone_status` | `CLAIMED \| VERIFIED` | Yes | Whether phone ownership has been proved by OTP. |
| `display_name` | String | No | Operator-supplied or customer-supplied name; never inferred from a printed invoice. |
| `version` | Integer | Yes | Aggregate version for controlled customer edits. |
| `created_at` | Instant | Yes | Creation time. |

**Relationships**

- Belongs to one `Store`.
- Optionally links to one global `CustomerProfile` after verification.
- May be referenced by `Sale` and `Invoice` records in the same store.
- May have one customer-role `Membership` for self-service.

**Constraints**

- `(store_id, phone)` is unique; different textual phone forms cannot create separate store customers.
- `VERIFIED` requires a profile whose `verified_phone` exactly equals `phone`; `CLAIMED` has no verified-profile link.
- Linking or changing a verified phone requires OTP proof and must not merge store history silently.
- A store operator cannot traverse this record to another store's customer or invoice data.

**Tests:** `TC-E06`, `TC-D11-R1`, `TC-SI-R4`, `TC-SI-R5`.

### 2.7 CustomerProfile

**Description:** A global customer identity keyed by a **verified** phone, enabling customer-initiated cross-purchase history after OTP without exposing one store's data to another store.

**Key fields**

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `customer_profile_id` | UUID | Yes | Immutable global profile identifier. |
| `verified_phone` | String | Yes | Normalized phone whose ownership was verified by OTP. |
| `user_id` | UUID | No | Registered global user used for customer self-service; unique when present. |
| `display_name` | String | No | Global customer display name. |
| `verified_at` | Instant | Yes | Time at which phone ownership was verified. |

**Relationships**

- May link to many store-level `Customer` records across stores.
- May link to one global `User` for authenticated customer history.

**Constraints**

- `verified_phone` is globally unique; a second verification gets/links the existing profile rather than creating a duplicate.
- A profile cannot be created merely from OCR, an operator claim, or an unverified printed phone.
- The profile is not a store-operator search index. Cross-store history is available only to the verified customer self, not to a store acting on the profile.

**Tests:** `TC-E07`, `TC-SI-R4`, `TC-SI-R5`.

### 2.8 Product

**Description:** A store-owned catalog item with normalized identity, explicit versioning, and operator-assisted duplicate detection.

**Key fields**

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `product_id` | String | Yes | Immutable opaque identifier, unique within the store. |
| `store_id` | UUID | Yes | Owning store. |
| `name` | String | Yes | Operator-visible product name. |
| `normalized_name` | String | Derived | Deterministic normalized name defined in Section 1.2. |
| `similarity_key` | String | Derived | Conservative near-duplicate comparison key defined in Section 1.2. |
| `unit` | String | Yes | Sale unit such as `item`, `kg`, or `pack`. |
| `unit_price_rials` | Rials | Yes | Current non-negative integer unit price. |
| `specification` | String | No | Operator-maintained product specification. |
| `is_active` | Boolean | Yes | Whether the item is active in the catalog. |
| `version` | Integer | Yes | Current aggregate version, starting at `1`; required for OCC edits. |
| `created_at` / `updated_at` | Instant | Yes | Creation and last successful edit times. |

**Relationships**

- Belongs to one `Store`.
- Has zero or more `InventoryEvent` records.
- May be referenced by `InvoiceItem`; Mode `B` extracted lines can remain unlinked until explicit review.

**Constraints**

- `(store_id, product_id)` is unique and identifiers are never reused within a store.
- At most one active product may have a given `(store_id, normalized_name)`.
- Exact duplicates fail closed; near duplicates return a warning and require explicit acknowledgement, never an automatic merge.
- Successful price/specification edits increment `version`; stale `base_version` edits fail without changing the product.
- Product matching and catalog results never include another store's products.

**Tests:** `TC-E08`, `TC-D9-R1`–`TC-D9-R5`, `TC-D10-R4`, `TC-MD-R7`.

### 2.9 InventoryEvent

**Description:** One immutable, signed stock movement in a store's append-only inventory ledger. Current stock is derived from events, not maintained as an authoritative mutable counter.

**Key fields**

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `inventory_event_id` | UUID | Yes | Immutable event identifier. |
| `store_id` | UUID | Yes | Owning store. |
| `product_id` | String | Yes | Product in the same store. |
| `quantity_delta` | Integer | Yes | Non-zero signed quantity change. |
| `event_type` | `RECEIPT \| SALE \| RETURN \| ADJUSTMENT \| CORRECTION` | Yes | Business meaning of the movement. |
| `source_invoice_id` | UUID | No | Native invoice causing the movement, where applicable. |
| `reverses_event_id` | UUID | Conditional | Required for `CORRECTION`; identifies the same-store event being compensated. |
| `reason` | String | Conditional | Required for manual `ADJUSTMENT` and `CORRECTION`. |
| `operation_id` | String | Yes | Mutation idempotency key. |
| `device_id` | UUID | Yes | Originating device. |
| `occurred_at` / `recorded_at` | Instant | Yes | Business occurrence and Hub persistence times. |

**Relationships**

- Belongs to one `Product` and one `Store`.
- May reference a source `Invoice` and a corrected `InventoryEvent`, all in the same store.
- Is produced as part of exactly one idempotent `Operation`.

**Constraints**

- Events are append-only: update and delete are forbidden, including by privileged store operators.
- Corrections append a compensating event; they never rewrite the original.
- Stock for a product is the sum of its `quantity_delta` values in that store. Negative stock is allowed and does not by itself block a sale.
- Mode `B` capture creates no inventory event without a separate explicit native command.

**Tests:** `TC-E09`, `TC-D10-R1`–`TC-D10-R3`, `TC-D10-R10`, `TC-MD-R8`.

### 2.10 Sale

**Description:** A native Mode `A` sales aggregate. Mode `B` invoices do not implicitly acquire a native `Sale`.

**Key fields**

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `sale_id` | UUID | Yes | Immutable sale identifier. |
| `store_id` | UUID | Yes | Owning store. |
| `customer_id` | UUID | No | Same-store customer, if identified. |
| `status` | `OPEN \| COMPLETED \| CANCELLED` | Yes | Native sale lifecycle. |
| `total_rials` | Rials | Yes | Non-negative native sale total. |
| `created_operation_id` | String | Yes | Operation that created the sale. |
| `created_by_device_id` | UUID | Yes | Device that created the sale. |
| `version` | Integer | Yes | Native sale aggregate version. |
| `created_at` / `completed_at` | Instant | Yes / No | Creation and completion times. |

**Relationships**

- Belongs to one `Store` and optionally one same-store `Customer`.
- Has zero or more `Invoice` records over time when replacements occur, with exactly one head of the current replacement lineage at any time.
- Native invoice issuance can generate inventory events through explicit Mode `A` domain behavior.

**Constraints**

- A native sale and all its customer, invoice, and inventory references must belong to the same store.
- `completed_at` is required for `COMPLETED` and null otherwise.
- A Mode `B` capture, extraction, review, or issuance alone must not create a `Sale`.

**Tests:** `TC-E10`, `TC-D10-R10`, `TC-SI-R3`.

### 2.11 Invoice

**Description:** The internal operational invoice aggregate for either operating mode. It carries platform identity, Hub numbering, lifecycle, and optional native-sale linkage.

**Key fields**

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `invoice_id` | UUID | Yes | Immutable globally unique internal identifier. |
| `store_id` | UUID | Yes | Owning store and numbering scope. |
| `sale_id` | UUID | Conditional | Required for native Mode `A`; null for Mode `B` unless an explicit native workflow says otherwise. |
| `customer_id` | UUID | No | Same-store customer, if identified. |
| `display_number` | Integer | Derived | Positive sequential human-readable number allocated by the Store Hub. |
| `status` | Canonical lifecycle | Yes | `DRAFT \| EXTRACTED \| VALIDATED \| ISSUED \| REVOKED \| SUPERSEDED`. |
| `retired_at` | Instant | No | Pre-issuance cancellation/timeout time; a retired draft can never be issued. |
| `retirement_reason` | `CANCEL \| TIMEOUT` | Conditional | Required when `retired_at` is set. |
| `original_invoice_id` | UUID | No | Previous invoice for an explicit superseding replacement. |
| `created_operation_id` | String | Yes | Idempotent creation operation. |
| `created_by_device_id` | UUID | Yes | Originating device. |
| `created_at` / `issued_at` | Instant | Yes / Conditional | Creation time; issue time required once issued. |

**Relationships**

- Belongs to one `Store`; optionally belongs to one `Sale` and one `Customer`, all store-consistent.
- Contains one or more `InvoiceItem` records before issuance.
- Has zero or one `DigitalInvoice` issued snapshot.
- Has zero or more historical `Lock` records and at most one active lock.
- May reference an original invoice when superseding it.

**Constraints**

- `invoice_id` is globally unique; `(store_id, display_number)` is unique and numbers are never reused.
- External printed POS numbers remain untrusted Canonical header data and never populate `invoice_id` or `display_number`.
- Valid lifecycle transitions are those allowed by Canonical Invoice v0. Retirement is only pre-issuance and does not add a canonical status.
- After `ISSUED`, invoice content and items are immutable. Only `REVOKED` or `SUPERSEDED` plus a new replacement invoice are permitted correction paths.
- A pre-issuance mutation requires the active owning per-phone lock whenever a customer phone is present.

**Tests:** `TC-E11`, `TC-D10-R5`, `TC-D10-R6`, all `TC-D11-*`, all `TC-D12-*`.

### 2.12 InvoiceItem

**Description:** One line of an invoice. Native lines normally reference a product; extracted Mode `B` lines can remain explicitly unlinked pending review.

**Key fields**

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `invoice_item_id` | UUID | Yes | Immutable line identifier. |
| `invoice_id` | UUID | Yes | Owning invoice. |
| `store_id` | UUID | Yes | Tenant scope; must equal the invoice store. |
| `product_id` | String | Conditional | Same-store product; required for native Mode `A` catalog lines. |
| `line_number` | Integer | Yes | Positive stable line position, unique within the invoice. |
| `description` | String | Yes | Native or extracted item description. |
| `quantity` | Decimal | Yes | Positive quantity. |
| `unit_price_rials` | Rials | Yes | Non-negative integer unit price. |
| `line_total_rials` | Rials | Yes | Non-negative integer line total. |
| `provenance` | `NATIVE \| OPERATOR \| EXTRACTED` | Yes | Origin of the line data. |

**Relationships**

- Belongs to exactly one `Invoice`.
- Optionally references one `Product` in the same store.

**Constraints**

- `(invoice_id, line_number)` is unique.
- Invoice, item, and optional product must all have the same `store_id`.
- Linking an extracted line to a product is explicit and never silently creates a product, sale, or stock event.
- Items cannot be inserted, changed, reordered, or deleted after the invoice is issued.

**Tests:** `TC-E12`, `TC-D10-R5`, `TC-D10-R10`, `TC-SI-R3`.

### 2.13 DigitalInvoice

**Description:** The immutable customer-facing issued snapshot and access metadata for an invoice. It is created only after platform issuance and is separate from both the operational `Invoice` and the extraction module.

**Key fields**

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `digital_invoice_id` | UUID | Yes | Immutable digital-invoice identifier. |
| `invoice_id` | UUID | Yes | Invoice from which this snapshot was issued; unique. |
| `store_id` | UUID | Yes | Tenant scope copied from and validated against the invoice. |
| `public_token` | String | Yes | Cryptographically random, unpadded base64url token with at least 128 bits of entropy (at least 22 characters). |
| `issued_payload` | JSON | Yes | Validated Canonical Invoice v0 issued snapshot. |
| `issued_content_sha256` | String | Yes | Lowercase 64-hex digest of the canonical issued content. |
| `issued_at` | Instant | Yes | Must equal the source invoice issue time. |

**Relationships**

- Belongs one-to-one to an `Invoice` that has reached `ISSUED`.
- Resolves anonymously to exactly one invoice through `public_token`; full history still requires verified customer self-service.

**Constraints**

- No `DigitalInvoice` may exist for a never-issued, cancelled, or timed-out draft.
- `issued_payload` and `issued_content_sha256` are immutable, including after revocation or supersession.
- `public_token` is unique and unguessable; knowledge of a token grants single-invoice view only, not store authorization or phone-history access.
- A replacement invoice receives its own `DigitalInvoice`; the old snapshot is never overwritten.

**Tests:** `TC-E13`, `TC-D10-R5`, `TC-D10-R6`, `TC-SI-R9`.

### 2.14 Operation

**Description:** The durable idempotency and attribution record for every mutation accepted by a Store Hub.

**Key fields**

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `operation_id` | String | Yes | Client-generated idempotency key, unique within a store. |
| `store_id` | UUID | Yes | Tenant scope of the mutation. |
| `device_id` | UUID | Yes | Active device paired to the same store. |
| `operation_type` | String | Yes | Logical command type. |
| `request_hash` | String | Yes | Lowercase 64-hex SHA-256 hash of the canonical mutation request. |
| `result_status` | `SUCCEEDED \| REJECTED \| WARNING` | Yes | Stable outcome returned to exact replays. |
| `result_reference` | String | No | Identifier of the primary created/affected record, if any. |
| `response_snapshot` | JSON | Yes | Original logical response returned on an exact replay. |
| `created_at` / `completed_at` | Instant | Yes | Acceptance and completion times. |

**Relationships**

- Belongs to one `Store` and originates from one `Device`.
- Can atomically own effects on products, inventory, sales, invoices, and locks.

**Constraints**

- `(store_id, operation_id)` is unique; the operation record and all its effects commit atomically.
- An exact replay has the same store, operation ID, device ID, operation type, and request hash, and returns the original response with zero new effects.
- Reusing a store-scoped operation ID with a different payload, operation type, or device is a hard conflict, never a new command.
- Missing `operation_id` or `device_id` fails before domain effects occur.

**Tests:** `TC-E14`, `TC-D10-R7`–`TC-D10-R9`, `TC-D11-R3`, `TC-D12-R6`, `TC-SI-R7`.

### 2.15 Lock

**Description:** The Hub-owned lease binding one active phone-associated `DRAFT` invoice to one device within one store.

**Key fields**

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `lock_id` | UUID | Yes | Immutable lock identifier. |
| `store_id` | UUID | Yes | Tenant scope. |
| `customer_phone` | String | Yes | Normalized phone; uniqueness scope together with store. |
| `invoice_id` | UUID | Yes | The single active draft protected by this lock. |
| `device_id` | UUID | Yes | Active device currently allowed to mutate the draft. |
| `acquired_at` | Instant | Yes | Hub acquisition time. |
| `last_activity_at` | Instant | Yes | Last Hub-accepted activity on the draft. |
| `timeout_seconds` | Integer | Yes | Positive configured timeout, copied from store configuration at acquisition; default `300`. |
| `expires_at` | Instant | Derived | `last_activity_at + timeout_seconds`. |
| `state` | `ACTIVE \| RELEASED` | Yes | Lease state. |
| `release_reason` | `ISSUE \| CANCEL \| DISCONNECT \| TIMEOUT` | Conditional | Required once released. |
| `released_at` | Instant | Conditional | Required once released. |

**Relationships**

- Belongs to one `Store`, one `Invoice`, and one `Device`, all store-consistent.
- Historical released locks are retained; at most one lock is active for a store/phone pair.

**Constraints**

- At most one `ACTIVE` lock exists for `(store_id, customer_phone)`, and it references exactly one non-retired `DRAFT`.
- Only the owning device with an active, unexpired lock can mutate, cancel, or issue that phone-associated draft.
- Issue, cancel, explicit disconnect, and timeout release the lock. Timeout/cancel also retires the affected pre-issuance draft as appropriate.
- A lock is evaluated against Hub time; an expired owner has no residual write privilege.
- The same phone in another store is an independent lock namespace.

**Tests:** `TC-E15`, all `TC-D11-*`, `TC-SI-R10`, `TC-MD-R5`, `TC-MD-R6`.

---

## 3. Entity-Relationship Diagram (ASCII)

`1` means exactly one, `*` means zero or more, and `0..1` means optional. All relationships crossing a store-owned record are constrained to the same `store_id` unless explicitly marked global.

```text
                                      GLOBAL IDENTITY
                           ┌──────────────────────────────────────┐
                           │                                      │
                           ▼                                      ▼
                       ┌──────────┐
                       │ Business │
                       └────┬─────┘
                            │ 1
                            ▼ *
                       ┌───────┐ 1       * ┌────────────┐ *     1 ┌──────┐
                       │ Store │───────────│ Membership │────────│ User │
                       └───┬───┘           └────────────┘        └──┬───┘
                           │ 1                                      │ 1
             ┌─────────────┼───────────────────┐                    │ optional
             │             │                   │                    ▼
             │ *           │ *                 │ *           ┌─────────────────┐
       ┌─────▼─────┐ ┌─────▼─────┐       ┌─────▼─────┐       │ CustomerProfile │  GLOBAL,
       │  Device   │ │   Sale    │       │  Product  │       │ verified phone  │  no store_id
       └─────┬─────┘ └─────┬─────┘       └─────┬─────┘       └────────┬────────┘
             │ 1           │ 1                 │ 1                    │ 1
             │             │                   │                      │ linked after OTP
             │ *           │ *                 │ *                    ▼ *
       ┌─────▼─────┐ ┌─────▼─────┐       ┌─────────────────┐     ┌──────────┐
       │ Operation │ │  Invoice  │       │ InventoryEvent  │     │ Customer │
       └───────────┘ └────┬───┬──┘       └─────────────────┘     └────┬─────┘
                          │ 1 │ 1                                     │ *
                          │   │                                       └──── same Store ────┐
                ┌─────────┘   └──────────┐                                                 │
                │ *                      │ 0..1                                            │
       ┌────────▼────────┐       ┌───────▼──────────┐                                      │
       │   InvoiceItem   │       │  DigitalInvoice  │                                      │
       └─────────────────┘       └──────────────────┘                                      │
                │ * optional product link                                                  │
                └──────────────────────► Product                                           │
                                                                                            │
                       Store 1 ─── * Customer ───────────────────────────────────────────────┘
                       Store 1 ─── * Lock * ─── 1 Invoice
                       Lock  * ─── 1 Device
                       Invoice 1 ─── * Lock history (at most one ACTIVE)
```

Additional cardinalities and boundaries:

- `Business 1 ─── * Store`; every `Store` has exactly one `Business`.
- `Store 1 ─── * {Device, Membership, Customer, Product, InventoryEvent, Sale, Invoice, Operation, Lock}`.
- `User 1 ─── * Membership`; `User 1 ─── 0..1 CustomerProfile` when registered for customer self-service.
- `CustomerProfile 1 ─── * Customer` only for verified links; a `CLAIMED` Customer temporarily has no profile.
- `Sale 1 ─── * Invoice` permits replacement history; `Invoice 1 ─── * InvoiceItem`; `Invoice 1 ─── 0..1 DigitalInvoice`.
- `Product 1 ─── * InventoryEvent`; `Device 1 ─── * Operation`; `Invoice 1 ─── * Lock` over lock history.
- `Operation` is written with every mutation even though drawing an edge to every affected aggregate would obscure the diagram.

---

## 4. Rules D9–D12 as Testable Statements

Every row names its concrete Section 7 scenario and the required expected result.

### 4.1 D9 — Product uniqueness and near-duplicate warning

| Rule ID | Testable statement | Test case | Expected result |
| --- | --- | --- | --- |
| **D9-R1** | A product ID must be unique and never reused within its store; product lookup must always include `store_id`. | `TC-D9-R1` | Duplicate ID in `S1` fails with no overwrite; the same literal ID in `S2` is an independent product. |
| **D9-R2** | Product normalization must deterministically apply the Section 1.2 transformations before exact or similar-name comparison. | `TC-D9-R2` | Persian/Arabic variants, ZWNJ, repeated whitespace, and Latin case produce the specified normalized values on repeated runs. |
| **D9-R3** | Creating an active product whose `normalized_name` already belongs to an active product in the same store must fail as an exact duplicate; silent create, merge, and overwrite are forbidden. | `TC-D9-R3` | `EXACT_DUPLICATE` is returned, catalog count stays one, and the existing product/version is unchanged. |
| **D9-R4** | Equal `similarity_key` values with different normalized names must produce `NEAR_DUPLICATE_WARNING`; creation occurs only after explicit candidate-specific acknowledgement in a new operation and must still create a distinct product. | `TC-D9-R4` | Initial submission creates nothing; acknowledged submission creates a new ID without merging or overwriting either product. |
| **D9-R5** | Exact and near-duplicate matching must use only products in the requested store. | `TC-D9-R5` | A matching name in `S1` neither blocks nor appears in a warning for creation in `S2`. |

### 4.2 D10 — Ledger inventory, OCC, immutability, and idempotency

| Rule ID | Testable statement | Test case | Expected result |
| --- | --- | --- | --- |
| **D10-R1** | Inventory must be represented only by appended events, and stock must equal the sum of signed deltas for that store/product. | `TC-D10-R1` | Events `+10` and `-3` produce stock `7` and two immutable ledger entries. |
| **D10-R2** | Updating or deleting an inventory event must be rejected; a correction must append a `CORRECTION` event referencing the original. | `TC-D10-R2` | Original bytes remain unchanged, update/delete fail, and the compensating event alone changes derived stock. |
| **D10-R3** | Negative stock is allowed; shortage alone must not reject a native sale or invoice issuance. | `TC-D10-R3` | Selling two units from zero stock succeeds and derived stock becomes `-2`. |
| **D10-R4** | Every product price/specification edit must supply `base_version`; a mismatch must return an explicit conflict and apply no part of the edit. | `TC-D10-R4` | First edit moves version `7→8`; stale second edit returns `VERSION_CONFLICT` and does not overwrite version 8. |
| **D10-R5** | Once an invoice is `ISSUED`, its content, customer binding, display number, items, issued payload, and digest must be immutable. | `TC-D10-R5` | Every attempted content mutation is rejected and the original issued snapshot/digest remains byte-for-byte unchanged. |
| **D10-R6** | Issued-invoice correction must use only an allowed terminal transition (`REVOKED` or `SUPERSEDED`); a superseding replacement must be a new invoice with new identifiers. | `TC-D10-R6` | Original content remains immutable, replacement references the original, and no item is edited in place. |
| **D10-R7** | Every mutation must include a non-empty `operation_id` and an active same-store `device_id`. | `TC-D10-R7` | Missing identifiers and revoked/cross-store devices fail before any domain effect. |
| **D10-R8** | An exact replay of `(store_id, operation_id, device_id, operation_type, request_hash)` must return the original response atomically with zero additional effects. | `TC-D10-R8` | Repeated product creation returns the original product/response; catalog and ledger effects occur once. |
| **D10-R9** | Reusing a store-scoped `operation_id` with a different device, operation type, or request hash must fail as `OPERATION_ID_CONFLICT`. | `TC-D10-R9` | Mismatches create no effects and cannot retrieve or replace the original response. |
| **D10-R10** | Mode `B` ingestion and issuance alone must create no native `Sale` or `InventoryEvent`. | `TC-D10-R10` | A reviewed Mode `B` invoice is stored without implicit native sale/stock side effects. |

### 4.3 D11 — Per-phone draft lock

| Rule ID | Testable statement | Test case | Expected result |
| --- | --- | --- | --- |
| **D11-R1** | Creating a phone-associated draft must atomically create one active lock for `(store_id, normalized_phone)`, its invoice, and its owning device. | `TC-D11-R1` | Draft and lock commit together with normalized phone and correct device; no partial state is visible. |
| **D11-R2** | While that lock is active, a different device attempting to create a draft for the same normalized phone must receive an immediate hard conflict. | `TC-D11-R2` | `DRAFT_LOCK_CONFLICT`; no second draft, lock, item, or display number is created. |
| **D11-R3** | The owning device's exact replay of the lock-acquiring `operation_id` must follow idempotency rather than conflict with its own lock. | `TC-D11-R3` | Original invoice and response are returned; exactly one draft and one lock exist. |
| **D11-R4** | A different operation cannot create a second concurrent draft for the same store/phone even from the owning device. | `TC-D11-R4` | `DRAFT_ALREADY_ACTIVE`; the original draft remains the only active draft. |
| **D11-R5** | Successful issue must atomically release the active lock with reason `ISSUE`. | `TC-D11-R5` | Another device can immediately start a new draft for the phone; the issued invoice remains immutable. |
| **D11-R6** | Cancelling an active draft must retire it pre-issuance and release the lock with reason `CANCEL`. | `TC-D11-R6` | A new draft can start immediately; the cancelled draft cannot be mutated or issued. |
| **D11-R7** | A Hub-observed client disconnect must release the active lock with reason `DISCONNECT`; late owner writes must not regain ownership. | `TC-D11-R7` | Another device can acquire the phone; late writes from the disconnected device fail. |
| **D11-R8** | Lock expiry must use Hub inactivity time and a positive configurable timeout defaulting to exactly 300 seconds. | `TC-D11-R8` | At `299s` the default lock is active; at `300s` it expires; a store configured to `600s` remains active at `300s`. |
| **D11-R9** | Timeout must release the lock and retire the abandoned draft; acquiring a new lock must not restore write rights to the old device/draft. | `TC-D11-R9` | A new device can create a fresh draft, while old add-item/issue commands fail with no effects. |
| **D11-R10** | Sequential purchases for the same phone must be allowed immediately after each prior lock is released. | `TC-D11-R10` | Two successive issued invoices for one phone succeed with distinct IDs and increasing display numbers. |
| **D11-R11** | Phone locks must be store-scoped; identical phones in different stores must not block or reveal each other's locks. | `TC-D11-R11` | `S1` and `S2` locks coexist; releasing one leaves the other unchanged. |

### 4.4 D12 — Internal ID and Hub-sequential display number

| Rule ID | Testable statement | Test case | Expected result |
| --- | --- | --- | --- |
| **D12-R1** | Every invoice must receive a platform-generated, globally unique `invoice_id` independent of its display number and originating device. | `TC-D12-R1` | Invoices created on different devices/stores never share an internal ID. |
| **D12-R2** | The Store Hub must allocate a positive `display_number` sequentially within each store when the invoice is first durably persisted. | `TC-D12-R2` | `S1` advances `41→42→43`; `S2` allocates from its own independent sequence. |
| **D12-R3** | Concurrent allocations must be serialized by the Hub so each number is issued at most once and allocation order is strictly increasing. | `TC-D12-R3` | Twenty concurrent creates produce exactly `1..20`, with no duplicate or omitted value. |
| **D12-R4** | Display numbers must survive Hub restart and authorized restore, and numbers consumed by retired invoices must never be reused. | `TC-D12-R4` | Restart/restore continues after the recorded maximum; a cancelled number remains an auditable gap. |
| **D12-R5** | Mode `B` `header.invoice_number` must remain untrusted external data and must never determine either platform identifier. | `TC-D12-R5` | Duplicate/external printed numbers do not alter Hub numbering or internal IDs. |
| **D12-R6** | Replaying invoice creation must not allocate another display number or invoice ID. | `TC-D12-R6` | Exact replay returns the original pair; the next new operation receives only the next number. |

---

## 5. Store Isolation Rules with Test Cases

These rules apply to local Hub and Cloud API paths, list and detail reads, writes, searches, sync projections, and identifiers used as references. Denials should not disclose whether an out-of-scope record exists.

| Rule ID | Testable statement | Test case | Expected result |
| --- | --- | --- | --- |
| **SI-R1** | Every store-scoped read must have explicit `store_id` scope and an active user membership in that store. | `TC-SI-R1` | `U-A` cannot list or read `S2` products; no `S2` record or metadata is returned. |
| **SI-R2** | Every device-originated store mutation must require both active membership and an active device paired to that same store. | `TC-SI-R2` | Even `U-BOTH` cannot write `S2` through `D1` (`S1`); the same user can write through `D3` (`S2`). |
| **SI-R3** | A command must reject any relationship whose referenced entity belongs to another store, even if the caller can access both stores. | `TC-SI-R3` | Adding an `S2` product to an `S1` invoice fails atomically with `CROSS_STORE_REFERENCE`. |
| **SI-R4** | Store-level `Customer` records and their sales/invoices must remain isolated even when linked to the same global `CustomerProfile`. | `TC-SI-R4` | An `S1` operator cannot use a phone or profile link to read the customer's `S2` record/history. |
| **SI-R5** | Only the verified customer self, through the registered global user and OTP-backed session, may access cross-store history for a `CustomerProfile`; store roles cannot. | `TC-SI-R5` | Store-owner history request fails; verified customer receives only their own linked history. |
| **SI-R6** | Product identity, inventory events, and stock derivation must never cross a store boundary. | `TC-SI-R6` | Identical product IDs have independent ledgers; an `S1` adjustment cannot change `S2` stock. |
| **SI-R7** | Idempotency records and replay results must be namespaced by `store_id`. | `TC-SI-R7` | The same operation ID can execute independently in two authorized stores; neither replay returns the other store's result. |
| **SI-R8** | Knowing another store's `invoice_id` or `display_number` must not bypass the requested store's authorization/scope. | `TC-SI-R8` | An `S1`-scoped request naming an `S2` invoice returns no invoice content or store metadata. |
| **SI-R9** | A digital-invoice public token resolves only its single issued invoice and grants no store authorization, list capability, or phone-history access. | `TC-SI-R9` | Token view contains only that invoice; attempts to enumerate/store-scope other records fail. |
| **SI-R10** | Locks are isolated by `(store_id, normalized_phone)` and lock errors must not expose another store's state. | `TC-SI-R10` | Same-phone drafts proceed independently in `S1` and `S2`; responses contain no cross-store lock data. |
| **SI-R11** | Shared `Business` ownership must not merge stores or imply membership in sibling stores. | `TC-SI-R11` | A user with only `S1` membership is denied `S2` data even though both stores belong to `B1`. |

---

## 6. Multi-Device Rules (D13) with Test Cases

Multi-device behavior is foundational, not a later compatibility feature. All Hub service tests that exercise mutations must include at least two device identities; the following scenarios are the minimum domain acceptance set.

| Rule ID | Testable statement | Test case | Expected result |
| --- | --- | --- | --- |
| **MD-R1** | From the first implementation increment, every Hub mutation must accept and validate `device_id` plus `operation_id` rather than assuming one local user/device. | `TC-MD-R1` | Two active devices can perform attributed mutations; missing/revoked device identity fails closed. |
| **MD-R2** | Each store must have exactly one active Hub write authority; companions must not write peer-to-peer or maintain an authoritative local ledger. | `TC-MD-R2` | A second active Hub is rejected and a companion-only write never becomes store state. |
| **MD-R3** | Loss of internet must not stop Hub-mediated multi-device operations; accepted effects must synchronize once without duplication after reconnection. | `TC-MD-R3` | Two devices operate during WAN outage, then cloud replicas contain each effect exactly once. |
| **MD-R4** | A companion must not start new store work while its Hub is unreachable; it must fail explicitly instead of using local authoritative state. | `TC-MD-R4` | `HUB_UNAVAILABLE`, no local draft/event/number, and a later retry has only one accepted effect. |
| **MD-R5** | Two devices must be able to build drafts concurrently for different customer phones in one store. | `TC-MD-R5` | Both drafts/locks succeed independently with distinct invoices and display numbers. |
| **MD-R6** | Simultaneous draft creation for the same store/phone must serialize to exactly one active owner. | `TC-MD-R6` | Exactly one command succeeds, the other receives a lock conflict, and only one active draft/lock exists. |
| **MD-R7** | Concurrent edits of the same product version from two devices must use OCC and never silently last-write-win. | `TC-MD-R7` | One edit commits, the other receives `VERSION_CONFLICT`, and final content equals the winning edit. |
| **MD-R8** | Concurrent inventory commands from different devices must both append durable events; derived stock must reflect both. | `TC-MD-R8` | Starting at `10`, concurrent `+5` and `-2` produce stock `13` with both events present. |
| **MD-R9** | Concurrent invoice creation and network retries from multiple devices must preserve both idempotency and Hub numbering. | `TC-MD-R9` | All accepted invoices have unique IDs and gap-controlled numbers; exact retries add no invoice or number. |

---

## 7. Test Cases

These are concrete acceptance scenarios to automate against the eventual domain services. Tests must use a controllable Hub clock for timeouts and synchronization barriers for concurrency. A rejected command may retain its store-scoped idempotency/audit record, but must have no prohibited domain effects.

### 7.1 Entity scenarios (15)

| Test ID | Given | When | Then |
| --- | --- | --- | --- |
| **TC-E01** | No business exists with ID `business-1`. | Create `B1` with a non-blank name, then attempt to reuse `business-1` for another business. | First creation persists one active business; reuse is rejected and the original name is unchanged. |
| **TC-E02** | `B1` exists and no store timeout is supplied. | Create `S1` in Mode `A`. | `S1.business_id = B1`, mode is `A`, and `draft_lock_timeout_seconds = 300`; a later zero/negative timeout update is rejected. |
| **TC-E03** | One global `U-BOTH` account and stores `S1`, `S2` exist. | Assign active memberships in both stores and authenticate the same user in each scope. | The same `user_id` is used, roles remain store-scoped, and `User` itself has no store ownership. |
| **TC-E04** | `D1` is actively paired to `S1`. | Attempt to pair the same `device_id` actively to `S2`, then revoke `D1` and submit a mutation. | Cross-store pairing is rejected; after revocation the mutation is rejected with no effects. |
| **TC-E05** | `U-A` has an active `OWNER` membership in `S1`. | Add another active membership for `(U-A,S1)` and separately create a `CUSTOMER` membership without `customer_id`. | Both are rejected; the original owner membership is unchanged. |
| **TC-E06** | No `S1` customer exists for `P`. | Create a claimed customer, try to mark it verified without a matching profile, then complete OTP and link a profile for `P`. | Claimed customer has no profile; premature verification fails; OTP-backed verification succeeds with matching normalized phone. |
| **TC-E07** | OTP verification for `P` has succeeded once. | Verify the same normalized phone again using differently formatted input. | Exactly one global profile exists for `P`, and the second flow links/reuses it rather than duplicating it. |
| **TC-E08** | `S1` has no product `prod-1`. | Submit a product with negative price, then a valid product with required name/unit and price `1000`. | Negative price fails; valid product persists with `version = 1`, active state, and derived normalized/similarity keys. |
| **TC-E09** | Product `prod-1` has one immutable `+5` event. | Attempt to change that event to `+6`. | Update is rejected, the stored event remains `+5`, and derived stock remains `5`. |
| **TC-E10** | Customers `C1(S1)` and `C2(S2)` exist. | Create a native sale in `S1` referencing `C2`, then create one referencing `C1`. | Cross-store sale fails; same-store sale succeeds and `completed_at` is null while `OPEN`. |
| **TC-E11** | `S1` has active Hub `H1`. | Create two native drafts and inspect their identity/number fields. | Each has a distinct global `invoice_id`, a unique positive `S1` display number, and valid initial lifecycle/retirement metadata. |
| **TC-E12** | `S1` invoice `I1` and `S2` product `prod-2` exist. | Add `prod-2` to `I1`, then add two same-store lines with line number `1`. | Cross-store reference is rejected; duplicate line number is rejected; no partial line is persisted. |
| **TC-E13** | `I1` is a draft and `I2` is issued with a valid canonical payload. | Try creating a DigitalInvoice for `I1`, a 21-character token for `I2`, then a valid 22+-character CSPRNG token for `I2`. | Only the valid issued case succeeds; its payload/hash is immutable and token resolves only `I2`. |
| **TC-E14** | `op-1` completed in `S1` from `D1` with request hash `H`. | Replay `op-1` with hash `H`, then with hash `H2`. | Exact replay returns the stored response; `H2` returns `OPERATION_ID_CONFLICT` and no new effect. |
| **TC-E15** | No active lock exists for `(S1,P)`. | Acquire lock `L1` for `D1`, attempt a second active `S1/P` lock, and acquire `P` in `S2`. | Second `S1` lock fails, `S2` succeeds, and `L1` remains the sole active `S1/P` lock. |

### 7.2 D9 scenarios (5)

| Test ID | Given | When | Then |
| --- | --- | --- | --- |
| **TC-D9-R1** | `S1/prod-1` is active with name `Alpha`; no `S2/prod-1` exists. | Create another `S1/prod-1` named `Beta`, then create `S2/prod-1` named `Beta`. | `S1` duplicate fails without changing `Alpha`; `S2` succeeds independently and store-scoped reads return the correct product. |
| **TC-D9-R2** | The normalizer is run on two identical inputs in separate processes. | Normalize `"  شیر‌كاکائو  "` and `"SHIR  Kakao"`; derive their similarity keys. | Results are respectively `"شیر کاکائو"` and `"shir kakao"` every time; the first similarity key is `"شیرکاکائو"`. |
| **TC-D9-R3** | `S1` has active product `prod-1` named `شیر کاکائو` at version 3. | Submit active product creation with name `" شیر‌كاکائو "` and a new operation ID. | Normalized names match; result is `EXACT_DUPLICATE`; catalog count is one and `prod-1` remains at version 3. |
| **TC-D9-R4** | `S1/prod-1` has normalized name `شیرکاکائو`; the new name normalizes to `شیر کاکائو` and has the same similarity key. | Submit creation with `op-1`, then explicitly acknowledge warning for candidate `prod-1` using new `op-2`. | `op-1` returns `NEAR_DUPLICATE_WARNING` and creates nothing; `op-2` creates `prod-2`, leaving two distinct unmerged products. |
| **TC-D9-R5** | Only `S1` has product `شیر کاکائو`; `U-BOTH` is authorized in both stores. | Create the same name in `S2`. | Creation succeeds without an `S1` candidate or warning payload; `S1` catalog remains unchanged. |

### 7.3 D10 scenarios (10)

| Test ID | Given | When | Then |
| --- | --- | --- | --- |
| **TC-D10-R1** | `S1/prod-1` has no stock events. | Append receipt `+10` (`op-1`) and native sale `-3` (`op-2`). | Exactly two events exist and derived stock is `7`; there is no separately authoritative mutable stock value to diverge. |
| **TC-D10-R2** | Event `E1` contains `+10`; its serialized content is captured. | Attempt update/delete of `E1`, then append `CORRECTION E2` with delta `-2`, reason, and `reverses_event_id=E1`. | Update/delete fail; `E1` is byte-identical; `E2` is appended and stock is `8`. |
| **TC-D10-R3** | Native `S1/prod-1` stock is `0`; a valid draft contains quantity `2`. | Issue the sale/invoice with sufficient authorization and operation envelope. | Issuance succeeds, a `-2` event is appended once, and stock is `-2`; no insufficient-stock error occurs. |
| **TC-D10-R4** | Product version is `7`, price `1000`; `D1` and `D2` each read version 7. | `D1` edits price to `1100` with `base_version=7`; `D2` then edits specification with `base_version=7`. | `D1` commits version 8; `D2` receives `VERSION_CONFLICT(current=8)` and version-8 price/specification is not overwritten. |
| **TC-D10-R5** | Issued `I1` has issued payload bytes `B` and digest `H`. | Attempt separate operations changing quantity, customer, display number, deleting an item, and replacing the issued payload. | All fail; status remains valid, bytes remain `B`, digest remains `H`, and item set is unchanged. |
| **TC-D10-R6** | Issued `I1` requires correction. | Mark `I1` `SUPERSEDED` and create replacement `I2` referencing `I1`; also attempt an in-place edit of `I1`. | In-place edit fails; `I2` has new invoice ID/display number and DigitalInvoice snapshot, while `I1` issued content is unchanged. |
| **TC-D10-R7** | A valid product-creation command is prepared for `S1`. | Submit it without `operation_id`, without `device_id`, with revoked `D1`, and with `D3` in an `S1`-scoped command. | All four submissions fail before product/version/ledger effects; each defect is explicit. |
| **TC-D10-R8** | `op-1` from `D1` successfully created one product and returned response `R`. | Retry the byte-equivalent canonical request with the same store/device/operation ID one and ten times. | Every retry returns `R` and the original product ID; product count and side-effect counts remain those of one execution. |
| **TC-D10-R9** | `S1/op-1` completed from `D1` with request hash `H`. | Reuse `op-1` from `D2`, from `D1` with another operation type, and from `D1` with changed price/hash. | Each returns `OPERATION_ID_CONFLICT`; no new product/edit occurs and the original stored response is not replaced. |
| **TC-D10-R10** | Mode `B` store `S2` receives a contract-valid extraction with product-like lines. | Complete required review and issue the invoice without any separate native sale/stock command. | Invoice/DigitalInvoice exist as applicable; native `Sale` count is zero and no `InventoryEvent` is created. |

### 7.4 D11 scenarios (11)

| Test ID | Given | When | Then |
| --- | --- | --- | --- |
| **TC-D11-R1** | No draft/lock exists for `(S1,P)`; `D1` submits valid creation `op-1`. | Hub persists the phone-associated draft. | One transaction creates draft `I1` and active lock `L1(S1,P,I1,D1)`; a forced failure before commit leaves neither visible. |
| **TC-D11-R2** | `L1(S1,P,I1,D1)` is active. | `D2` submits new draft creation `op-2` using formatted phone `+98 912 000 0001`, which normalizes to `P`. | Immediate `DRAFT_LOCK_CONFLICT`; no `I2`, second lock, item, operation effect, or newly consumed display number exists. |
| **TC-D11-R3** | `op-1` created `I1/L1` for `D1`. | `D1` exactly replays `op-1`. | Original response and `I1` are returned; there is still one draft and one active lock, not a lock conflict. |
| **TC-D11-R4** | `D1` owns active `I1/L1` for `P`. | `D1` submits different operation `op-2` to create another draft for `P`. | `DRAFT_ALREADY_ACTIVE`; `I1/L1` remain unchanged and no second draft is persisted. |
| **TC-D11-R5** | `D1` owns valid draft `I1/L1` for `P`. | `D1` issues `I1`; immediately afterward `D2` creates `I2` for `P`. | Issue and release commit atomically with reason `ISSUE`; `I2` succeeds and `I1` is immutable. |
| **TC-D11-R6** | `D1` owns draft `I1/L1`. | `D1` cancels `I1`, `D2` creates `I2`, then `D1` tries to add an item to or issue `I1`. | `I1` is retired with reason `CANCEL`, `L1` is released, `I2` succeeds, and both old-draft commands fail without effects. |
| **TC-D11-R7** | `D1` owns `I1/L1`; Hub receives an explicit disconnect for `D1`. | Process disconnect, acquire `I2/L2` from `D2`, then deliver a late `D1` item mutation. | `L1.release_reason=DISCONNECT`; `D2` succeeds; late mutation is rejected and does not reactivate `L1`. |
| **TC-D11-R8** | Hub clock is `T0`; `S1` uses default timeout and `S2` is configured to `600s`; both acquire locks with no further activity. | Evaluate locks at `T0+299s`, `T0+300s`, and evaluate `S2` again at `T0+300s`. | `S1` is active at 299 and released/retired at 300 with `TIMEOUT`; `S2` remains active at 300. |
| **TC-D11-R9** | `I1/L1(D1)` has just expired and been retired; `D2` then creates `I2/L2` for the same phone. | Submit add-item and issue commands for old `I1` from `D1`, then inspect both drafts. | Old commands fail (`DRAFT_RETIRED`/lock ownership error); only `I2/L2` is active and no old items/status change occurs. |
| **TC-D11-R10** | No lock exists for `P`; numbering starts at 1. | `D1` creates/issues `I1`, then `D2` immediately creates/issues `I2` for the same phone. | Both sequential purchases succeed; lock ownership transfers only after release, IDs differ, and display numbers are `1`, `2`. |
| **TC-D11-R11** | Authorized devices in `S1` and `S2` each create a draft for `P`. | Inspect both locks, then issue the `S1` draft. | Both locks were active independently; `S1` releases while `S2` remains active, and neither response contains the other store's lock metadata. |

### 7.5 D12 scenarios (6)

| Test ID | Given | When | Then |
| --- | --- | --- | --- |
| **TC-D12-R1** | Hubs for `S1/S2` and devices `D1/D2/D3` are active. | Create invoices on all three devices, including simultaneous creation in different stores. | Every `invoice_id` is a distinct platform-generated UUID; none is derived from display number or external POS number. |
| **TC-D12-R2** | `S1` last allocated display number is `41`; `S2` has no invoices. | Persist two new `S1` drafts and one `S2` draft. | Numbers are `42`, `43` in `S1` and `1` in `S2`; each is Hub-allocated and store-unique. |
| **TC-D12-R3** | `S1` sequence is unused; 20 distinct creation commands are ready on `D1/D2`. | Release all commands through a concurrency barrier at `H1`. | All 20 succeed with display numbers exactly `1..20`, each once, and strictly increasing Hub allocation order. |
| **TC-D12-R4** | `S1` has allocated through `7`. | Restart `H1` and allocate one draft; cancel number `8`; perform an authorized OTP+second-factor restore of the state and allocate again. | Post-restart number is `8`; post-restore next number is `9`; cancelled `8` is retained as an auditable gap and never reused. |
| **TC-D12-R5** | Two Mode `B` captures both carry external `header.invoice_number="0007"`; `S2` next display number is `42`. | Ingest and persist both invoices. | They receive different invoice IDs and display numbers `42`, `43`; both external values remain `"0007"` with untrusted provenance. |
| **TC-D12-R6** | Creation `op-1` returned `I1/display=42`; no other command has run. | Replay `op-1`, then submit new creation `op-2`. | Replay returns `I1/42`; `op-2` returns a new invoice with display `43`, and only two invoices exist. |

### 7.6 Store-isolation scenarios (11)

| Test ID | Given | When | Then |
| --- | --- | --- | --- |
| **TC-SI-R1** | `U-A` has only `S1` membership; `S2` contains products. | Request `S2` product list and a known `S2` product detail using `U-A`. | Both are denied without product data or existence metadata; `S1` reads remain functional. |
| **TC-SI-R2** | `U-BOTH` is authorized in both stores; `D1` belongs to `S1`, `D3` to `S2`. | Submit an `S2` product mutation through `D1`, then the same logical command through `D3` with a fresh operation ID. | `D1` attempt has no `S2` effect; `D3` succeeds with correct user/device/store attribution. |
| **TC-SI-R3** | `U-BOTH`, `S1` invoice `I1`, and `S2/prod-2` all exist. | Add `prod-2` to `I1` in one atomic command. | `CROSS_STORE_REFERENCE`; no item, inventory event, invoice version change, or partial relationship is created. |
| **TC-SI-R4** | One global profile links `C1(S1)` and `C2(S2)` for phone `P`; `U-A` is `S1` owner only. | `U-A` reads `C1`, then requests `C2` and invoice history by `P/profile_id`. | `C1` response contains only `S1` data; `C2`/history requests are denied with no cross-store records. |
| **TC-SI-R5** | The same profile is linked to a registered customer user and invoices in both stores. | Request global history as `S1` owner, then as the verified customer in an OTP-backed self-service session. | Owner is denied; verified customer receives only invoices linked to that profile, with no other customers' records. |
| **TC-SI-R6** | `S1/prod-1` stock is `5`; independently created `S2/prod-1` stock is `9`. | Append `-2` to `S1/prod-1`, then query both stocks and event lists. | `S1=3`, `S2=9`; neither event list contains the other store's events despite identical product IDs. |
| **TC-SI-R7** | `U-BOTH` can mutate both stores; no operation `op-shared` exists. | Execute product creation with `op-shared` in `S1(D1)` and independently in `S2(D3)`, then replay each exactly. | Each store has its own result and product; each replay returns only its own original response with no duplicate effect. |
| **TC-SI-R8** | `U-A` knows `S2` invoice UUID `I2` and display number `7`. | Make `S1`-scoped detail requests using `I2`, and search `S1` for display `7` when only `S2/7` exists. | No `S2` invoice/content/metadata is returned; the request cannot re-scope itself using the identifier. |
| **TC-SI-R9** | Issued `S2/I2` has public token `T`; same-phone invoices exist in `S1` and `S2`. | Open `T` anonymously, attempt to alter its store/invoice parameter, and use `T` as authorization for store or phone-history lists. | Only `I2`'s permitted single-invoice view is returned; enumeration, substitution, store access, and history fail. |
| **TC-SI-R10** | Both stores have an active lock for normalized phone `P`. | Attempt another `S1` draft and inspect its error; then release only `S2`'s lock. | `S1` conflict contains no `S2` lock details; releasing `S2` has no effect on the `S1` lock. |
| **TC-SI-R11** | `S1` and `S2` both belong to `B1`; `U-A` has only `S1` membership. | Access business details, then attempt `S2` catalog, customer, invoice, and mutation requests. | Permitted business metadata does not include `S2` private data; every `S2` store-scoped request is denied. |

### 7.7 Multi-device D13 scenarios (9)

| Test ID | Given | When | Then |
| --- | --- | --- | --- |
| **TC-MD-R1** | `D1`, `D2` are active `S1` companions and `D3` belongs to `S2`. | Submit attributed product adjustments from `D1/D2`; submit one without device ID and one from revoked/cross-store `D3`. | Valid mutations persist with correct operation/device attribution; invalid ones fail with no effects. |
| **TC-MD-R2** | `H1` is already the active Hub for `S1`; `D1` is a companion. | Activate another Hub-role device for `S1`, then attempt a direct companion-local ledger write while bypassing `H1`. | Second Hub activation is rejected; local write is unavailable/not replicated and never becomes authoritative store state. |
| **TC-MD-R3** | WAN is disconnected but `H1`, `D1`, and `D2` communicate over LAN. | Create/issue invoices for `P` and `P2`, queue replication, reconnect WAN, and run sync twice. | Local operations succeed offline; cloud receives each invoice/event once, with stable IDs and no duplicate on repeated sync. |
| **TC-MD-R4** | `D1` cannot reach `H1`; creation `op-1` has never been accepted. | Attempt new draft/sale locally, restore LAN, then retry `op-1`. | Offline attempt returns `HUB_UNAVAILABLE` with no authoritative draft/event/number; retry succeeds exactly once. |
| **TC-MD-R5** | No locks exist for `P/P2`; `D1/D2` submit distinct draft operations simultaneously. | Process both through `H1`. | `D1/P` and `D2/P2` both succeed with separate active locks, invoice IDs, and display numbers. |
| **TC-MD-R6** | `D1/op-1` and `D2/op-2` both request a new `S1/P` draft and wait at a barrier. | Release both requests concurrently. | Exactly one succeeds; the other gets `DRAFT_LOCK_CONFLICT`; one active non-retired draft and one active lock exist. |
| **TC-MD-R7** | Product version is `7`; `D1/D2` prepare different price edits with `base_version=7`. | Submit both concurrently and record which obtains the commit first. | Winner persists version 8; loser gets `VERSION_CONFLICT`; final version/content equals winner, not an unreported mixture. |
| **TC-MD-R8** | Product stock is `10` from event `E0`; `D1` prepares `+5`, `D2` prepares `-2`, with distinct operation IDs. | Commit both concurrently, then rebuild stock from the ledger. | Both events exist once and stock is `13`; neither event overwrites or loses the other. |
| **TC-MD-R9** | Twenty create commands are split across `D1/D2`; each exact request is also scheduled for one retry. | Run all creations/retries concurrently through `H1`. | Exactly 20 invoices exist; display numbers are exactly `1..20`; retries return original responses and add no IDs/numbers. |

### 7.8 Coverage check

| Area | Rule/scenario count |
| --- | ---: |
| Entities | 15 |
| D9 | 5 |
| D10 | 10 |
| D11 | 11 |
| D12 | 6 |
| Store isolation | 11 |
| Multi-device D13 | 9 |
| **Total concrete test cases** | **67** |

---

## 8. Open Questions and Conservative Assumptions

### 8.1 Open questions

The following items need explicit Owner/PM confirmation. Until then, implementations must use the conservative posture already recorded in [`docs/DECISIONS.md`](DECISIONS.md), not silently choose a more permissive behavior.

| Question | Conservative posture used by this specification | Decision needed |
| --- | --- | --- |
| Does active editing/heartbeat extend the five-minute D11 timeout, and is the 5-minute default finally confirmed? | Timeout is configurable, defaults to 300 seconds, and is measured from last Hub-accepted draft activity. Explicit disconnect still releases immediately. | Owner confirmation of default and heartbeat/active-editing semantics before production multi-device rollout (existing `D11` Open item). |
| Should near-duplicate detection include broader fuzzy matching beyond equal conservative similarity keys? | Exact normalized duplicates reject; equal similarity keys with different normalized names warn. No undocumented probabilistic merge or blocking rule. | Owner/PM may approve a deterministic additional algorithm and threshold; any extension needs new fixtures (new Open item). |
| Are auditable display-number gaps from cancelled/timed-out drafts acceptable? | Allocate at first durable Hub persistence, never reuse, and retain retired invoices to explain gaps. Failed atomic creation consumes no number. | Owner/PM confirmation before numbering implementation if gapless issued-invoice numbering is desired instead (new Open item). |

### 8.2 Assumptions recorded in the decision register

`ASM-06`–`ASM-12` record the conservative P-01 choices for documentation scope, product matching, idempotency mismatches, pre-issuance retirement/lock ownership, display-number allocation, tenant/global-profile access, and verified-phone customer linking. These assumptions constrain implementation only until explicitly superseded; none changes a frozen `D1`–`D14` decision or the Canonical Invoice v0 contract.
