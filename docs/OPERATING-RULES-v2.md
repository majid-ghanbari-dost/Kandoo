# KANDOO — OPERATING RULES v2

Effective immediately. Replaces any conflicting earlier rule (for example "one push per CI run").
Approved by the product owner on 2026-11-20. Amended by the PM on 2026-11-21 (rules 6 and 8, tag and PR policy).

1. **Orders live in the repo.** Orders are files in `docs/orders/` on `main` of the C# repo (the PM adds them).
   Agents read the order from there. Agents write reports to `docs/reports/<order>-<task>.md` on their own branch
   (short, Persian, one table: task, branch, full SHA, CI run URL, result, artifact names).
   The PM reads reports from GitHub. The PO no longer copies text between agents.
2. **Iterate until green.** Push budget is **15 CI-triggering pushes per task**. After each push read the run through the API
   (`gh run list`, job annotations with `--paginate`, artifacts list) and continue until CI is green or the budget ends.
   Never stop to ask unless one of the four R-conditions applies: missing owner input; a frozen-contract change;
   real data leaving the PC; an unsafe security or data-loss risk.
3. **Every CI step that can fail prints its error as `::error::` annotations and uploads its log as an artifact.**
   No `continue-on-error` without a written reason. Judge steps by outcome, not conclusion.
4. **Definition of done = CI green on both jobs + the required artifact exists + the automated smoke test passed.**
   "It compiles" is not done.
5. **Tasks are big and run in parallel** (C#, PHP and Python agents at the same time). Cross-agent dependencies go through
   the frozen contracts, not through waiting.
6. **Tags and merges.** Agents never merge and never push to `main`. Agents DO create ONE NEW tag per finished task,
   named `o16-<task>` (for example `o16-C-93`), on the green commit of their own branch. Agents never move, delete or re-use
   an existing tag and never tag `main`. The PM merges when CI is genuinely green and the PO approved.
7. **Honest status.** PARTIAL / BLOCKED with the first verbatim error line. No success claim without an opened CI run URL.
   A claim such as "infrastructure limit" or "unrelated flakiness" without a quoted error line is not accepted.
8. **Pull requests.** C# lane: one create-only PR per task from its own branch. PHP and Python lanes: if the platform fixes one
   branch per lane, ONE consolidated create-only PR per lane is accepted, with one commit group per task (task id in every commit
   message), one report file per task, and the PR description listing task, SHA, CI run URL, result. A PR that conflicts with
   `main` must be fixed by the agent (merge `origin/main` into the branch, resolve, push) before the PM looks at it.
9. **No self-hosted runners.** Everything runs on GitHub-hosted runners (owner constraint). A task is never "deferred for
   infrastructure" without a quoted error line and 3 documented attempts.

## Technical traps already hit (do not repeat)
- Windows runner default `dotnet` is SDK 11. The SDK 8 pin lives ONLY in `src/Kandoo.App/global.json`; never add a repo-root `global.json`.
- Never pass the plural `-p:TargetFrameworks=...` on the command line (it leaks into project references). Use `-p:KandooWindowsOnly=true`.
- In pwsh, `@()` arrays are fixed-size (no `.Add`); the comma operator binds tighter than `+` inside `@( ... )`.
- In bash heredocs an unquoted `$env:NAME` is expanded away; use quoted heredocs and re-read the file.
- Agent sandboxes cannot download from blob storage: read job annotations and the step summary instead.
- `scripts/Invoke-InstallerSmoke.ps1` and `packaging/HubPilot/KandooHub.Installer.iss` are PM-owned. Changes to `.github/workflows/` are made by the agent that owns the task.
