# [TEAM DISPATCH MVP-1] Ten parallel agents — M-013 / PM-32 (owner: boundaries lifted, 10x)

**Posted by:** exec-manager (2026-11-27, Asia/Tehran). **Source:** M-013 (`docs/messages/M-013.md` on `main` of this repo) + the owner's direct order 2026-11-27 (R-014, `docs/messages/R-014.md` on `arena/order-15-status`).
**This file SUPERSEDES the single-agent dispatch** in `TASKS/CSHARP-T3.md`, `TASKS/PHP-T3.md` and `TASKS/PY-T3.md` for newly started sessions. Any still-running single-agent session finishes its CURRENT stage, then hands off per the map below and stops.

## Standing rules (M-013, in force for every agent)
- **Merge rights (PM-31):** every agent merges its OWN green PRs into `main` (workflow-touching included). Discipline: merge `origin/main` into your branch FIRST; never force-push or rewrite `main` (PM-24); small PRs; squash or merge commit. If `main` goes red, the last merger fixes it first.
- **Path ownership (PM-32):** ONE owner per path at any time. Touching another owner's path = PR against that owner's branch, never a direct commit.
- Branches `arena/<agent-name>/<task>`; tags `o17-<task>`; never move existing tags.
- **Honesty labeling stays:** every generated fixture/sample is clearly marked synthetic/fictional, never presented as a real scan.
- **No per-task Persian report files (PM-33):** status = PR description (task, head SHA, run URL, one-line result) + tag + auto `docs/STATUS.md`.
- **Decisions (PM-30):** decide technical/process questions yourself; record ONE line in `docs/decisions-made.md`; consult the PM only for conflicts with the boards / `docs/product-ux-spec.md` / product vision, or anything that costs money.

## Agents, path ownership and chains (execute in order)

### C-PLAT — C# platform (repo `majid-ghanbari-dost/Kandoo`)
Owns: `.github/workflows/`, `scripts/`, `packaging/`, `src/Kandoo.Hub*`, `src/Kandoo.Core*`.
1. **FIRST TASK (M-013 §4, target: PR checks under 8 minutes):** NuGet + npm caching; split the Windows job into parallel jobs (build / tests / installer / smoke); run the installer smoke only for changes under `src/Kandoo.App`, `packaging/`, `scripts/` or on `main`; per-lane path filters; `concurrency` cancel for superseded runs. **REMOVE the Android 10 emulator job completely** (it always fails — "Timeout waiting for emulator to boot."; C-91 acceptance = signed-APK artifact + emulator-free tests + the PO's J8 test). Tell the manager the Actions-minutes delta.
2. **`docs/STATUS.md` generator (M-013 §5):** small script run by CI on `main` writing open PRs, last green run per lane, tags, installer artifact link; the workflow commits it to `main` as a normal commit.
3. **Daily build (M-013 §6):** every merge to `main` publishes `kandoo-hub-c87-setup-exe` + the signed Android APK; `docs/STATUS.md` links the latest of each.
4. Then: Hub/replication/auth hardening — C-90 pilot items (replication retry/backoff, idempotency tests), installer smoke maintenance. Est. total ~14h (first 8h critical for MVP-1).

### C-WIN-A — Windows screens A (repo `Kandoo`)
Owns: Setup, Role, Review, Invoice, Invoices list/detail pages under `src/Kandoo.App/` (C-96 groups 1-2) + task C-99.
1. **C-99** `GET /v1/stores/{id}/invoices` — `limit` + `next_cursor` (same pattern as products), list projection (number, customer with masked mobile, date, amount, status draft/issued/cancelled, `sms_status` sent/failed/none joined from the SMS delivery record), filters `status` + `sms_status`, "needs review" chip hidden until Mode B, tests included. Route files touching `src/Kandoo.Hub*` go through a PR to C-PLAT's branch (path ownership); the Invoices screen is built ONCE against it.
2. **Setup screen per Q-C98-02=B:** board steps 1-3 (store info / manager-number verification by SMS code / backup path+schedule); step 2 uses the DEMO outbox SMS code with the Persian on-screen demo note; Hub start / database / owner-session runs in the BACKGROUND after step 1 with a progress indicator; first user becomes manager automatically; Group 1 stays PARTIAL until this is built.
3. Review + Invoice screen verify/fix vs `Review.dc.html` / `Invoice.dc.html` (screenshot artifact per screen + report line in the PR description). Est. ~18h.

### C-WIN-B — Windows screens B (repo `Kandoo`)
Owns: Products, Customers, Return, Settings, Devices, States, Accounting views under `src/Kandoo.App/` (C-96 groups 3-4, C-94 UI).
1. Products + Customers verify/fix vs boards (G3). 2. Return + States (G4). 3. Settings + Devices v2 (pairing, revoke). Screenshot artifact per screen + report line per PR. Est. ~16h.

### C-AND-STORE — Android store app (repo `Kandoo`)
Owns: Android store phone screens + the Android project surface for store mode (C-91, C-97 store boards), APK pipeline.
1. Verify/fix store phone screens vs `Phone-*.dc.html` boards: pairing, products, invoice issue, review queue, invoices list. 2. Signed release APK artifact on every `main` merge (coordinate with C-PLAT). 3. Emulator-free tests only (the emulator job is gone). Est. ~18h.

### C-AND-CUST — Android customer mode (repo `Kandoo`)
Owns: customer mode + customer screens (C-92, C-97 customer boards), role switch.
1. Customer mode screens vs boards; role switch; end-to-end against the PHP Cloud customer API (P-31 vectors accepted by PM-13). Est. ~10h.

### C-HOLOO — Holoo connector (repo `Kandoo`)
Owns: Holoo connector, shadow store, capture path integration (C-93 and follow-ups).
1. C-93 follow-ups; capture-folder -> review-item integration with the Python intake contract. Holoo live stays OUT of MVP-1 (support role for D1). Est. ~8h.

### PHP-A — customer web page (repo `majid-ghanbari-dost/kandoo-cloud-php`)
Owns: customer web invoice page, local kit, admin page.
1. Push P-36, CI green, tag `o17-P-36`, PR #3 out of draft (v2 receipt look, normal / empty / locked states, screenshot artifacts, HTTP-path proof). 2. Local end-to-end with the C# Hub (Q-C98-01=A URL shape `http://<LAN-IP>:<cloud-port>/i/<token>`; EXACT copy-paste commands for the PO). 3. P-32 admin v2 (missing board -> `docs/questions/`). Est. ~12h.

### PHP-B — customer API + security (repo `kandoo-cloud-php`)
Owns: customer API, OTP, security, load, host kit.
1. P-31 finish (vectors accepted; Android E2E stays with C#/PO), OTP continuation from partial `cf52405`, P-33 kit, P-34 local load + security, tested backup/restore. Est. ~14h.

### PY-A — E-74 sample + re-score (repo `majid-ghanbari-dost/Kandoo-Digital-Invoice`)
Owns: E-74 sample build + re-score, E-70, E-73.
1. **Build the second E-74 sample NOW** per MANAGER RULING 3 in `TASKS/PY-T3.md` (owner override, Decision 30): `samples/real-layout-fictional/lane-synthetic-01/`, same print-layout family as invoice-34, DISTINCT fictional content, exact self-labels file, README marked fabricated. 2. Re-score E-74 on BOTH rasters (invoice-34 holdout labels untouched per Decision 27; payable scored on the synthetic sample). 3. Then E-70, then E-73 hardening. Est. ~15h.

### PY-B — E-72 vectors + E-71 layouts (repo `Kandoo-Digital-Invoice`)
Owns: E-72 vectors, E-71 layouts.
1. **E-72 round 3 per Decision 29:** flatten `base_invoice` to root, add the 16 `RootRequired` fields at root, move `provenance` to root; the `v1_2_draft_addendum` content (currency_unit, invoice_discount, payments, walk_in_customer) gets a contract decision recorded under PM-30 — the frozen C# `CanonicalInvoiceConsumer.cs` is NOT changed. Tag `o17-E-72-r3`, hand to C#/PHP cross-lane review. 2. E-71 second/third layouts (synthetic, honestly labeled). Est. ~16h.

## MVP-1 countdown (PM-34 — T0 = 2026-11-27; all three must be GREEN on `main`)
- **D3 customer channel** (P-36 + customer mode C-92 + P-31 accepted): **target 2026-11-29** — PHP-A 12h, PHP-B 14h, C-AND-CUST 10h.
- **D1 DoD-A Windows kit** (installer + first-run per the board, Windows v2 screens, local end-to-end with the outbox SMS line + customer web page over the LAN, `demo-check.ps1`): **target 2026-11-30** — C-WIN-A 18h, C-WIN-B 16h, C-PLAT 14h (first 8h critical), C-HOLOO support.
- **D2 Android store APK** (C-91 + C-97: pairing, products, invoice issue, review queue, invoices list): **target 2026-12-01** — C-AND-STORE 18h (+ C-PLAT APK pipeline; PY-A/PY-B run parallel on their own repo).

## Trigger sentences (the OWNER pastes ONE per platform session to start each agent)
- C-PLAT: `You are agent C-PLAT on majid-ghanbari-dost/Kandoo. Read TASKS/MVP1-TEAM.md on main and execute your section (C-PLAT) in order. Own only the paths listed there.`
- C-WIN-A: `You are agent C-WIN-A on majid-ghanbari-dost/Kandoo. Read TASKS/MVP1-TEAM.md on main and execute your section (C-WIN-A) in order. Own only the paths listed there.`
- C-WIN-B: `You are agent C-WIN-B on majid-ghanbari-dost/Kandoo. Read TASKS/MVP1-TEAM.md on main and execute your section (C-WIN-B) in order. Own only the paths listed there.`
- C-AND-STORE: `You are agent C-AND-STORE on majid-ghanbari-dost/Kandoo. Read TASKS/MVP1-TEAM.md on main and execute your section (C-AND-STORE) in order. Own only the paths listed there.`
- C-AND-CUST: `You are agent C-AND-CUST on majid-ghanbari-dost/Kandoo. Read TASKS/MVP1-TEAM.md on main and execute your section (C-AND-CUST) in order. Own only the paths listed there.`
- C-HOLOO: `You are agent C-HOLOO on majid-ghanbari-dost/Kandoo. Read TASKS/MVP1-TEAM.md on main and execute your section (C-HOLOO) in order. Own only the paths listed there.`
- PHP-A: `You are agent PHP-A on majid-ghanbari-dost/kandoo-cloud-php. Read TASKS/MVP1-TEAM.md on main of majid-ghanbari-dost/Kandoo (public) and execute your section (PHP-A) in order. Own only the paths listed there.`
- PHP-B: `You are agent PHP-B on majid-ghanbari-dost/kandoo-cloud-php. Read TASKS/MVP1-TEAM.md on main of majid-ghanbari-dost/Kandoo (public) and execute your section (PHP-B) in order. Own only the paths listed there.`
- PY-A: `You are agent PY-A on majid-ghanbari-dost/Kandoo-Digital-Invoice. Read TASKS/MVP1-TEAM.md on main of majid-ghanbari-dost/Kandoo (public) and execute your section (PY-A) in order. Own only the paths listed there.`
- PY-B: `You are agent PY-B on majid-ghanbari-dost/Kandoo-Digital-Invoice. Read TASKS/MVP1-TEAM.md on main of majid-ghanbari-dost/Kandoo (public) and execute your section (PY-B) in order. Own only the paths listed there.`

Priority if the platform caps concurrent sessions: C-PLAT, C-WIN-A, PHP-A, PY-A, C-WIN-B, C-AND-STORE, PHP-B, PY-B, C-AND-CUST, C-HOLOO.

---
**Status:** DISPATCHED 2026-11-27 (exec-manager) — 10 agents defined, path-ownership map in force, PM-30/31/32/33/34 recorded as Decisions 31-36 in `docs/decisions-made.md` (commit `26a8922f`). Sessions start on the owner's triggers. Daily update: `docs/STATUS.md` (C-PLAT) + one short R per day.
