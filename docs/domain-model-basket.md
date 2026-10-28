# KANDOO — CUSTOMER SELF-SERVICE BASKET EXTENSION

**Document status:** `P-01b` domain-model extension; documentation only

**Date:** `2026-10-25`

**Baseline:** C-09-v2 commit `98a5a45983dc25ebe3eb90523dec222935e63fda`

**Scope:** `Basket` and `BasketItem`, their state machine, pricing, product labels, verification, abuse controls, notifications, isolation, acceptance tests, and a non-destructive list of required P-01/P-02/P-04 follow-up changes.

This document does not change the existing fifteen-entity model, protocol, OpenAPI contract, or C# implementation. A basket is a customer **REQUEST**. It is not an invoice, sale, payment, receipt, tax document, inventory movement, or public capability.

---

## 1. Decisions and conservative defaults

### 1.1 Preserved invariants

1. The Store Hub remains the only authoritative writer for store operations (`D7`). A customer application or Cloud transport cannot create an authoritative peer ledger.
2. Every accepted mutation retains store-scoped `operation_id` idempotency, attribution, OCC where applicable, and atomic operation/effect/outbox persistence (`D10`).
3. A basket never receives `display_number`. Only the existing successful `invoice.issue` path allocates one (`D12`).
4. Signed integer thousandths are used for quantities. Rial fields remain unscaled signed integers with checked arithmetic (`ASM-22`).
5. Store isolation, verified-phone rules, deterministic SMS-stub behavior, and the public single-invoice token boundary remain unchanged.
6. A QR product label is an identifier, not authentication and not an invoice/public token.

### 1.2 New conservative defaults pending product tuning

These are implementation baselines, not measured production-capacity claims:

| Setting | Default | Rule |
| --- | ---: | --- |
| `customer_basket_enabled` | `false` | Store-level opt-in; no cross-store default or fallback. |
| Basket lifetime | `24h` | Hub time; expires at exactly `submitted_at + lifetime`. |
| Maximum items per basket | `50` | Reject larger submissions before domain effects. |
| Maximum open `SUBMITTED` baskets per store+customer | `5` | Counts customer requests, not operation IDs or notifications. |
| Customer submit attempts | `10 / 15 min` | Per store+verified customer; retries of an already accepted operation do not add effects. |
| Basket read attempts | `60 / min` | Per store+customer or store+device principal. |
| Product tag check digit | Disabled | Optional store setting; enabling it never changes the underlying unique number. |

No default permits unverified phones, client-set prices, silent price changes, public basket history, tag reuse, or a required Google push service.

---

## 2. Entities

### 2.1 `Basket`

A store-owned customer request header.

| Field | Presence | Rule |
| --- | --- | --- |
| `basket_id` | Required | Hub-issued UUID; immutable and never reused. |
| `store_id` | Required | Explicit tenant scope; part of every lookup and uniqueness boundary. |
| `customer_id` | Required | Store-level Customer reference. |
| `customer_profile_id` | Required | Globally verified-phone profile linked to the Customer. |
| `customer_phone` | Required | Normalized verification snapshot; never inferred from printed content. |
| `phone_status` | Required | Must be `VERIFIED` at submit and revalidated at accept. |
| `status` | Required | `SUBMITTED`, `ACCEPTED`, `REJECTED`, `INVOICED`, or `EXPIRED`. |
| `version` | Required | Positive OCC version; increments on each accepted transition. |
| `submitted_at` | Required | Hub acceptance timestamp. |
| `expires_at` | Required | Hub-computed submission expiry; client time is informational only. |
| `decided_at` | Nullable | Accept/reject timestamp. |
| `invoiced_at` | Nullable | Set only after the linked invoice is successfully issued. |
| `submitted_total_rials` | Required | Checked sum of submitted line snapshots; not an invoice total or debt. |
| `accepted_total_rials` | Nullable | Rechecked total at accept; on success equals the submitted total. |
| `submit_operation_id` | Required | Store-scoped idempotency identity of submission. |
| `decision_operation_id` | Nullable | Accept/reject operation identity. |
| `invoice_operation_id` | Nullable | Successful issue operation identity. |
| `invoice_id` | Nullable | Required and unique within the store only in `INVOICED`; null otherwise. |
| `reject_reason` | Nullable | Bounded store reason in `REJECTED`; no free-form secrets. |
| `created_by_device_id` / actor references | Required as applicable | Preserves customer transport and Hub/store actor attribution without granting peer-writer authority. |

A basket has no `display_number`, issued content hash, Canonical Invoice lifecycle, tax status, payment status, or public token.

### 2.2 `BasketItem`

A store-owned line snapshot belonging to exactly one Basket.

| Field | Presence | Rule |
| --- | --- | --- |
| `basket_item_id` | Required | Hub-issued UUID; immutable. |
| `basket_id` | Required | Parent must belong to the same store. |
| `store_id` | Required | Duplicated deliberately for tenant guards and persistence constraints. |
| `product_id` | Required | Existing store Product reference. |
| `product_tag_number` | Nullable | Present when submission used a printed tag; resolved to the same store Product. |
| `line_number` | Required | Contiguous from 1; unique within the Basket. |
| `description_snapshot` | Required | Product description at submit; not client-authoritative pricing. |
| `product_version_snapshot` | Required | Product version used for the submit snapshot and accept recheck. |
| `quantity_millis` | Required | Positive signed-integer thousandths. |
| `submitted_unit_price_rials` | Required | Active store Product price at submit. |
| `submitted_line_total_rials` | Required | Checked exact line calculation at submit. |
| `accepted_unit_price_rials` | Nullable | Current price rechecked at accept. |
| `accepted_line_total_rials` | Nullable | Checked exact recalculation at accept. |

No BasketItem is an InvoiceItem. A later invoice issue creates separate invoice items through the existing invoice flow; it must not rename or reuse BasketItem IDs as issued-item IDs.

### 2.3 Relationships

```text
Store 1 ─── * Basket 1 ─── * BasketItem
  │              │                 │
  │              │                 └── * ─── 1 Product (same Store)
  │              │
  │              └── * ─── 1 Customer ─── 0..1 CustomerProfile (VERIFIED phone)
  │
  └── 1 ─── * Product 1 ─── 1 immutable store-scoped product_tag_number

Basket 0..1 ─── 0..1 Invoice (only after successful ISSUE; same Store)
```

The `Basket`/`Invoice` relationship is not a claim that every accepted request becomes an invoice, nor that a Basket is a draft invoice.

---

## 3. State machine

### 3.1 States

| State | Meaning | Prohibited interpretation |
| --- | --- | --- |
| `SUBMITTED` | A verified customer request awaits store review and price recheck. | Approved sale, reserved stock, issued invoice, payment due. |
| `ACCEPTED` | The store accepted the unchanged price snapshot and may proceed to the existing invoice flow. | Invoice already issued; display number allocated; payment complete. |
| `REJECTED` | The store declined the request before acceptance. | Cancelled invoice or inventory reversal. |
| `INVOICED` | Exactly one same-store invoice was successfully issued for the accepted request. | A request state assigned by the client or by basket acceptance alone. |
| `EXPIRED` | A still-`SUBMITTED` request reached Hub expiry. | Retired/issued invoice; reuse of its identity for a new request. |

### 3.2 Allowed transitions

| From | To | Actor / trigger | Required guards |
| --- | --- | --- | --- |
| New entity | `SUBMITTED` | Verified customer request accepted by the Hub | Feature enabled, verified phone, valid items, authoritative price snapshot, abuse limits. |
| `SUBMITTED` | `ACCEPTED` | Authorized store device/user | Not expired, still verified phone, current prices/products match, current `base_version`. |
| `SUBMITTED` | `REJECTED` | Authorized store device/user | Not expired, current `base_version`, bounded reason. |
| `SUBMITTED` | `EXPIRED` | Hub-observed timeout | `now >= expires_at`; no client timestamp. |
| `ACCEPTED` | `INVOICED` | Existing successful invoice issue | Same-store accepted Basket, invoice payload binds `basket_id`, exactly one issued invoice, atomic transition. |

Rules:

1. There is no direct `SUBMITTED → INVOICED`, `ACCEPTED → REJECTED`, or terminal-state transition.
2. `ACCEPTED` no longer uses the submission-expiry timer; the store has accepted the request but still must use the normal issuance rules.
3. `REJECTED`, `INVOICED`, and `EXPIRED` are terminal and immutable.
4. A price-change conflict leaves the Basket `SUBMITTED`; it does not silently update the snapshot. The customer may create a new request at current prices, or the store may reject the old request.
5. Expiry and customer disconnect do not create an invoice or consume a display number.
6. Linking an accepted Basket to an invoice is atomic with successful `invoice.issue`. A failed issue leaves the Basket `ACCEPTED` and allocates no number.

---

## 4. Pricing rules

### 4.1 Snapshot at submit

1. The Hub loads every Product in the explicit store and requires it to be active.
2. Client-supplied prices, totals, currency conversions, discounts, and tax fields are not authoritative. Unknown pricing fields are rejected rather than silently applied.
3. Each line stores the current product version, description, unit price, quantity, and exact line total.
4. Use checked integer arithmetic:

```text
line_total_rials = (quantity_millis * submitted_unit_price_rials) / 1000
basket_total_rials = checked_sum(line_total_rials)
```

5. Until a rounding decision is explicitly approved, a line whose numerator is not exactly divisible by `1000` is rejected as `BASKET_PRICE_NOT_WHOLE_RIAL`; no binary float or implicit floor/ceiling is allowed.
6. The snapshot is immutable after submission. Replaying the same operation returns the same snapshot and has no new effect.

### 4.2 Recheck at accept

1. Reload every Product in the same store and recalculate all lines from current active price/version.
2. Require exact equality of every unit price and total with the submitted snapshot.
3. A difference returns `BASKET_PRICE_CHANGED` with no accept, invoice, inventory, or notification success effect.
4. An inactive/missing Product returns `BASKET_PRODUCT_UNAVAILABLE` with the same no-effect guarantees.
5. Successful accept records the rechecked values and current OCC version; it still does not issue an invoice.

---

## 5. Store-level switch

`Store.customer_basket_enabled` is store-owned configuration, audited and changed only by an active owner through an idempotent operation.

| Operation while disabled | Behavior |
| --- | --- |
| New Basket submit | Reject `BASKET_DISABLED`; create no Basket, tag, notification, or invoice. |
| Accept a previously submitted Basket | Reject `BASKET_DISABLED`; the request remains `SUBMITTED`. |
| Reject a previously submitted Basket | Allow the store to close the request explicitly. |
| Expire a previously submitted Basket | Allow normal Hub-time expiry. |
| Issue for an already `ACCEPTED` Basket | Allow the existing invoice flow; disabling new requests must not rewrite an accepted commitment. |
| Read own existing Basket | Allow subject to normal authentication/isolation. |

Enabling the feature does not enable public listing, unverified customers, client pricing, or sibling-store access.

---

## 6. Product tag numbers and optional check digit

### 6.1 Identity and allocation

1. `product_tag_number` is a positive unsigned decimal integer unique within one store.
2. It is allocated atomically by the Hub from a dedicated per-store high watermark. It is independent of invoice `display_number`, `operation_seq`, Product UUID, and Basket UUID.
3. The number is immutable and never reused after product retirement/deletion, Basket rejection/expiry, Hub restart, restore, correction, or re-pairing.
4. Restore preserves both assignments and the tag high watermark. A recovered conflicting tag is review material, never silently renumbered.
5. A tag resolves only inside its assigned store. Equal numbers in different stores are unrelated.

### 6.2 Optional check digit

1. A store may enable one decimal Luhn check digit for transcription validation.
2. The check digit is derived from the decimal `product_tag_number`; it is not part of the uniqueness counter and is never allocated independently.
3. Printed form is `<product_tag_number>-<check_digit>` when enabled, otherwise `<product_tag_number>`.
4. A scanner validates syntax and check digit, then resolves `(store_id, product_tag_number)`. A valid check digit does not prove authorization or current product availability.
5. Enabling/disabling printing of check digits does not change existing tag numbers. Persisted references use the numeric portion.

---

## 7. Printed QR product-label specification

### 7.1 Payload

The QR payload is compact UTF-8 JSON with recursively sorted keys, no terminal newline, and this exact shape:

```json
{"product_tag_number":123,"schema_version":"0","store_id":"11111111-1111-4111-8111-111111111111"}
```

When check digits are enabled, the numeric field remains a JSON integer and an additional string field is present in sorted order:

```json
{"check_digit":"0","product_tag_number":123,"schema_version":"0","store_id":"11111111-1111-4111-8111-111111111111"}
```

The example uses Luhn check digit `0` for tag `123`. Implementations must calculate and test the value rather than accept a client-supplied check digit.

### 7.2 Human-readable and print requirements

1. Print store name, product description, and full tag (including check digit when enabled) next to the symbol.
2. Do not print price, customer data, Basket ID, invoice ID, public invoice token, recovery key, session token, or a claim that scanning creates a sale.
3. Use QR error correction `M`, a four-module quiet zone, dark-on-light contrast, and a smallest recommended printed symbol width of `25 mm` for the MVP label profile.
4. A label is reprintable with identical payload. Reprinting does not allocate a new tag.
5. A damaged/unknown/wrong-store/check-digit-invalid label returns a scoped validation error and cannot submit a Basket.
6. Product price is always loaded at Basket submit, never trusted from a label or cached customer screen.

The QR is a product identifier only. It grants no anonymous read or mutation capability.

---

## 8. Verified-phone requirement

1. Submission requires a normalized exact match among the Customer, linked CustomerProfile, and authenticated customer principal, with `phone_status = VERIFIED`.
2. `CLAIMED`, an outstanding OTP challenge, an expired verification, a different normalized phone, or a profile link inferred from OCR/label content returns `PHONE_NOT_VERIFIED` with no Basket effect.
3. Store acceptance revalidates the link/status. If verification was revoked after submission, accept fails closed.
4. Basket status notifications do not verify a phone and cannot be used as an OTP bypass.
5. Full invoice history remains outside this extension. Basket read access is limited to the customer’s own requests or authorized staff in the same store.

---

## 9. Abuse controls

1. Enforce the defaults in Section 1.2 through store-scoped persistent counters, not only process memory.
2. Apply the open-request and item limits before creating domain effects. Exact idempotent replays do not create another request or consume another open-request slot.
3. Rate-limit failed attempts as well as successful submissions to prevent verification, tag, and price probing. Return `RATE_LIMITED` with retry metadata.
4. Bound reject reasons, description snapshots, request bodies, and notification payloads; reject oversized input without partial writes.
5. Tag scans and live-connection reconnects use the same authentication, store scope, and limits as other reads.
6. Repeated accept/reject/expire transitions with new operation IDs cannot bypass terminal-state or OCC checks.
7. Notifications and polling cursors do not reset submission, recipient, or verification abuse counters.

---

## 10. Notification design without Google push assumptions

### 10.1 Authority and events

Notifications are hints, never authoritative state. The durable Hub operation/outcome remains the source of truth.

Emit a minimal event for `SUBMITTED`, `ACCEPTED`, `REJECTED`, `INVOICED`, and `EXPIRED` containing only:

```text
event_id, store_id, basket_id, status, version, occurred_at
```

A client re-fetches the visible Basket when needed. Events contain no phone, item prices, invoice token, recovery material, or full customer history.

### 10.2 Delivery paths

1. **LAN:** authenticated in-app live connection to the Store Hub (SSE or WebSocket selected at implementation time). Hub-accepted changes are published to connected store/customer applications even when WAN is unavailable.
2. **Online:** authenticated in-app live connection to Cloud using the same event/cursor semantics. Cloud is a transport/projection, not a peer writer.
3. **Fallback:** authenticated cursor polling with durable event IDs. Missed events are replayed in order and deduplicated by the client.
4. **Optional future OS push:** may be added behind a provider interface only after a separate decision. Google/FCM, Google Play services, or any specific vendor is not required, assumed, or part of correctness tests.
5. Basket status notifications are not invoice SMS. C-09 invoice-link delivery remains applicable only after a real issued invoice exists.

### 10.3 Delivery guarantees

1. Delivery is at least once; an exact replay produces no new domain event.
2. A disconnected client can poll from its last event ID without treating a missed notification as a missed state transition.
3. Authorization is evaluated on connect and on replay/read. A subscription cannot cross store or customer boundaries.
4. Expiry/notification worker failure never changes the already durable Basket result or permits a duplicate invoice.

---

## 11. Store isolation and authorization rules

1. Persistence keys and queries include `store_id` for Basket, BasketItem, tag counters, configuration, and notification cursors.
2. `basket_id`, `invoice_id`, `product_id`, and tag values are insufficient without the matching store scope.
3. A customer sees only requests linked to their verified CustomerProfile in that store; staff see only stores where they have active authorized membership.
4. Accept/reject requires an active store device/user. Customer transport cannot approve its own request.
5. A printed label cannot select a sibling store, reveal current price, enumerate products, or bypass membership.
6. `invoice_id` and `basket_id` must be unique together within the store; an accepted Basket cannot be attached to another store’s invoice or to two invoices.
7. Live events and polling filter by both store and principal. A shared Business or global CustomerProfile never grants sibling-store staff access.
8. Unknown and invisible Basket reads use the same not-found response to avoid cross-store enumeration.
9. No anonymous Basket list, customer-history endpoint, or public mutation endpoint is introduced.

---

## 12. Acceptance-test matrix

Every rule below must run against the Hub/domain implementation where applicable and against the HTTP contract when implemented. Cross-platform CI must include Ubuntu and Windows; notification tests use in-process/live test transports, never Google services.

| Test ID | Required proof |
| --- | --- |
| `TC-BASKET-R01` | Submit creates `SUBMITTED` Basket/items and no Invoice, InvoiceItem, Sale, InventoryEvent, display number, or public token. |
| `TC-BASKET-R02` | Valid accept moves only `SUBMITTED → ACCEPTED`; no invoice or number is created. |
| `TC-BASKET-R03` | Valid reject moves `SUBMITTED → REJECTED` and is terminal. |
| `TC-BASKET-R04` | At `expires_at - 1 tick` the request is active; at exactly `expires_at` it is `EXPIRED` with no invoice effect. |
| `TC-BASKET-R05` | `ACCEPTED → INVOICED` succeeds only atomically with same-store successful issue and its assigned display number. |
| `TC-BASKET-R06` | Direct submit-to-invoice, accept-to-reject, and all terminal transitions fail with zero effects. |
| `TC-BASKET-R07` | Failed issue leaves an accepted Basket unchanged and consumes no display number; exact issue replay cannot transition twice. |
| `TC-BASKET-R08` | Submit uses authoritative store Product price/version, not client-supplied price or total. |
| `TC-BASKET-R09` | Unchanged prices pass accept recheck and persist matching submitted/accepted values. |
| `TC-BASKET-R10` | Any current-price difference returns `BASKET_PRICE_CHANGED`, leaves `SUBMITTED`, and has no invoice/notification-success effect. |
| `TC-BASKET-R11` | Missing/inactive Product at submit or accept fails closed in the same store. |
| `TC-BASKET-R12` | Fractional-Rial and overflow cases return explicit validation errors without binary floating point or partial lines. |
| `TC-BASKET-R13` | Disabled store blocks new submit and accept; reject/expire and pre-accepted issue follow the switch matrix. |
| `TC-BASKET-R14` | Store switch changes are owner-only, audited, idempotent, and invisible to sibling stores. |
| `TC-BASKET-R15` | Concurrent product-tag allocations are contiguous/unique within one store and independent of display/operation counters. |
| `TC-BASKET-R16` | Restart/restore and product retirement never reuse a tag or regress its high watermark. |
| `TC-BASKET-R17` | Valid optional check digit verifies; altered digits fail without product or Basket effects. |
| `TC-BASKET-R18` | Equal tag numbers in two stores resolve only their own Product and never cross tenant scope. |
| `TC-BASKET-R19` | QR payload round-trips exact UTF-8 canonical shape; reprint preserves bytes and assigns no new number. |
| `TC-BASKET-R20` | Label inspection proves required human text/quiet zone and absence of price, customer, invoice, token, and credential data. |
| `TC-BASKET-R21` | Unknown, malformed, wrong-store, and check-digit-invalid labels cannot submit or read a Basket. |
| `TC-BASKET-R22` | `CLAIMED`, pending OTP, expired, mismatched, and inferred phone identities return `PHONE_NOT_VERIFIED`. |
| `TC-BASKET-R23` | Exact normalized `VERIFIED` phone can submit; revocation before accept blocks accept. |
| `TC-BASKET-R24` | Item/open-request/body limits reject before effects; exact replay does not consume another slot. |
| `TC-BASKET-R25` | Submit/read abuse windows return `RATE_LIMITED` and retry metadata; alternate operation IDs cannot bypass them. |
| `TC-BASKET-R26` | Exact operation replay returns the original Basket/result with one outbox effect; changed payload under the same operation ID conflicts. |
| `TC-BASKET-R27` | Stale `base_version` on accept/reject returns `VERSION_CONFLICT` without overwriting a newer state. |
| `TC-BASKET-R28` | Hub LAN live delivery works with WAN unavailable; Cloud live delivery works online using equivalent event semantics. |
| `TC-BASKET-R29` | Reconnect/poll replays missed events in order and deduplicates them; no Google/FCM dependency is present. |
| `TC-BASKET-R30` | A forged notification cannot change Basket state; only the durable accepted operation is authoritative. |
| `TC-BASKET-R31` | Cross-store Basket read/write, staff access, tag lookup, invoice linking, and notification subscription all fail closed. |
| `TC-BASKET-R32` | Customer A cannot read Customer B’s Basket; unknown and invisible IDs share one not-found response. |
| `TC-BASKET-R33` | A Basket cannot link to two invoices or to another store’s invoice; one accepted request has at most one issued link. |
| `TC-BASKET-R34` | Basket rejection, expiry, and failed acceptance consume no display number and leave the issued-only high watermark unchanged. |

---

## 13. Required follow-up changes — listed only, not performed

`P-01b` intentionally leaves every existing document unchanged. A later explicitly assigned contract/protocol task must make the following changes instead of treating this section as proof that they already exist.

### 13.1 Required P-01 changes (`docs/domain-model.md` and decision register)

1. Add Basket and BasketItem to the entity count, relationship diagram, persistence constraints, and store-isolation rules.
2. Add the state machine, price snapshots/recheck, store switch, verified-phone guard, abuse defaults, and notification-authority boundary.
3. Add immutable store-scoped product tag numbers, optional Luhn check digit, dedicated high watermark, and QR label profile.
4. Add `TC-BASKET-R01`–`TC-BASKET-R34` to the domain acceptance suite.
5. Record owner confirmation or changes for the conservative lifetime/rate/item/open-request defaults and the whole-Rial restriction.
6. Clarify that accepted Basket-to-invoice linking extends the existing issue flow without changing Canonical Invoice v0 or D12.

### 13.2 Required P-02 changes (`docs/sync-protocol.md` and vectors)

1. Add Hub operation types `basket.submit`, `basket.accept`, `basket.reject`, `basket.expire`, and `store.basket_config_update`. Product tag allocation must be part of the relevant product-create operation or receive a separately specified idempotent operation; it cannot be an untracked side effect.
2. Extend `invoice.issue` payload/outcome with optional same-store `basket_id` binding. Successful issue atomically transitions the Basket; no independent client `basket.invoice` transition is allowed.
3. Define payload/result schemas, OCC fields, actor roles, stable errors, expiry-worker attribution, and notification event outcomes for the new operations.
4. Include baskets, basket items, store basket configuration, tag assignments, and tag high watermarks in restore/replay/state-hash projections. Runtime live connections and notification delivery cursors remain outside authoritative state.
5. Add LAN/Cloud/failure vectors for idempotent submit/accept/reject, price conflict, verified-phone loss, exact expiry, tag uniqueness/restore, issue linking, isolation, and notification replay.
6. Preserve strict single-Hub authority: customer/Cloud transports relay requests but never become alternate authoritative writers.

### 13.3 Required P-04 changes (`docs/openapi.yaml` and `docs/cloud-api-contract.md`)

1. Add versioned customer endpoints for submit/get/list-own and authenticated polling/live events; add store-device accept/reject and owner basket-switch endpoints. Every mutation endpoint is a transport to the authoritative Hub and cannot write an independent Cloud projection when the Hub is unreachable.
2. Add an authenticated same-store product-by-tag resolver for Basket use. Do not add an anonymous Basket/product-history endpoint.
3. Bind every mutation to `Idempotency-Key = operation_id`, `base_version` where applicable, explicit store scope, and the standard error envelope/rate headers.
4. Add stable errors including `BASKET_DISABLED`, `BASKET_NOT_FOUND`, `BASKET_STATE_CONFLICT`, `BASKET_PRICE_CHANGED`, `BASKET_PRICE_NOT_WHOLE_RIAL`, `BASKET_PRODUCT_UNAVAILABLE`, `BASKET_EXPIRED`, `PHONE_NOT_VERIFIED`, `PRODUCT_TAG_NOT_FOUND`, `CHECK_DIGIT_INVALID`, `RATE_LIMITED`, and `VERSION_CONFLICT`.
5. Specify SSE/WebSocket selection, event cursor, replay limits, authentication refresh, and polling fallback without naming Google/FCM as a dependency.
6. Extend the existing issue-operation contract with optional `basket_id` only through the P-02 entry shape; do not create a Cloud-side invoice writer.
7. Re-run structural checks and Redocly with zero errors/warnings after the actual YAML revision. This P-01b document does not claim such validation has occurred.

---

## 14. Explicit non-goals

- No Basket C#, database table, endpoint, UI, QR generator, printer driver, live-provider integration, or push-service integration is delivered by P-01b.
- No guest/unverified checkout, anonymous history, peer-to-peer customer ledger, client-authoritative price, stock reservation, payment flow, tax computation, or invoice issuance shortcut is introduced.
- No existing document is modified by the Basket specification itself; Section 13 is the complete required-change list for later authorized tasks.
