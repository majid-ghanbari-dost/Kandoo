# KANDOO — OPERATING RULES v2

Effective immediately. Replaces any conflicting earlier rule (for example "one push per CI run").
Approved by the product owner on 2026-11-20.

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
6. **PRs are create-only for agents.** The PM merges when CI is genuinely green and the PO approved.
   Agents never merge, never tag, never touch `main`.
7. **Honest status.** PARTIAL / BLOCKED with the first verbatim error line. No success claim without an opened CI run URL.

## Technical traps already hit (do not repeat)
- Windows runner default `dotnet` is SDK 11. The SDK 8 pin lives ONLY in `src/Kandoo.App/global.json`; never add a repo-root `global.json`.
- Never pass the plural `-p:TargetFrameworks=...` on the command line (it leaks into project references). Use `-p:KandooWindowsOnly=true`.
- In pwsh, `@()` arrays are fixed-size (no `.Add`); the comma operator binds tighter than `+` inside `@( ... )`.
- In bash heredocs an unquoted `$env:NAME` is expanded away; use quoted heredocs and re-read the file.
- Agent sandboxes cannot download from blob storage: read job annotations and the step summary instead.
- `scripts/Invoke-InstallerSmoke.ps1` is PM-owned. Changes to `.github/workflows/` are made by the agent that owns the task.
