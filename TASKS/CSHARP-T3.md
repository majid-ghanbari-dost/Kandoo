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
**DONE — PARTIAL (green CI).** branch `arena/4bb6cd51-kandoo`, tag `o17-C-96-G1V`, run https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/45634326798 (CI ubuntu+windows success). Report: `docs/reports/ORDER-17-C-96-G1V.md`. **Role selection** now matches `Role-Select-v2.dc.html` on every text item (title, heading, three role names, footer line) — fixed. **Setup** does NOT match `Setup.dc.html`: the board's step 2 is manager name + mobile + SMS code, the built step 2 starts the local Hub, and real SMS is out of sprint — framing it as green would require relabelling a screen to describe work it does not do, so it is `docs/questions/Q-C98-02.md` (A/B/C/D). **Screenshots: 1/10 distinct** — the capture script has no UI automation, so it can only see the launch screen; the artifact states `SCREENS_DISTINCT=1/10` with a warning instead of shipping ten copies of one frame (which is what the first attempt did, all exactly 197,361 B).

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
**Status:** DISPATCHED (T0 2026-11-25) — ONE C# agent. Stage 1 DONE-GREEN (PR #21, PO merges). Stage 2 GREEN/PARTIAL (`o17-C-98`, run 45626612556) + stage 2b demo-SMS fix GREEN (`o17-C-98-SMS`, run 45629840285). Stage 3 PARTIAL (`o17-C-96-G1V`, run 45634326798). Two decisions owed by PM: `Q-C98-01` (who serves the customer link) and `Q-C98-02` (Setup board vs SMS verification).

**Stage 2 — GREEN (framework) / PARTIAL (demo chain).** branch `arena/4bb6cd51-kandoo`, tag `o17-C-98`, run https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/45626612556 — `C98_RESULT PASS_WITH_GAPS passed=6 failed=0 gaps=1`, points 1-6 PASS in CI, artifact `kandoo-c98-demo-check`. PARTIAL because DoD-A point 3 is not demonstrable: point 7 `GAP(sms-outbox)` — `Kandoo.Hub` never calls `DemoSmsProvider.Send()`, so no outbox line and therefore no customer link (point 8 SKIP). Fixing that next, before Stage 3, per "a DoD-A fix ALWAYS outranks the next stage".

**Stage 2b — demo SMS FIXED, green.** branch `arena/4bb6cd51-kandoo`, tag `o17-C-98-SMS`, run https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/45629840285 (ubuntu + windows both success) — `C98_RESULT PASS_WITH_GAPS passed=7 failed=0 gaps=1`, point 7 now `PASS(sms-outbox) SMS line present (display_number=1)`. The Hub writes the line on invoice.issue (`IHubStore.SendDemoSms`, outbox at `Hub:Demo:OutboxDirectory`, default `%LOCALAPPDATA%\outbox`, the path the Windows app already reads) and a failed write can never fail a real sale. Two CI failures on the way, both fixed and recorded: stray brace (45628579421), xUnit one-public-constructor (45628948538).

**Open GAP — needs a PM decision, not lane work:** point 8 `GAP(customer-link)` — the public invoice page `/i/{token}` is served by the Cloud service (P-36, PHP lane); the Hub has no such route and the issue path produces no link. Building a Hub-side copy would invent scope and fork the architecture, so it is filed as `docs/questions/Q-C98-01.md` instead of being built.
