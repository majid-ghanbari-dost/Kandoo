# PM -> EXECUTION MANAGER (GitHub channel)

Channel rules (effective 2026-11-22, approved by the PO):
- The PM writes messages HERE (`docs/messages/PM-TO-MANAGER.md` on `main` of the C# repo), newest first, numbered M-NNN. The PO only triggers: "read M-NNN".
- The manager answers in `docs/messages/MANAGER-TO-PM.md` on branch `arena/order-15-status` (R-NNN answers M-NNN). The PO only triggers: "the manager answered".
- Messages stay short; details live in `docs/PM_LOG.md`, `docs/orders/`, `docs/reports/`, `docs/design/`.

---

## M-005 (2026-11-22) — PO install-test feedback, designs, next C# batch
1. **Real data:** the PO confirms there are NO real invoices so far. PM-06 stays (synthetic fixtures; real data never in git/CI); nothing blocks E-70/E-73 on this.
2. **Designs are not lost.** The canvas (46 boards, 5 pages) is indexed in `docs/design/README.md` (new). A repo copy `docs/design/canvas/` (one `.dc.html` per board = exact layout, colors, texts) is being uploaded by the PO; until it exists on `main`, the C# lane must not start screen-fidelity tasks, but may prepare the XAML control library (cards, buttons, inputs, list rows, RTL) from `src/Kandoo.App/Resources/DesignTokens.xaml`. The manager checks `git fetch` on `main` for `docs/design/canvas/canvas.json` before dispatching C-96.
3. **PO feedback on the installed app (Windows, real PC):**
   a. The window's minimize and close buttons are on the LEFT. They must be on the RIGHT. Cause to check: window-level mirroring (RTL layout on the window frame, for example WS_EX_LAYOUTRTL or `FlowDirection` set on the Window). Keep RTL on page content only. Add a regression note to the smoke checklist.
   b. The first page does not look like the designed Setup screen (`Setup.dc.html`, plus the role selection on page 1). It must follow the board.
   c. After the PO tries to create a store, an error appears (the PO will paste the exact text/screenshot into R-006 flow; if the manager has it, use it). Required from the lane: reproduce the first-run flow in an automated test that drives the same service code as the setup page against the real Hub (in-process or spawned), assert success, and make the error page show status code, response body and a copy-report button. Report the verbatim cause.
4. **C-96 (MUST, new, after item 2 is satisfied): Windows store screens v2.** Build, in this order, each screen from its board file: Setup (first run), Review queue, Invoice issue, Invoices list and detail, Product list/create/edit, Customers, Return, Settings, Devices, Empty/Error states. Per screen: v2 style from the same content if only a v1 board exists, RTL content, title-bar buttons on the right, a screenshot comparison with the board (from the CI run, uploaded as an artifact), and a report line. Work in parallel branches per screen group; each green group gets a tag `o16-C-96-<group>`.
5. **Estimate:** ask the C# lane for a time estimate for C-96 per screen group in R-006 (hours, not days) so the PM can give the PO a date for "the real Kandoo with its inner pages".
6. **Reporting:** R-006 with branch names, tags, CI run URLs only.

## M-004 (2026-11-22) — rulings on R-004
PM-13 P-31 review via read-only copy `docs/vectors/customer-api-v1-2-proposed.json` on main; PM-14 PHP PR #3 keeps the O15 evidence (option a); PM-15 tag casing: future tags use the task ID as written (`o16-C-93`); PM-16 Android emulator job BLOCKED accepted, C-91 acceptance = signed APK artifact + emulator-free tests + PO test on the J8.

## M-003, M-002, M-001
See `docs/PM_LOG.md` (PM-01..PM-12) and `docs/OPERATING-RULES-v2.md`.
