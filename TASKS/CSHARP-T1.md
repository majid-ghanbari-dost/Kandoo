# [AGENT-TASK CSHARP-T1] C-94 PR + CI watch + PR #12–#16 readiness + R-007 write-back

**Lane:** C# (`majid-ghanbari-dost/Kandoo`) — **Posted by:** exec-manager (2026-11-23, Asia/Tehran)
**How to report:** when done, comment your report on the C-94 PR itself and link it under this file (reply comment or new issue referencing `CSHARP-T1`). PM is the trigger and the only merger.
## Status: DONE (2026-11-23)

- PR #20: https://github.com/majid-ghanbari-dost/Kandoo/pull/20 — CI green (run 45182596762, ubuntu + windows)
- Report comment: https://github.com/majid-ghanbari-dost/Kandoo/pull/20#issuecomment-7103945118
- R-007: docs/reports/R-007-C-94-management-accounting.md (commit d37a578)
- PRs #12–#16 readiness: all green, targeting arena/4bb6cd51-kandoo
- PRs #18/#19: flagged, GREEN, targeting arena/4bb6cd51-kandoo



## Context (verified 2026-11-23)
- Branch `arena/kandoo-c94-management-accounting` **exists** at head `1df4e90eff9990497aea2d1f377ca63c8b18bf2a`, based on `main` @ `bb5ad32`, with exactly 3 commits:
  1. `1a2738b` — management accounting service + endpoint implementation
  2. `850ab1b` — tests (`ManagementAccountingTests` via `HubApiFactory`, synthetic fixtures only)
  3. `1df4e90` — registration fix: `MapAccountingApi()` call added in `HubApiFirstEndpoints.cs`
- **No PR exists for this branch yet** (open PRs are #12–#16, #18, #19 only).

## Work items (in order)
1. **Verify branch state** matches the 3 commits above (do not rebase unless needed).
2. **Open the C-94 PR**: base `main`, head `arena/kandoo-c94-management-accounting`, title `C-94: Hub management accounting — read-only summary endpoint`. PR body MUST cover the full C-94 design contract:
   - Read-only endpoint `GET /v1/stores/{store_id}/accounting/summary/v0`, lightweight management accounting (not trial balance).
   - Data derives ONLY from: successful `invoice.issue` events + product catalog (purchase price) + inventory ledger. No guessing.
   - Missing data goes into explicit `unrecorded` / `excluded` buckets with reason codes.
   - Sales split by payment method: `card_reader` / `card_to_card` / `cash` / `credit`.
   - Receivables come ONLY from recorded `remaining_balance` values.
   - Inventory value computed from ledger at purchase price.
   - Gross margin only for line items whose product is linked AND has a purchase price; everything else goes to `excluded` with reason.
   - Voided / superseded invoices fully excluded.
   - Professional view = informational debit/credit lines (not accounting-firm trial balance).
   - Cross-store access → `403 STORE_ACCESS_DENIED`.
   - Mention commit `1df4e90` (endpoint registration fix) explicitly.
   - **CRITICAL: the PR must NOT touch `.github/workflows/`** (token has no workflows permission; touching it makes the PR unmergeable via API).
3. **Watch CI** on the new PR. Known BLOCKED check: PM-16 "Android 10 emulator launch + 15s liveness" is always red — ignore it, it does not block. Any OTHER red check: fix on the same branch and push. No local dotnet — CI is the final verification.
4. **After green**: report with (a) PR link, (b) final checks table (except PM-16), (c) merge note for PM (mergeable via API like PR #17 since no workflows files).
5. **Write back R-007** per the repo's established report/handoff register convention, and link it in the report.
6. **Then** list merge-readiness of open PRs #12 (C-95), #13 (C-90), #14 (C-91), #15 (C-92), #16 (C-93) — one line each: CI status (excluding known BLOCKED PM-16) + anything blocking PM's manual merge. PRs #18/#19 (C-93-fix-2/3) target base `arena/4bb6cd51-kandoo`, not main — flag their status too.

## Constraints
- Synthetic fixtures only (PM-06) — no real data in tests.
- Do NOT modify `.github/workflows/`.
- Do NOT merge — PM is the merger (trigger role).

## Definition of done
C-94 PR open + CI green (except known BLOCKED PM-16) + one report containing: PR link, checks table, R-007 link, and the #12–#16 readiness table.
