# [AGENT-TASK CSHARP-T3] C# lane (ONE agent on this repo) — M-010/M-011 sprint chain

**Lane:** C# (`majid-ghanbari-dost/Kandoo`) — **Posted by:** exec-manager (2026-11-25, Asia/Tehran)
**Supersedes:** `TASKS/CSHARP-T3A.md` + `TASKS/CSHARP-T3B.md` (both deleted 2026-11-25). OWNER RULING: parallelism is ACROSS repos only (C# / PHP / Python = one agent per repo) — peer agents on ONE repo do NOT run in parallel. The former Track A / Track B stages below therefore run as ONE sequential chain in the order given.
**Source orders:** M-010 + M-011 (`docs/messages/` on `main` of this repo). Manager answer: R-010 in `docs/messages/MANAGER-TO-PM.md` on `arena/order-15-status`; post-merge R-013; owner override R-014 (`docs/messages/R-014.md`) on the same branch.
**Reporting (GitHub only):** update the `## Status` line in THIS file + report comment on the PR you open. One short line per green stage (branch, tag, run URL). Never merge/close — PM merges (PO merges workflow-touching PRs).

## Branching (single-lane discipline)
- Branch off the integration tip `e6cecc3` on `arena/4bb6cd51-kandoo`; branch name `arena/<task>` (e.g. `arena/c98-demo-check`).
- CI runs on push; a stage is green ONLY per `docs/OPERATING-RULES-v2.md` (CI green + artifact + smoke).
- ~~Open the create-only PR with base `main` ONLY AFTER PR #21 is merged (orphan-`main` trap — see R-010 item 6).~~ **PR #21 IS MERGED (2026-11-26, merge commit `5c018fe0`) — create-only PRs into `main` are now ordinary; open them as soon as a stage is green.**
- Screenshots = CI artifacts; no success claim without an opened CI run URL.

## MANAGER UPDATE (2026-11-27) — PR #21 MERGED; owner speed order; boundaries lifted
1. **PR #21 is MERGED into `main`** (merge commit `5c018fe0`, head `3301cd33`, manager-verified 2026-11-26): the merge-main chain item is DONE and the orphan-`main` trap is structurally gone (PM-23 freeze lifted by the completed merge).
2. **OWNER DIRECT ORDER (2026-11-27, Decision 30 in `docs/decisions-made.md`, R-014): every boundary built so far is removed until project delivery — move forward with speed only, no idle gaps between stages.**
3. **PM-06/PM-17 is LIFTED by the owner's certification** (all project data is fictional): the lane may generate fictional test/demo data freely, honestly marked synthetic/fictional. `git fetch --unshallow` before diagnosing history (R-011 item 4 stays).
4. **Chain per M-012 Q3 (resumes NOW):** (2) **C-99** `GET /v1/stores/{id}/invoices` — `limit` + `next_cursor` (same pattern as products), list projection (number, customer with masked mobile, date, amount, status draft/issued/cancelled, `sms_status` sent/failed/none joined from the SMS delivery record), filters `status` + `sms_status` as query parameters, "needs review" chip hidden until Mode B review state exists, tests included; the Invoices screen is built ONCE against it. (3) **Setup screen per Q-C98-02=B** — board steps 1-3 (store info / manager-number verification by SMS code / backup path+schedule); step 2 uses the DEMO SMS outbox code with the Persian on-screen demo note; automatic Hub start / database / owner-session runs in the BACKGROUND after step 1 with a progress indicator; first user becomes manager automatically; Group 1 stays PARTIAL until this is built. (4) C-96 group 2 screens on top of C-99 (Invoices list/detail), then group 3 (Products, Customers). (5) group 4 + Settings/Devices. DoD-A hotfixes from PO test reports still outrank everything. Q-C98-01=A: the lane writes the outbox link `http://<LAN-IP>:<cloud-port>/i/<token>` and updates `scripts/demo-check.ps1` as proposed; the PHP lane serves `/i/<token>`.

## Stage 1 — Integration PR (M-009) — DONE-GREEN (2026-11-25, manager-verified)
PR #21 open, gate green (runs 45603452519 / 45603453470; gate runs 45600084428 / 45600084435, `SMOKE_ALL_HARD_CHECKS_PASSED`, artifacts `kandoo-hub-c87-setup-exe` + `kandoo-install-smoke`). PO merges it (workflow-touching). Do NOT touch PR #21 or the smoke script. **[2026-11-27: MERGED — done.]**

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

## Constraints (updated 2026-11-27)
- PM-06/PM-17 data rule is LIFTED by the owner's 2026-11-27 certification (all project data is fictional) — generated data must be honestly marked synthetic/fictional. `[؟]` items are NOT built; file `docs/questions/Q-<id>.md` with options + default.
- No local dotnet — CI is the verifier. NEVER edit `scripts/Invoke-InstallerSmoke.ps1` / `packaging/HubPilot/KandooHub.Installer.iss`.
- Hotfix cycles from the PO test reports outrank new work (M-010).

---
**Status:** all green stages below, one line each (branch `arena/4bb6cd51-kandoo` throughout; nothing merged).

- **Stage 1** — DONE-GREEN (pre-existing): PR #21, runs 45603452519 / 45603453470. **[2026-11-27: PR #21 MERGED by the owner — merge commit `5c018fe0`. DONE.]**
- **Stage 2** — GREEN: tag `o17-C-98`, run https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/45626612556 — `scripts/demo-check.ps1` + Persian `RUN-LOCAL.md`, `C98_RESULT PASS_WITH_GAPS passed=6`.
- **Stage 2b** — GREEN: tag `o17-C-98-SMS`, run https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/45629840285 — demo SMS wired into the Hub, point 7 now PASS, `passed=7`.
- **Stage 3** — GREEN CI, **PARTIAL** screen match: tag `o17-C-96-G1V`, run https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/45634326798 — role-selection copy matches its board; Setup does not (Q-C98-02); screenshots 1/10 distinct.
- **Stage 4** — GREEN: tag `o17-C-96-G2V`, run https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/45638750569 — report `docs/reports/ORDER-17-C-96-G2V.md`; fixed the app↔Hub invoice wire contract (three defects, issuing an invoice from the app was impossible) with tests. Screen match PARTIAL (Q-C98-03: no invoices read route).

- **Merge of main's second orphan snapshot** — `da4ecb7`, run https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/45642357019 (ubuntu + windows success; `Kandoo App` 45642357122 green) — PR #21 back to `MERGEABLE`. 27 conflicts: ours for code/CI/tests, PM log from main, smoke script from `pm/order21-selfcheck` (byte-identical). `main`-only files missing afterwards = 0. [R-011 item 4: the "orphan snapshot" reading was a platform-sync artifact — `main` was never rewritten.]

**Decisions owed by PM before further screen work:** ~~Q-C98-01 / Q-C98-02 / Q-C98-03~~ — ALL ANSWERED by M-012 Q3 (2026-11-26): A / B / A-as-C-99. Nothing owed; the chain in MANAGER UPDATE governs.

**Open GAP — resolved by M-012 Q3 (Q-C98-01=A):** the public invoice page `/i/{token}` is served by the PHP Cloud in the local kit (P-36); the Hub's outbox SMS line carries `http://<LAN-IP>:<cloud-port>/i/<token>`; the C# lane writes the link (MANAGER UPDATE item 4). Fallback C only if A is not working by T+30h, announced before switching.

**MANAGER UPDATE 2026-11-27:** PR #21 MERGED (`5c018fe0`); merge-main fix DONE; next: C-99 -> Setup (Q-C98-02=B) -> C-96 G2/G3 -> G4 + Settings/Devices; owner speed order in force (Decision 30 / R-014); create-only PRs into `main` now ordinary.