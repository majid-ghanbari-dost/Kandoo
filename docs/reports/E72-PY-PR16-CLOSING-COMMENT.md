<!--
BLOCKED DELIVERY — this comment could NOT be posted by the C# lane.

Target: https://github.com/majid-ghanbari-dost/Kandoo-Digital-Invoice/pull/16
Attempted: 2026-11-24, twice, via `gh api repos/majid-ghanbari-dost/Kandoo-Digital-Invoice/issues/16/comments`.
Result: HTTP 403 `Resource not accessible by integration` on both attempts.
Read access on that repo works (PR #16 metadata and existing comments fetch fine), so this is a
write-permission gap, not a connectivity problem.

PM action: either paste the body below onto the Python repo's PR #16, or grant the C# lane
write access to that repo. Body is ready as-is.
-->

## E-72 — C# lane verdict (closing the loop)

The C# review of vector `o17-E-72` (`1e029bdadc3f970d45556352d23b5dc7be40587d`) is recorded on the **C# repo** PR #16:

- **Review (full):** https://github.com/majid-ghanbari-dost/Kandoo/pull/16#pullrequestreview-6039821259
- **Corrected report (Persian, with byte-level evidence):** https://github.com/majid-ghanbari-dost/Kandoo/blob/d1767ac/docs/reports/E72-CANONICAL-INVOICE-V1-2-O17-CSHARP-COMPARISON-FA.md

**Verdict: INCOMPATIBLE** against frozen `src/Kandoo.Core/CanonicalInvoiceConsumer.cs`. C# is fail-fast — it returns exactly **one** error, not a list:

```
SchemaValidationFailed | Unexpected field: base_invoice.
```

Three defect categories: (1) `base_invoice` wrapper instead of flat root; (2) all 16 `RootRequired` fields absent at root; (3) 4 unknown root fields (`base_invoice`, `synthetic_only`, `v1_2_draft_addendum`, `vector_id`).

Two points for your lane:

- **PHP's objection is upheld.** My earlier claim that `image_sha256`/`template_library_id` were duplicated was wrong — each string occurs exactly once in the 1518-byte file (byte offsets 955 and 975), both inside `provenance`. Credit to the PHP lane for catching it. The conclusion stays: those fields are in the right place, but `provenance` itself must move from inside `base_invoice` to root.
- **`v1_2_draft_addendum` needs a decision, not just flattening.** Its content (`currency_unit`, `invoice_discount`, `payments`, `walk_in_customer`, `schema_version: "1.2-draft"`) has no home in Canonical Invoice v0. Promoting `base_invoice` to root makes everything else pass every C# validation step — that addendum is the only part flattening cannot resolve.

Frozen contract unchanged; no C# code changes required. Third review will follow your new tag.
