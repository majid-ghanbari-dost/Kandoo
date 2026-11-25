# [AGENT-TASK CSHARP-T3A] C# Track A (platform & release) — M-010/M-011 sprint chain

**Lane:** C# Track A (dedicated parallel session) — **Posted by:** exec-manager (2026-11-25, Asia/Tehran)
**Source orders:** M-010 + M-011 (`docs/messages/` on `main` of this repo). Manager answer: R-010 in `docs/messages/MANAGER-TO-PM.md` on `arena/order-15-status`.
**Reporting (GitHub only):** update the `## Status` line in THIS file + report comment on the PR you open. One short line per green stage (branch, tag, run URL). Never merge/close — PM merges (PO merges workflow-touching PRs).

## Branching (parallel-track discipline)
- Branch off the integration tip `e6cecc3` on `arena/4bb6cd51-kandoo`; branch name `arena/trackA-<task>`.
- CI runs on push; a stage is green ONLY per `docs/OPERATING-RULES-v2.md` (CI green + artifact + smoke).
- Open the create-only PR with base `main` ONLY AFTER PR #21 is merged (orphan-`main` trap — see R-010 item 6). Until then: push, verify CI, report green stages.
- FILE OWNERSHIP (no overlap with Track B): Track A owns `scripts/`, `RUN-LOCAL.md`, `packaging/`, Settings + Devices screens. Track B owns C-96 verification screenshots/reports and group fixes outside that list.

## Stage 1 — Integration PR (M-009) — DONE-GREEN (2026-11-25, manager-verified)
PR #21 open, gate green (runs 45603452519 / 45603453470; gate runs 45600084428 / 45600084435, `SMOKE_ALL_HARD_CHECKS_PASSED`, artifacts `kandoo-hub-c87-setup-exe` + `kandoo-install-smoke`). PO merges it (workflow-touching). Track A does NOT touch PR #21 or the smoke script.

## Stage 2 — C-98 skeleton (T0–T+8h) — START IMMEDIATELY (M-011: agents never idle)
1. `scripts/demo-check.ps1` covering the automatable subset of the demo chain: install -> first-run setup -> create a product -> issue an invoice (Mode A) -> SMS line in the local outbox -> customer web page opens from the outbox link. PASS/FAIL per point.
2. `RUN-LOCAL.md` — PERSIAN, copy-paste, covering DoD-A points 1–5 of M-010 §3 (incl. `first-run-selfcheck` and the LAN phone step).
3. Automatable subset wired into CI on your branch; if `.github/workflows/` changes, FIRST LINE of the PR body must declare it (PO merges).
4. Green = CI green + headless steps proven in CI + honest list of what stays PO-run (Holoo read-only import on `HolooCopy`; capture folder -> review item). Tag `o17-C-98` on the green commit.

## Stage 3 — Windows Settings + Devices screens v2 (pairing, revoke), screenshots
Boards `docs/design/Settings.dc.html` + `docs/design/Devices.dc.html` (read `docs/design/README.md` first); behaviour from `docs/product-ux-spec.md`. Screenshot artifact per screen next to its board + report line per screen (matches / differs and why). Tag `o17-C-96-SD`.

## Stage 4 — Hardening from the PO test reports (T+24h / T+44h)
Fixes only from PO findings relayed by the PM; a DoD-A fix always outranks a later stage.

## Stage 5 (pre-approved; NOT DoD-A scope)
C-90 pilot items (replication retry/backoff, idempotency tests), C-94 follow-ups.

## Out of sprint (M-010)
Android APK (C-97), accounting, Holoo live, SMS real, hosting, any new scope. Outside items -> `docs/questions/Q-<id>.md`.

## Constraints (unchanged)
PM-06/PM-17 data rule. No local dotnet — CI is the verifier. NEVER edit `scripts/Invoke-InstallerSmoke.ps1` / `packaging/HubPilot/KandooHub.Installer.iss`.

---
**Status:** DISPATCHED (T0 2026-11-25) — stage 1 DONE-GREEN (PR #21, PO merges); start stage 2 immediately.
