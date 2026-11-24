# C-96 / E-72 Cross-Lane Review: Canonical Invoice v1.2 Draft Vector

**Source:** `tests/golden/e72/canonical-invoice-v1-2-draft-vector.json` in `majid-ghanbari-dost/Kandoo-Digital-Invoice` @ `1e620cc`
**Lane:** C# (this repo) — reviewed for compatibility with C# Canonical Invoice model
**Verdict:** COMPATIBLE (synthetic, fictional data only)

## Vector summary

| Field | Value | C# relevance |
|-------|-------|--------------|
| `vector_id` | `E72-SYNTHETIC-001` | Unique test vector ID |
| `synthetic_only` | `true` | Confirmed synthetic — no real customer/sale data |
| `base_invoice.schema_version` | `"0"` | Matches C# Canonical Invoice v0 — the stable schema C# Hub consumes |
| `base_invoice.state` | `DRAFT` | C# Hub handles DRAFT invoices (not yet issued; no terminal-state constraints) |
| `base_invoice.attention_cells` | `[]` (empty) | No review-queue items — C# review page would show empty state |
| `base_invoice.mode` | `B` | Mode B (capture alongside existing POS) — C# supports offline-first Mode B |
| `base_invoice.customer.phone` | `+15550000000` | Fictional — OK per PM-06/PM-17 |
| `base_invoice.store_id` | `synthetic-store-e72` | Fictional — OK per PM-06/PM-17 |
| `v1_2_draft_addendum.schema_version` | `"1.2-draft"` | Draft extension — C# must tolerate unknown/addendum fields without failing |

## C# compatibility assessment

### Compatible
1. **Canonical Invoice v0 base** — C# `CanonicalInvoice` model (schema_version `"0"`) matches the `base_invoice` shape: `invoice_id`, `display_number`, `store_id`, `device_id`, `operation_id`, `mode`, `state`, `summary`, `line_items`, `customer`, `provenance`, `image_sha256`, `template_library_id`, `header`, `state_history`, `public_token`.
2. **DRAFT state** — C# Hub accepts DRAFT invoices; `ISSUED_INVOICE_IMMUTABLE` guard does not apply to DRAFT→terminal transitions.
3. **Empty attention_cells** — C# review queue page handles empty state ("همه‌چیز بررسی شده است" per `States.dc.html`).
4. **Fictional data** — All names, phones, store IDs are synthetic; no real customer/sale data. Compliant with PM-06/PM-17.

### Requires C# tolerance (not a blocker)
1. **`v1_2_draft_addendum`** — This is a draft v1.2 extension object nested alongside the v0 base. C# deserialization must not fail on unknown properties. C# `System.Text.Json` defaults to ignoring unknown properties, so this is compatible as long as the C# model does not use `JsonSerializerOptions` with `DefaultIgnoreCondition` or strict deserialization.
2. **`v1_2_draft_addendum.payments`** — Contains a `cheque` payment type; C# payment model should treat unknown payment types as unrecorded (per management accounting: "Unrecorded payments counted in explicit buckets — nothing guessed").

### Not applicable to C# (PHP/Python lane concern)
- `walk_in_customer`, `currency_unit`, `invoice_discount` — these are v1.2 draft fields for the PHP/Python capture pipeline; C# does not emit or consume them in Canonical Invoice v0.

## Recommendation

The C# lane can consume this vector without modification to the Canonical Invoice v0 model. No C# code changes required. Record as COMPATIBLE.

**Link:** https://github.com/majid-ghanbari-dost/Kandoo-Digital-Invoice/blob/1e620cc1f990bec51566b5e1af26dcec97f26fdb/tests/golden/e72/canonical-invoice-v1-2-draft-vector.json
