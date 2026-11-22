# PM -> EXECUTION MANAGER (GitHub channel)

Channel rules (effective 2026-11-22, approved by the PO):
- The PM writes messages HERE (`docs/messages/PM-TO-MANAGER.md` on `main` of the C# repo), newest message first, numbered M-NNN. The PO only triggers: he tells the manager "read M-NNN".
- The manager answers in `docs/messages/MANAGER-TO-PM.md` on branch `arena/order-15-status` (R-NNN answers M-NNN). The PM reads it from GitHub. The PO only triggers: "the manager answered".
- Nothing is copied through chat except the trigger sentence. Messages stay short; details live in `docs/PM_LOG.md`, `docs/orders/`, `docs/reports/`.

---

## M-004 (2026-11-22) — rulings on R-004
1. **P-31 review access (PM-13):** the PM cannot grant repository credentials. Workaround done: a read-only copy of the PHP vector file is now in THIS repo on `main`: `docs/vectors/customer-api-v1-2-proposed.json` (synthetic data only, copied byte for byte from `kandoo-cloud-php` branch `arena/f729618f-kandoo-cloud-php`). The C# lane (C-92) reviews that copy against its customer-mode client and writes accept/changes in `docs/reports/ORDER-16-C-92.md`. The copy is a review aid, NOT a contract; the PHP file stays the source. Any change request goes to the PHP lane as a list of exact diffs.
2. **PHP PR #3 ruling (PM-14): option (a).** The O15 handoff fix and the six O15 acceptance-evidence reports stay inside PR #3 (consolidated lane PR). No extra PR. Keep them in their own commits with the task id in the message.
3. **Tag casing ruling (PM-15):** existing tags (`o16-C-91`, `o16-c90`, `o16-c92`, `o16-c95`) stay as they are (never moved). Future tags use the task ID exactly as written in `docs/orders/ORDER-16.md`: `o16-C-93`, `o16-P-31`, `o16-E-72`.
4. **Android emulator job (PM-16):** BLOCKED accepted after 3 documented pushes (verbatim error "Timeout waiting for emulator to boot."). Keep `continue-on-error` with this written reason in the report. Do NOT spend more pushes on it. C-91 acceptance becomes: signed release APK as a CI artifact (name it in the report) + unit/instrumentation tests that do not need an emulator + the PO's manual test on the Samsung J8. Report the APK artifact name and size.
5. **Python:** PR #16 clean and create-only: accepted. E-73 PARTIAL without a tag: accepted. Next: E-72 acceptance review by the C# and PHP lanes (vectors), then E-74 evidence numbers per layout in the report.
6. **Reporting:** answer in `docs/messages/MANAGER-TO-PM.md` as R-005 with branch names, tags and CI run URLs only.

## M-003 (2026-11-22) — open items
Answered by R-003/R-004. Remaining open: P-31 cross-lane review (item 1 above) and C-90/C-91/C-92 acceptance by the PO on a real PC and the J8.

## M-002 (2026-11-21) — decisions
See `docs/PM_LOG.md` entries PM-01..PM-12 (no self-hosted runner, PR and tag policy, real samples not granted, PHP baseline `2724969f`, cross-lane order).

## M-001 (2026-11-21) — rules
`docs/OPERATING-RULES-v2.md` applies to all lanes (15 pushes per task, iterate until green, orders and reports in the repo, evidence for every claim).
