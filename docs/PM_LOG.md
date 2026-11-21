# KANDOO — PM LOG (living document, append-only decisions)

Purpose: the permanent management handoff. Any new PM chat or agent reads, in this order:
1. `docs/PM_LOG.md` (this file: management decisions and status since 2026-11-16)
2. `docs/OPERATING-RULES-v2.md` (working rules for all agents)
3. `docs/MASTER_HANDOFF.md` and `docs/DECISIONS.md` (architecture, D1-D14 and amendments; the status sections of MASTER_HANDOFF are stale, dated 2026-10-22, trust this log and `docs/reports/` for status)
4. `docs/orders/` (current orders) and `docs/reports/` (agent reports)

Rule: append new entries at the bottom of the decision log; never rewrite old entries (add a superseding entry instead).

## Roles and channels
- Product owner (PO): the human owner. Carries messages between the PM chat and the Execution Manager. Final say on real-data access, legal text, SMS provider, production host.
- PM: Claude (claude.ai chat). Reads and writes GitHub through the Custom `github` connector (read repos, create branches, write ordinary files, merge PRs with PO approval). The connector CANNOT write `.github/workflows/`. The PM owns `scripts/Invoke-InstallerSmoke.ps1` and `packaging/HubPilot/KandooHub.Installer.iss`.
- Execution Manager: coordinates three agents (C#, PHP, Python). Agent sandboxes cannot download from blob storage; they read CI results through job annotations and the artifacts list.
- Repos: `majid-ghanbari-dost/Kandoo` (C#, parent), `Kandoo-Digital-Invoice` (Python), `kandoo-cloud-php` (PHP, private).
- Channel: GitHub only. Orders in `docs/orders/`, reports in `docs/reports/<order>-<task>.md` on agent branches, decisions here and in `docs/decisions-made.md`. No issues, no extra coordination files.

## Decision log

### 2026-11-18..21 — Windows installer chain (C-87 fix series) and process reset
- **PM-01 No self-hosted runner.** Owner constraint: everything builds and tests on GitHub-hosted runners. "Deferred for infrastructure" needs a quoted error line and 3 documented attempts.
- **PM-02 Operating Rules v2** (see `docs/OPERATING-RULES-v2.md`): 15 pushes per task, iterate until green, orders and reports as repo files, PM merges on green CI with PO approval, one new tag per task `o16-<task>`, one PR per task for C#, one consolidated PR per lane accepted for PHP and Python.
- **PM-03 Windows MAUI build facts (do not re-learn):** windows-latest default `dotnet` is SDK 11; the SDK 8 pin lives only in `src/Kandoo.App/global.json`; MAUI steps run with `working-directory: src/Kandoo.App`; workload `maui-windows`; select the Windows-only target with `-p:KandooWindowsOnly=true` (never the plural `-p:TargetFrameworks`); pwsh arrays are fixed-size and the comma binds tighter than `+`.
- **PM-04 Installer layout v2:** `{app}\hub\Kandoo.Hub.exe` and `{app}\app\Kandoo.App.exe`, two separate self-contained runtimes, never mixed in one folder (a framework-dependent exe next to a self-contained hostfxr.dll produced the "install .NET" prompt). Shortcuts and [Run] must point to `{app}\app\`. Inno Setup script and smoke script are PM-owned.
- **PM-05 Launcher is temporary:** the WinForms launcher was replaced by the MAUI `SetupWizardPage` (C-95). First-run flow: free random Hub port, previous Hub killed on retry, owner session via bootstrap proof (fix for HTTP 403 `OWNER_BOOTSTRAP_INVALID`, ORDER 19).
- **PM-06 Real invoice samples NOT granted** to agents (real data must not leave the owner's PC). E-70/E-73 use synthetic fixtures plus a local tool the owner runs, printing only aggregate numbers.
- **PM-07 Claims need evidence:** "infrastructure limit" or "unrelated flakiness" without a quoted error line is not accepted; judge CI steps by outcome, not conclusion (`continue-on-error` hides failures).
- **PM-08 Merges:** PM merges when CI is genuinely green and the PO approved. PR #11 (ORDER 15 close-out, installer, ORDER 19/20) merged into `main` on 2026-11-21 (`5500a7a8`).

## Status snapshot (2026-11-21)
- C# lane: per-task branches `arena/kandoo-c90-windows-store`, `c91-android-store`, `c92-customer-mode`, `c93-holoo-connector` (PR #16 open), `c95-maui-launcher`. CI job "Android 10 emulator launch + 15s liveness" is red (continue-on-error); first verbatim error still owed by the C# agent.
- Python lane: PR #16 conflicts with main, agent resolving. E-70/E-73 per PM-06.
- PHP lane: ORDER 15 PR to main being prepared (ORDER 15 content only); draft PR #3 holds ORDER 16 work.
- Open for the PO: real installer test on a Windows PC after PR #11 (installer artifact `kandoo-hub-c87-setup-exe`), lawyer text (D14), SMS provider (D4), production VPS host (D5).

## How to continue in a new chat
First message to the PM: "Read docs/PM_LOG.md, docs/OPERATING-RULES-v2.md and the newest files in docs/reports/ on majid-ghanbari-dost/Kandoo (main), then give me the status and the next orders."
