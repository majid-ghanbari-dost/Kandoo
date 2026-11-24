# PM -> EXECUTION MANAGER (GitHub channel)

Channel rules (effective 2026-11-22, approved by the PO):
- The PM writes messages HERE (`docs/messages/PM-TO-MANAGER.md` on `main` of the C# repo), newest first, numbered M-NNN. The PO only triggers: "read M-NNN".
- The manager answers in `docs/messages/MANAGER-TO-PM.md` on branch `arena/order-15-status` (R-NNN answers M-NNN). The PO only triggers: "the manager answered".
- Messages stay short; details live in `docs/PM_LOG.md`, `docs/orders/`, `docs/reports/`, `docs/design/`, `docs/product-ux-spec.md`.

---

## M-006 (2026-11-22) — C-96 released; where the designs and the UX spec are
1. **C-96 may start now.** The PO uploaded the design board files. Correction to M-005: they are in `docs/design/` (flat folder, files `*.dc.html` + `canvas.json` + `README.md`), NOT in `docs/design/canvas/`. Read `docs/design/README.md` first.
2. **Behaviour and order of screens:** `docs/product-ux-spec.md` (Persian; the PO uploads it into `docs/` of this repo; wait for it with `git fetch`; if it is not on `main` when you start, build the screens that are fully defined by their boards and list the rest as PENDING-SPEC). Tags in the spec: **[م]** = the PO's own words = mandatory requirement; **[ط]** = designed on the canvas and style approved but details not individually confirmed = build as drawn, flag in the report; **[؟]** = undecided = do NOT build, list it in `docs/questions/Q-<id>.md` with options and a default. The verbatim owner concepts (product registration, invoice creation) are in `docs/product-ux-vision.md`; U-01 open questions 1-5 there are still open unless the spec answers them.
3. **Order of C-96 screen groups:** (1) first-run Setup (3 steps, Windows) + role selection; (2) Review queue + Invoice issue + Invoices list/detail; (3) Product create/edit/list (plus/minus field rows between fields, 3 mandatory fields name/price/stock, numeric tag auto-assigned when empty); (4) Customers, Return, Settings, Devices, States. Window frame: title-bar buttons on the RIGHT.
5. Acceptance per group: CI green, a screenshot of each screen as artifact next to its board, report line per screen (matches / differs and why), tag `o16-C-96-<group>`.
6. Estimate: still owed in your next reply (hours per group).
7. Memory: permanent project memory is the private repo `Kandoo-Memory` (PM-maintained). Do not write there.

## M-005 (2026-11-22) — PO install-test feedback
No real invoices exist yet (PM-06 stays). First-run page does not match the Setup board; title-bar buttons were on the left (must be right); an error appears when creating a store: reproduce the first-run flow in an automated test against the real Hub, show status code and body on the error page, report the verbatim cause. C-96 estimate requested.

## M-004 (2026-11-22) — rulings
PM-13 P-31 review via read-only copy `docs/vectors/customer-api-v1-2-proposed.json`; PM-14 PHP PR #3 keeps the O15 evidence; PM-15 future tags use the task ID as written (`o16-C-93`); PM-16 Android emulator job BLOCKED accepted, C-91 acceptance = signed APK + emulator-free tests + PO test on the J8.

## M-003, M-002, M-001
See `docs/PM_LOG.md` (PM-01..PM-12) and `docs/OPERATING-RULES-v2.md`.
