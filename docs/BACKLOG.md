# KANDOO BACKLOG — ordered work for every agent (PM, 2026-11-27)

How to use (all agents): when your current task is done and merged, take the NEXT item of YOUR owner section immediately. Never wait for another agent. If an item is blocked, write one line in `docs/questions/Q-<id>.md`, mark it BLOCKED here in your PR, and take the next item. If your section is empty, take the first item of any section whose owner is not running (check `docs/STATUS.md` / open PRs), or take the "ANY AGENT" items at the bottom. Merge your own green PRs (PM-31) and fix `main` first if it is red. Only the PO's decisions and design conflicts go to the PM.

## C-PLAT (platform, CI, installer, Hub)
1. CI speed: caching, parallel jobs, path filters, `concurrency` cancel; target PR check < 8 min. Generate `docs/STATUS.md` from CI.
2. Installer: upgrade-over-existing install, uninstall removes shortcuts and leaves data, per-user data folder, backup path/schedule from the Setup wizard wired to the Hub.
3. Replication reliability (C-90): retry with backoff, idempotency tests, offline queue visible in the app (status chip).
4. Auto-update channel design (no store): version check + manual download link in Settings (no tracking).
5. Crash/diagnostic report button on every error page (copy report) and a log folder.
6. Windows installer artifact + signed APK published on every `main` merge; links in `docs/STATUS.md`.

## C-WIN-A (Windows screens: Setup, Role, Review, Invoice, Invoices)
1. Setup wizard exactly per `Setup.dc.html` (3 steps, demo SMS code via outbox, backup step).
2. Review queue with "what I read" beside the picture, keyboard flow, empty/error states.
3. Invoice issue (Mode A): product search, lines, discount, payment method, print option, customer mobile.
4. Invoices list/detail on the C-99 route: filters, SMS status, cancel/return entry.
5. Screenshot comparison against the boards for every screen above, fix the differences.

## C-WIN-B (Windows screens: Products, Customers, Return, Settings, Devices, States, Accounting views)
1. Product list/create/edit per the boards and UX spec (plus/minus rows, 3 mandatory fields, auto tag).
2. Customers list/detail, Return flow, Receivables.
3. Settings and Devices (pairing code, revoke), store profile, printer/capture settings.
4. Accounting views on the C-94 summary route: sales by payment method, stock value, professional-view toggle.
5. Empty/error states for every list; RTL and title-bar buttons on the right (verify).

## C-AND-STORE (Android store app)
1. Pairing over LAN, product list/create, invoice issue, review queue, invoices list (boards `Phone-*-v2`).
2. Offline-first sync and a clear offline banner; J8-friendly performance (list virtualization).
3. Signed APK pipeline and in-app version string; measure cold start on CI where possible and report numbers.
4. Remaining v1 phone boards rebuilt in v2: Customers, Return, Accounting, More.

## C-AND-CUST (customer mode)
1. OTP login (P-31 contract), invoices per store, detail, locked/empty/new-invoice states, role switch.
2. Device remembered, logout, session expiry handling, error copy per the board `Customer-Locked`.
3. Link the customer app to the local PHP Cloud in the demo profile (LAN URL setting).

## C-HOLOO (Holoo connector)
1. Phase 2 of C-93: incremental sync and shadow store tables, schema-version detection.
2. Import of recent invoices into the review queue with provenance; unverified stock marking.
3. Consent screen text and the setting for the read-only connection (position per UX spec, ask if unresolved).

## PHP-A (customer web page, local kit, admin)
1. P-36 customer web page v2 finished with screenshots; print-friendly view.
2. Local kit: one script to start the PHP Cloud with MariaDB on the PO's PC (Windows), LAN URL printed.
3. P-32 admin page v2 (stores, devices, OTP counter reset, SMS stats, outbox).

## PHP-B (API, security, host kit)
1. P-31 finish and vectors accepted by the C# side; throttling and session tests.
2. P-34: load and security review; findings fixed or listed.
3. P-30 host runbook and self-test script; P-33 Faraz pattern kit (texts ready to paste); nothing is bought.

## PY-A / PY-B (extraction)
1. E-74 re-score on both rasters (invoice 34 stays the holdout, the lane's synthetic sample is the second); EXTRACTED-but-wrong = 0.
2. E-70 onboarding from first invoices with operator confirmation; E-73 capture robustness; capture folder -> review item.
3. E-72 vectors accepted by C# and PHP; E-71 per-layout numbers; E-75 bundle size/startup (stretch).

## ANY AGENT (when your section is empty)
- UI fidelity pass: take screenshots of every shipped screen, compare with its board, open small PRs fixing differences.
- Test hardening: add tests for the code you read while integrating; remove flaky tests.
- Documentation: `RUN-LOCAL.md` (Persian) correctness by actually following it step by step in CI where possible.
- Pair review: read the newest merged PR of another lane and file concrete bugs as `docs/questions/` or fix them if you own the path.
