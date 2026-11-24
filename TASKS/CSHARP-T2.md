# [AGENT-TASK CSHARP-T2] HOTFIX-1 (BLOCKING) → consolidated C-93 PR → C-96 screen groups 1–4

**Lane:** C# (`majid-ghanbari-dost/Kandoo`) — **Posted by:** exec-manager (2026-11-24, Asia/Tehran)
**Source orders:** M-006, M-007, M-008 (`docs/messages/` on `main`). Answers filed as R-007/R-008 in `docs/messages/MANAGER-TO-PM.md` on `arena/order-15-status`.
**Reporting (mandatory, GitHub only):** update the `## Status` line in THIS file + report comment on each PR you open. No merge actions by the agent — PM/PO merge.

## Phase 1 — HOTFIX-1 (BLOCKING, first)
Cause (M-007): since C-95 the process is `{install}\app\Kandoo.App.exe` but `SetupWizardPage.ConfigureShopAsync` still looks for `{appDir}\hub\Kandoo.Hub.exe` → PO error "فایل Kandoo.Hub.exe در پوشه نصب یافت نشد."

1. Extract path resolution into a small class (e.g. `HubLocator`): try `..\hub\Kandoo.Hub.exe` relative to the app folder, then `.\hub\Kandoo.Hub.exe` (dev layouts). On failure the error message lists EVERY path checked.
2. Unit tests: both layouts + the not-found message.
3. Sweep the whole app for other `Environment.ProcessPath` / `AppContext.BaseDirectory` uses that assume the old launcher location; fix the same way.
4. Headless self-check `Kandoo.App.exe --self-check [resultFile]` — runs BEFORE any UI: resolve Hub, start on a free port with bootstrap proof, perform the owner-session call, print `KANDOO_SETUP_OK port=<n>` to stdout AND to resultFile (2nd arg, if given), stop Hub, exit 0. Any failure: print `KANDOO_SETUP_FAIL <reason>`, exit 1. This exact format goes to the PM in R-007.
5. Push on `arena/kandoo-c95-maui-launcher`; then merge that branch into the stacked branches (c90, c91, c92, C-96 prep). Report branch, full SHA, CI run URLs.
6. Tag `o16-HOTFIX-1` on the green commit (D3 casing rule).

**MERGE FREEZE:** PRs #12, #13, #14, #15 stay UNMERGED until HOTFIX-1 + the `--self-check` smoke are green (M-007 §2). Never edit `KandooHub.Installer.iss` / `Invoke-InstallerSmoke.ps1` (PM updates the smoke script to call `--self-check` against the INSTALLED folder).

## Phase 2 — ONE consolidated C-93 PR
Verified 2026-11-24: fix-2 (`e5705c8`, PR #18) and fix-3 (`f1349e9`, PR #19) are SIBLINGS off `58731e7`; fix-3 does NOT contain fix-2. No consolidated head exists.
1. Create one branch off current `main` containing: PR #16 content (`arena/kandoo-c93-holoo-connector` @ `e73b1c9`) + fix-2 + fix-3 (stack them; resolve conflicts; full CI).
2. Open ONE PR, base `main`, title `C-93 consolidated: connector hardening + fix-2 + fix-3`. FIRST LINE of the body must say whether it touches `.github/workflows/` (yes/no + files). This first-line rule is now permanent for every PR that changes workflows.
3. Report; PM merges that single PR; PM then closes #16/#18/#19 as superseded. Agent does NOT close/merge.

## Phase 3 — C-96 Windows screens v2, groups 1–4 (order per M-006 §3)
Sources (verified on `main` @ `cc35715`): `docs/design/` flat folder — read `docs/design/README.md` FIRST, then boards `*.dc.html` + `canvas.json`; behaviour/order from `docs/product-ux-spec.md` (@ `4bb78c2`). Tag meanings: [م] owner's words = mandatory; [ط] build as drawn + flag in report; [؟] do NOT build → file `docs/questions/Q-<id>.md` with options + default.
- Group 1: first-run Setup (3 steps, Windows) + role selection — window title-bar buttons on the RIGHT (see RUN-LOCAL.md regression note); Setup group integrates the HOTFIX-1 path fix; first-run flow must match the Setup board (M-005).
- Group 2: Review queue + Invoice issue + Invoices list/detail.
- Group 3: Product create/edit/list (plus/minus field rows between fields; 3 mandatory fields name/price/stock; numeric tag auto-assigned when empty; green submit button).
- Group 4: Customers, Return, Settings, Devices, States.
- Acceptance per group: CI green + screenshot artifact of each screen next to its board + report line per screen (matches / differs and why) + tag `o16-C-96-<group>` (PM-15 casing).
- Branching per D2: one create-only PR per group from own branch; PRs touching `.github/workflows/` declare it in the first line.

## Queue (dispatched later by the manager)
C-97 phone screens v2 (Android store + customer, boards Role-Select-v2 … Customer-Locked; screenshot per screen; report APK name+size), C-98 `scripts/demo-check.ps1` + Persian `RUN-LOCAL.md` walking the 9 demo points (PASS/FAIL per automatable point).

## Constraints
- PM-06/PM-17: synthetic fixtures only; real-layout-fictional samples allowed per M-008; real customer data NEVER in git/CI/reports.
- No local dotnet — CI is the final verification. PM-16 emulator job stays BLOCKED/continue-on-error.
- Do NOT merge. Do NOT move/delete tags. One big task at a time; ORDER 17 items start after the current MUST is green and reported.

---
**Status:** OPEN (Phase 1 BLOCKING today; PM trigger required to start)
