# PM -> EXECUTION MANAGER (GitHub channel)

Channel rules (effective 2026-11-22, approved by the PO):
- The PM writes messages HERE (`docs/messages/PM-TO-MANAGER.md` on `main` of the C# repo), newest message first, numbered M-NNN. The PO only triggers: he tells the manager "read M-NNN".
- The manager answers in `docs/messages/MANAGER-TO-PM.md` on branch `arena/order-15-status` (same message numbering: R-NNN answers M-NNN). The PM reads it from GitHub. The PO only triggers: "the manager answered".
- Nothing is copied through chat except the trigger sentence. Messages stay short; details live in `docs/PM_LOG.md`, `docs/orders/` and `docs/reports/`.

---

## M-003 (2026-11-22) — open items, in this order
1. **PHP:** PR #4 is merged into PHP `main` (`ad1efce6`). Merge `origin/main` into `arena/f729618f-kandoo-cloud-php`, push, confirm draft PR #3 shows only ORDER 16. No tags until a task is finished and green. Stray branch `pm/o15-baseline` stays untouched.
2. **C# lane:**
   a. Merge `origin/pm/order20-installer-layout` (installer layout v2: `.iss` shortcut paths + new smoke script) into your task stack; do not edit those two files.
   b. The job "Android 10 emulator launch + 15s liveness" is red (runs 44882873347, 44882868584): quote the first verbatim error line, job and step; at most 3 fix pushes; if still red, BLOCKED with the verbatim line and continue.
   c. Priority: C-90, C-91, C-92 to green + tag `o16-<task>` + report; then C-93 acceptance; C-94 stretch last.
3. **Python lane:** PR #16 still conflicts with `main`: merge `origin/main` (no rebase), resolve, push; report new head SHA and a green run URL. E-70/E-73 per PM-06 (synthetic fixtures + local aggregate-only tool). Priority: E-72, E-74, E-71.
4. **P-31 cross-lane:** the C# agent reviews `docs/vectors/customer-api-v1-2-proposed.json` (PHP repo) and writes accept/changes in `docs/reports/ORDER-16-C-92.md`; PHP keeps P-31 as PENDING-CROSS-LANE until then.
5. **Reporting:** answer in `docs/messages/MANAGER-TO-PM.md` with branch names, tag names (`o16-<task>`) and CI run URLs only. Do not send long chat texts to the PO.

## M-002 (2026-11-21) — decisions
See `docs/PM_LOG.md` entries PM-01..PM-12 (no self-hosted runner, PR and tag policy, real samples not granted, PHP baseline `2724969f`, cross-lane order).

## M-001 (2026-11-21) — rules
`docs/OPERATING-RULES-v2.md` applies to all lanes (15 pushes per task, iterate until green, orders and reports in the repo, evidence for every claim).
