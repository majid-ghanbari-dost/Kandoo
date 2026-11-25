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

## Manager addendum (2026-11-24, Asia/Tehran) — exec-manager review of the lane report

Report read from `AGENT_HANDOFF.md` (ORDER 16 section) on `arena/4bb6cd51-kandoo` @ `7e55bf6` (docs commits `1684ae05`, `0a5b8606`, `7e55bf62`). Manager verification results:

- Phase 1 HOTFIX-1: commit `207ebeb` on `arena/4bb6cd51-kandoo`; tag `o16-HOTFIX-1` manager-VERIFIED pointing at `207ebebd0a5c87675b9e95af451a0c2a37eb8a49`. Content matches M-007 §1 (HubLocator with both layouts + full checked-path list, `--self-check [resultFile]` contract, HubLocatorTests). CI results NOT yet manager-verified — report run URLs; fix any red check on the same branch.
- DEVIATION RULING (Phase 1 item 5): the fix landed ONLY on `arena/4bb6cd51-kandoo`, NOT on `arena/kandoo-c95-maui-launcher` and NOT merged into the stacked PR branches (c90, c91, c92). PR #12–#15 heads therefore do NOT contain HOTFIX-1; the MERGE FREEZE on #12–#15 STAYS in force. The lane MUST propagate `207ebeb` into `arena/kandoo-c95-maui-launcher`, `arena/kandoo-c90-windows-store`, `arena/kandoo-c91-android-store`, `arena/kandoo-c92-customer-mode` and push, OR file the exact reason in AGENT_HANDOFF — PM decides any merge of #12–#15 only after that.
- Phase 2 DEVIATION: the consolidation was committed on `arena/4bb6cd51-kandoo` (`8eb849b`), which carries the whole ORDER 15/16 cumulative diff — a PR from that branch would NOT be C-93-only. The ONE consolidated PR must come from a DEDICATED branch off current `main` (e.g. `arena/kandoo-c93-consolidated`) containing exactly PR #16 content + fix-2 + fix-3; open it only AFTER CI green, with the first-line workflows declaration; PM merges that single PR, then PM closes #16/#18/#19.
- Phase 3: commit `6aea923` + docs `7e55bf62`; tag `o16-C-96-1` manager-VERIFIED at `6aea923b3aa116e887d69768167472e6b2386a58`. WARNING: tagging BEFORE CI green is a recorded process violation — per-group acceptance (CI green + per-screen screenshot artifact + per-screen report line) is NOT met yet, and D2 requires one create-only PR per group from its own branch. No new tag before green; never move existing ones.
- NEW cross-lane item (from the PY-T2 report on PR #16): review `tests/golden/e72/canonical-invoice-v1-2-draft-vector.json` (Kandoo-Digital-Invoice @ `1e620cc`) for compatibility and record the C# verdict in `docs/reports/` + AGENT_HANDOFF with a link.

#### E-72 cross-lane review — DONE (second verdict INCOMPATIBLE; fix owned by the Python lane)

Recorded on `arena/4bb6cd51-kandoo`. Two rounds completed: `1e620cc` (NOT compatible) and `o17-E-72` @ `1e029bd` (**INCOMPATIBLE**).

C# is fail-fast, so it emits exactly ONE error, not a list: `SchemaValidationFailed | Unexpected field: base_invoice.` Three defect categories: (1) `base_invoice` wrapper instead of flat root; (2) all **16** `RootRequired` fields absent at root; (3) 4 unknown root fields. `v1_2_draft_addendum` carries v1.2-only content with no v0 home and needs a separate contract decision.

PHP's objection is **UPHELD in part**: the round-1 duplication claim was wrong — `image_sha256`/`template_library_id` occur exactly once each (byte offsets 955/975) and only inside `provenance`. PHP's conclusion is **REJECTED**: C# is not wrong about field location; `provenance` must move from inside `base_invoice` to root.

Decisive test: promoting `base_invoice` to root makes the vector pass every C# validation step.

Reports: `docs/reports/E72-CANONICAL-INVOICE-V1-2-O17-CSHARP-COMPARISON-FA.md`, decision item 26 in `docs/decisions-made.md`, C# review 6039821259 on PR #16. **STANDBY for round 3** after the Python lane's new tag. Cross-repo comment blocked by HTTP 403 (read-only access on `Kandoo-Digital-Invoice`); text preserved at `docs/reports/E72-PY-PR16-CLOSING-COMMENT.md`.

## Manager addendum 2 (2026-11-25, Asia/Tehran) — PM-22: ONE integration PR (answers M-009 §1; supersedes the R-008 addendum points 1–2 and the Phase 2 dedicated-branch plan)

Source: `docs/messages/M-009.md` on `main` @ `cb1730aa`. Manager pre-verification (read-only git + REST, 2026-11-25):

- `arena/4bb6cd51-kandoo` tip: `dca2e1ebbd0c8c3809edae7dece8255a5e935cfc` ("E-72 close-out: mark cross-lane review done, standby for round 3").
- `git merge-base --is-ancestor` vs tip — ALL FIVE M-009 §1.1 heads are MISSING (their content was re-committed on this branch instead of merged; merge conflicts are expected and are resolved per the policy below):
  - `arena/kandoo-c95-maui-launcher` @ `c76046aa4ba89e72fe0673b5a872a3c6fbbc372d`
  - `arena/kandoo-c90-windows-store` @ `15aadc62bccf9490c093085f1eb97ab4e3382477`
  - `arena/kandoo-c91-android-store` @ `39bca968666790cfa902d42c344b3299489c3db5`
  - `arena/kandoo-c92-customer-mode` @ `992b0a0110db006c38e4ff49af5d95612431e25b`
  - `arena/kandoo-c93-holoo-connector` @ `e73b1c91523e86cad40628bb195797c5aadfb87a` (= PR #16 head)
  - (`arena/kandoo-c93-consolidated` @ `649ccb03` is also unmerged; not required by M-009 — merge only if its content differs from what this branch already holds.)
  - `pm/order21-selfcheck` @ `f4debb2453611cbe01b0c959e321794410237495` — MISSING, must be merged (adds CI check `first-run-selfcheck` to `scripts/Invoke-InstallerSmoke.ps1`).
- Manager-verified CI on the two M-009 §4 evidence commits — NOT fully green; fix on this branch BEFORE opening the integration PR and attach the verbatim error line of each failed job to the lane report:
  - `207ebebd0a5c87675b9e95af451a0c2a37eb8a49` (HOTFIX-1): RED — `App tests + Windows + Android` = failure (runs 45346666579 / 45346666591), `Build and test (windows-latest)` = failure; GREEN — `MAUI Windows + Android`, `Avalonia Windows + Android`, `Synthetic C-52` (ubuntu+windows), `Build and test (ubuntu-latest)` (runs 45346674379 / 45346675893 / 45346674417).
  - `6aea923b3aa116e887d69768167472e6b2386a58` (C-96 g1–4): RED — `App tests + Windows + Android` = failure (at least one push run, e.g. 45347940756 / 45347942357); GREEN — latest `Build and test` (ubuntu+windows), `MAUI Windows + Android`, `Avalonia Windows + Android`, `Synthetic C-52` (runs 45347937469 / 45347937473 / 45347937495 / 45347942332 / 45347942360).

Execution order (M-009 §1):
1. Merge every MISSING head above + `origin/pm/order21-selfcheck` into `arena/4bb6cd51-kandoo`. Conflict policy: this branch wins for code/CI/tests; `packaging/HubPilot/KandooHub.Installer.iss` and `scripts/Invoke-InstallerSmoke.ps1` come from `pm/order21-selfcheck`. Do NOT edit the smoke script: `first-run-selfcheck` runs `<install>\app\Kandoo.App.exe --self-check <file>` against the INSTALLED app and expects exit 0 + first line starting `KANDOO_SETUP_OK`; if it fails, fix the APP, or report the verbatim failing line to the PM.
2. Open ONE PR, base `main`, from `arena/4bb6cd51-kandoo`: title `O16 integration (C-90..C-96 g1-4, C-93, HOTFIX-1)`; FIRST LINE of the body: `This PR changes .github/workflows/*`; body lists each task with tag, full SHA and CI run URL. Gate: CI green on both jobs with `SMOKE_ALL_HARD_CHECKS_PASSED` and artifacts `kandoo-hub-c87-setup-exe` + `kandoo-install-smoke`.
3. PM merges (PO notified — workflow-touching). After the PM confirms the merge, the PM/manager closes #12, #13, #14, #15, #16, #18, #19 as superseded (comment with the integration PR number); the lane does NOT close/merge. The freeze on them stays until then.
4. Then continue C-96 remaining groups, C-97, C-98 (Phase 3 / Queue unchanged).

---
**Status:** PM-22 EXECUTED (2026-11-25) — integration PR **#21** open for PM merge, CI green on tip `db19e75`.

- **Merges:** all five MISSING heads (`c95` `c76046a`, `c90` `15aadc6`, `c91` `39bca96`, `c92` `992b0a0`, `c93-holoo-connector` `e73b1c9`) plus `pm/order21-selfcheck` `f4debb2` are ancestors of `arena/4bb6cd51-kandoo` @ `db19e75`. Conflict policy applied: branch wins for code/CI/tests; `Invoke-InstallerSmoke.ps1` + `KandooHub.Installer.iss` taken verbatim from `pm/order21-selfcheck` and verified identical to it. Smoke script not edited.
- **Failed job fixed:** `App tests + Windows + Android` / `Publish self-contained Windows app and zip` was red from four compile errors (CS0017 duplicate Main, CS0103 `HubLocator`, CS0234 `StringContent`, CS0103 `hubDir`), then three further chained ones (MSB4025 XML comment, CS0029 lambda shadowing, CS1729 `ReturnsPage`, XFC0009 `BorderBrush`). Verbatim messages and fixes: `docs/reports/ORDER-16-PM22-INTEGRATION.md`.
- **Also fixed:** `duplicate column name: server_key_issued_at` in the Windows Hub tests (concurrent `EnsureColumn`), and the branch's `ci.yml` had lost main's installer-smoke gate — restored as `main + C-93 Holoo steps`.
- **CI:** `932b17c` green on both jobs, run [45571592358](https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/45571592358). Gate met: `SMOKE_ALL_HARD_CHECKS_PASSED`, artifacts `kandoo-hub-c87-setup-exe` (89,838,429 B) and `kandoo-install-smoke` (22,918 B).
- **Next:** PM merges the ONE integration PR, then closes #12/#13/#14/#15/#16/#18/#19. Lane does not merge or close. E-72 round-3 standby unchanged.
