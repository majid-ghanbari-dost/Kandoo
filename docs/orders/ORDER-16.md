# ORDER 16 — PILOT READINESS + STAGES B/C

From: PM. To: C#, PHP and Python agents (via the Execution Manager).
Rules: `docs/OPERATING-RULES-v2.md` (15 pushes per task, iterate until green, reports in `docs/reports/`).
Start condition: ORDER 15 is closed for your repo and the ORDER 19 bootstrap fix is merged (C# agent).
Baseline: current `origin/main` of your repo after the PM merged the ORDER 15 close-out.

Standing rules (unchanged): frozen contracts are never edited, changes go only into new additive addendum files; one commit group and one NEW tag per task; agents never merge or push to `main`; no credentials in any repo; wording is "فاکتور دیجیتال کندو" only (never "رسید" or "official invoice"; no legal or tax claims); decisions are logged in `docs/decisions-made.md`; MUST before STRETCH.

## C# AGENT (about 40 h)

- **C-90 (MUST)** Windows store software at pilot level: screens for customers, returns, devices and settings (v2 style); replication queue with retry and backoff when the internet is down (nothing lost, nothing sent twice, idempotent); setup.exe from CI (already working) with the automated install smoke test; test on a clean Windows machine and record the result.
- **C-91 (MUST)** Android store app, complete: pairing, products with group/subgroup, invoice issue, review queue, invoices list; offline-first with sync; signed release APK; cold start on Samsung Galaxy J8 (Android 10) at most 3 s, report the real measured number or say it was not measured.
- **C-92 (MUST)** Customer mode inside the same Android app: OTP login against the PHP customer API (P-31), invoices grouped per store, role switch store/customer/both, verified device remembered. The mobile number is the identity; roles live under it.
- **C-93 (MUST)** Holoo live read-only connector hardening per decision D-M (tiers 1, 2, 3): incremental sync, schema-version detection, complete shadow store (products, customers, history), imported stock marked "unverified"; last sold price where a product has no sale price; never guess or read stored passwords; tested against the synthetic SQL Server fixture in CI and against the owner's local HolooCopy if available.
- **C-94 (STRETCH)** Light management accounting, read-only, derived from events: sales by payment method, receivables, stock value, profit only for products with a purchase price; a "professional view" toggle shows debit/credit terms. No tax integration.
- **C-95 (MUST)** Replace the temporary WinForms launcher (`src/Kandoo.App.Launcher`) with the MAUI `SetupWizardPage` (v2 design): single app, no separate launcher, same first-run flow (store name -> start local Hub on a free port -> owner session -> app). Keep the automated install smoke test green (adapt `scripts/Invoke-InstallerSmoke.ps1` expectations in your report if the launcher disappears; the PM updates the script).

## PHP AGENT (about 30 h)

- **P-30 (MUST)** Shared-host deployment package: step-by-step runbook for plan E1 (PHP 8.1, MariaDB 10.6), self-test script protected by a secret key (PHP version, extensions, outbound HTTPS to api.iranpayamak.com, outbound IP, MariaDB version, utf8mb4 round-trip of Persian text, cron heartbeat); nightly backup via cron to a separate location with a tested restore.
- **P-31 (MUST)** Customer API for the Android app in a NEW addendum file (Contract Addendum v1.2 proposal): OTP request and verify, invoices list per store, device remembered. Keep the D-C OTP policy: a number may request a code only if an invoice exists for it; two codes per invoice-notification SMS; 3 wrong entries lock; per-device and per-IP limits; all values configurable.
- **P-32 (MUST)** Admin page v2: stores, devices (revoke), OTP counter reset, SMS statistics and cost log. Default off, protected.
- **P-33 (MUST)** Production readiness for Faraz SMS: registration kit for the two patterns (one fixed notification pattern with a fixed link and no variables; one OTP pattern with one variable), final-domain placeholders, per-SMS cost log, privacy-minimisation review of retained SMS rows with a written proposal.
- **P-34 (MUST)** Real load test on MariaDB configured like the target host and a security review of ingest, OTP, admin and the public page; fix findings or list them honestly.
- **P-35 (STRETCH)** Public invoice page: print-friendly view and a placeholder slot for legal text (a flag only; no legal wording from you).

## PYTHON AGENT (about 28 h)

- **E-70 (MUST)** Production onboarding flow: learn from the first real invoices with operator confirmation, no test kit needed. Confirmed labels feed the baseline. Rule: unsure means REVIEW, never a silent mistake.
- **E-71 (MUST)** Multi-layout support: synthetic generator for a second and a third print layout (different columns, logo size, currency label); accuracy measured per layout.
- **E-72 (MUST)** Canonical Invoice v1.2 draft as NEW additive files only: cheque, invoice-level discount, walk-in customer flag; golden vectors the C# and PHP agents can verify byte for byte.
- **E-73 (MUST)** Print-capture robustness: duplicate jobs, empty 0-byte files, a job filed in the wrong folder; regression tests built from the real sample structure (real data stays local and git-ignored).
- **E-74 (MUST)** Accuracy gate: one command runs the full harness; hard requirement EXTRACTED-but-wrong = 0; counts per layout.
- **E-75 (STRETCH)** Windows bundle size and startup time reduction with measured before/after numbers.

## ACCEPTANCE FOR ORDER 16 (the PM checks from GitHub)

1. A real sale made on a clean Windows install (installed by `Kandoo-Setup-x64.exe`) appears in the Cloud and in the SMS outbox.
2. The customer opens the app on the J8, logs in with the outbox code and sees the invoice.
3. A recent Holoo invoice is read read-only and lands in the review queue with "what I read" beside the picture; nothing is wrong without being flagged.
4. Every task has a tag, a full SHA, an opened CI run URL and a short Persian report in `docs/reports/`.
5. Anything not finished is listed as PARTIAL or BLOCKED with the reason.
