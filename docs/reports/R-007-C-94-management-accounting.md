# R-007 — CSHARP-T1 / C-94 Management Accounting Summary — Completion Report

## Summary

CSHARP-T1 (C-94) is complete. PR #20 is open at https://github.com/majid-ghanbari-dost/Kandoo/pull/20, head `dc49ba6d67322da8c420697dd45c4e9f7bc8745a`, CI run 45182596762 — **GREEN on both ubuntu-latest and windows-latest**.

## Deliverable

Read-only management accounting endpoint `GET /v1/stores/{store_id}/accounting/summary/v0`, derived only from successful immutable `invoice.issue` events plus the product catalog and append-only inventory ledger. No real data — synthetic fixtures only (PM-06). Does not touch `.github/workflows/` (no emulator impact). Does not touch `KandooHub.Installer.iss` or `Invoke-InstallerSmoke.ps1`.

## Components

| Component | File | Role |
|-----------|------|------|
| Accounting endpoint | `src/Kandoo.Hub/HubAccountingApiEndpoints.cs` | `MapAccountingApi` registers `GET /v1/stores/{store_id}/accounting/summary/v0` via `MapHubApiFirstEndpoints` |
| Accounting service | `src/Kandoo.Hub/ManagementAccountingService.cs` | Derives payment methods, receivables, stock value, profit, professional ledger |
| Synthetic tests | `tests/Kandoo.Hub.Tests/ManagementAccountingTests.cs` | Issued, Revoked, unpriced, no-payment scenarios |
| Invoice guard fix | `src/Kandoo.Hub/SqliteHubTransaction.cs` | `SaveInvoice` allows `Issued → Revoked/Superseded` terminal transition |
| SQLite trigger fix | `src/Kandoo.Hub/SqliteHubStore.cs` | Trigger `invoices_terminal_update_forbidden` permits same transition |
| JsonElement GC fix | `src/Kandoo.Hub/SqliteHubStore.cs` | `ReadReplicationEntries` clones `JsonElement` Payload/Outcome/ResponseSnapshot to prevent dangling references after GC |
| Test factory fix | `tests/Kandoo.Hub.Tests/HubApiTestHarness.cs` | Registers `SqliteHubStore` as `TestStore` so accounting endpoint reads populated test store |

## Accounting Scope

- **Sales by payment method:** card-reader, card-to-card, cash, credit, plus unrecorded bucket
- **Receivables:** outstanding invoice count and amount, plus unrecorded remaining-balance bucket
- **Stock value:** priced products (at purchase price) and unpriced products (stock millis only)
- **Profit:** included items (products with purchase price) vs excluded items (no product / no purchase price)
- **Professional ledger:** debit/credit view with Persian note (informational, not formal balance sheet)
- **Revoked/superseded invoices:** excluded entirely from all figures
- **Unrecorded:** payments or remaining balance not recorded on the issue event → explicit buckets, never guessed

## Fixes Applied

1. **`ISSUED_INVOICE_IMMUTABLE`** — `SaveInvoice` C# guard and SQLite trigger both blocked all updates to terminal invoices, including the legitimate `Issued → Revoked/Superseded` transition needed by the C-94 revoke test. Fixed both to allow this specific transition while blocking all other terminal-state mutations.

2. **Counters reading as 0** — `ReadReplicationEntries` deserialized `entry_json` into `ReplicationLogEntry` with `JsonElement` Payload/Outcome/ResponseSnapshot properties backed by the deserialization's internal `JsonDocument`. After `Deserialize` returned, the document was unreferenced and could be GC'd, leaving `JsonElement` values dangling. Downstream consumers (`ManagementAccountingService`) then read `ValueKind = Undefined`, causing all counters to read as 0 ("Expected: 1, Actual: 0"). Fixed by cloning each `JsonElement` property so it holds an independent reference to its backing document.

3. **Wrong store instance in test factory** — `HubApiFactory.ConfigureWebHost` replaced `IHubStore` and `IHubAccessStore` with `TestStore` but did not replace `SqliteHubStore`. `Program.cs` registers `SqliteHubStore` as a separate singleton with the default connection string (`kandoo-hub.db`, empty). The accounting endpoint requested `SqliteHubStore` via DI and received the empty store instead of the populated `TestStore`, so `ReadReplicationEntries` returned 0 entries. Fixed by registering `services.AddSingleton<SqliteHubStore>(TestStore)`.

## PM Rulings Executed

- PM-13..PM-16: COMPLETE
- M-005 items 2, 3a, 3c: COMPLETE
- R-005: COMPLETE (content filed at commit `4a2e520`, pushed)
- R-006: COMPLETE (`docs/reports/R-006-C-96-time-estimates.md`, commit `f69860e`, pushed)

## Blockers

- None for CSHARP-T1. Emulator PM-16 remains BLOCKED per ORDER 16 rule 7 (3 fix pushes exhausted, `continue-on-error` kept) — ignored per instructions, not a code regression.

## Handoff

CI is green on both OSes. PR #20 is ready for PM merge review. Agent does NOT merge — PM is merger. Branch `arena/kandoo-c94-management-accounting` remains open awaiting PM action.

## Next (after PR #20 merge)

- PR #12–#16 readiness table: all green, conflict-free vs `main`, ready for PM merge on `arena/4bb6cd51-kandoo`
- PR #18 / #19: both GREEN, flagged as targeting `arena/4bb6cd51-kandoo`
- Update `AGENT_HANDOFF.md` after every major event (this report)
- Do NOT tag `main`, do NOT force-push, do NOT create coordination channel issue (ORDER 20-B)
