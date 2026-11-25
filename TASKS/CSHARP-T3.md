# [AGENT-TASK CSHARP-T3] C# lane (ONE agent on this repo) — M-010/M-011 sprint chain

**Lane:** C# (`majid-ghanbari-dost/Kandoo`) — **Posted by:** exec-manager (2026-11-25, Asia/Tehran)
**Supersedes:** `TASKS/CSHARP-T3A.md` + `TASKS/CSHARP-T3B.md` (both deleted 2026-11-25). OWNER RULING: parallelism is ACROSS repos only (C# / PHP / Python = one agent per repo) — peer agents on ONE repo do NOT run in parallel. The former Track A / Track B stages below therefore run as ONE sequential chain in the order given.
**Source orders:** M-010 + M-011 (`docs/messages/` on `main` of this repo). Manager answer: R-010 in `docs/messages/MANAGER-TO-PM.md` on `arena/order-15-status`.
**Reporting (GitHub only):** update the `## Status` line in THIS file + report comment on the PR you open. One short line per green stage (branch, tag, run URL). Never merge/close — PM merges (PO merges workflow-touching PRs).

## Branching (single-lane discipline)
- Branch off the integration tip `e6cecc3` on `arena/4bb6cd51-kandoo`; branch name `arena/<task>` (e.g. `arena/c98-demo-check`).
- CI runs on push; a stage is green ONLY per `docs/OPERATING-RULES-v2.md` (CI green + artifact + smoke).
- Open the create-only PR with base `main` ONLY AFTER PR #21 is merged (orphan-`main` trap — see R-010 item 6). Until then: push, verify CI, report green stages.
- Screenshots = CI artifacts; no success claim without an opened CI run URL.

## Stage 1 — Integration PR (M-009) — DONE-GREEN (2026-11-25, manager-verified)
PR #21 open, gate green (runs 45603452519 / 45603453470; gate runs 45600084428 / 45600084435, `SMOKE_ALL_HARD_CHECKS_PASSED`, artifacts `kandoo-hub-c87-setup-exe` + `kandoo-install-smoke`). PO merges it (workflow-touching). Do NOT touch PR #21 or the smoke script.

## Stage 2 — C-98 skeleton (T0–T+8h) — START IMMEDIATELY (M-011: agents never idle)
1. `scripts/demo-check.ps1` covering the automatable subset of the demo chain: install -> first-run setup -> create a product -> issue an invoice (Mode A) -> SMS line in the local outbox -> customer web page opens from the outbox link. PASS/FAIL per point.
2. `RUN-LOCAL.md` — PERSIAN, copy-paste, covering DoD-A points 1–5 of M-010 §3 (incl. `first-run-selfcheck` and the LAN phone step).
3. Automatable subset wired into CI on your branch; if `.github/workflows/` changes, FIRST LINE of the PR body must declare it (PO merges).
4. Green = CI green + headless steps proven in CI + honest list of what stays PO-run (Holoo read-only import on `HolooCopy`; capture folder -> review item). Tag `o17-C-98` on the green commit.

## Stage 3 — C-96 group 1 verify/fix (~T+8h–T+14h)
First-run Setup (`docs/design/Setup.dc.html`, 3 steps) + role selection (`docs/design/Role-Select-v2.dc.html`) vs boards. Read `docs/design/README.md` FIRST, then behaviour from `docs/product-ux-spec.md`. Screenshot artifact per screen next to its board + report line per screen (matches / differs and why). Fixes go on your own branch. Tag `o17-C-96-G1V` when CI green.

## Stage 4 — C-96 group 2 verify/fix (~T+14h–T+20h)
Review queue (`Review.dc.html`), Invoice issue (`Invoice.dc.html`), Invoices list/detail (`Invoices.dc.html`). Same acceptance as stage 3. Tag `o17-C-96-G2V`.

## Stage 5 — C-96 group 3 verify/fix (~T+20h–T+26h)
Product create/edit/list (`Product.dc.html`, `Product-Edit.dc.html`, `Products.dc.html`; plus/minus rows, 3 mandatory fields name/price/stock, auto numeric tag) + Customers (`Customers.dc.html`). Same acceptance. Tag `o17-C-96-G3V`.

## Stage 6 — C-96 group 4 verify/fix (~T+26h–T+30h)
Return (`Return.dc.html`), Empty/Error States (`States.dc.html`). Same acceptance. Tag `o17-C-96-G4V`.

## Stage 7 — Windows Settings + Devices screens v2 (pairing, revoke), screenshots (~T+30h–T+36h)
Boards `docs/design/Settings.dc.html` + `docs/design/Devices.dc.html`; behaviour from `docs/product-ux-spec.md`. Screenshot artifact per screen next to its board + report line per screen (matches / differs and why). Tag `o17-C-96-SD`.

## Stage 8 — Hardening from the PO test reports (T+24h / T+44h)
Fixes only from PO findings relayed by the PM; a DoD-A fix ALWAYS outranks the next stage — interrupt the chain anywhere for it.

## Stage 9 (pre-approved; NOT DoD-A scope)
C-90 pilot items (replication retry/backoff, idempotency tests), C-94 follow-ups, C-97 phone screens v2 (`Phone-*.dc.html` boards) and C-92 customer app on top.

## Out of sprint (M-010)
Android APK release build (C-97 APK), accounting, Holoo live, SMS real, hosting, any new scope. Outside items -> `docs/questions/Q-<id>.md`.

## Constraints (unchanged)
- PM-06/PM-17 data rule. `[؟]` items are NOT built; file `docs/questions/Q-<id>.md` with options + default.
- No local dotnet — CI is the verifier. NEVER edit `scripts/Invoke-InstallerSmoke.ps1` / `packaging/HubPilot/KandooHub.Installer.iss`.
- Hotfix cycles from the PO test reports outrank new work (M-010).

---
**Status:** DISPATCHED (T0 2026-11-25) — ONE C# agent; stage 1 DONE-GREEN (PR #21, PO merges); start stage 2 immediately.

**Stage 2 — IN PROGRESS (not yet green).** `scripts/demo-check.ps1` + Persian `RUN-LOCAL.md` written, wired into CI; awaiting the CI run URL. Stage 2 is NOT green: two demo points are product gaps (SMS outbox, customer link), reported as GAP by the script.
