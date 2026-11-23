# MANAGER -> PM (GitHub channel)

R-NNN answers M-NNN in docs/messages/PM-TO-MANAGER.md on main. Newest first.
Filing note: the manager sandbox has read-only GitHub access; the manager files R-NNN here through
the C# lane (exact-content commit) until a write token is granted. The channel-rules fallback
clause applies.

---

## R-006 (2026-11-23) — answers M-005

1. Item 3a (window chrome): commit c8e1415 on arena/4bb6cd51-kandoo — Window.FlowDirection=LeftToRight in App.xaml.cs CreateWindow (buttons on the RIGHT), page content stays RTL; regression note in RUN-LOCAL.md. CI: https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/45125498151 green.
2. Item 3c (setup flow): same commit — verbatim cause: SetupWizardPage sent an empty "{}" body (proof only in the Authorization header); the endpoint reads the body → 403 OWNER_BOOTSTRAP_INVALID. Fix: full OwnerSessionRequest body. Tests: SetupFlowStoreCreationTests (in-process Hub, asserts success; regression asserts empty body → 403). CI: https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/45125498151.
3. Item 2 (control library prep): branch arena/kandoo-c96-control-library-prep rebased on the fixed stack (src/Kandoo.Controls from DesignTokens.xaml), no tag. CI: https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/45126883836 green.
4. Item 5 (estimates, hours per C-96 group): Setup 6, Review queue 5, Invoice issue 8, Invoices list+detail 5, Product CRUD 6, Customers 4, Return 5, Settings 4, Devices 4, Empty+Error 3 — total ~50 (docs/reports/R-006-C-96-time-estimates.md, commit c8e1415).
5. Item 4 + 3b (C-96 and Setup screen fidelity): HELD — docs/design/canvas/canvas.json absent on main (manager re-verified 2026-11-23).
6. PM-16: manager ruling — CI artifacts kandoo-app-c59-android-release-apk (signed release) and kandoo-app-c50-android-debug-apk satisfy the signed-APK requirement; the PO J8 test uses the release APK; C-91 report cites both (be3c20a).
7. PM-13 / P-31: manager cross-lane review verdict ACCEPT (docs/vectors/customer-api-v1-2-proposed.json on main); verdict recorded in docs/reports/ORDER-16-C-92.md; PHP unblocks P-31.

---

## R-005 (2026-11-22) — answers M-004

1. PM-13: dispatched — the read-only copy docs/vectors/customer-api-v1-2-proposed.json is on main
   (manager-verified 2026-11-22); the C# verdict lands in docs/reports/ORDER-16-C-92.md on
   arena/4bb6cd51-kandoo; PHP unblocks P-31 acceptance from that file.
2. PM-14: acknowledged — PHP PR #3 keeps the O15 evidence (option a); no PHP action.
3. PM-15: acknowledged — future tags use the task ID as written (o16-C-93 style); existing
   lowercase tags stay per rule 6.
4. PM-16: acknowledged — emulator stays BLOCKED (no further pushes); C-91 acceptance = signed APK
   artifact + emulator-free tests + PO test on the J8; the C# lane confirms the signed APK
   artifact and cites emulator-free runs in the C-91 report.
5. M-005 received: dispatched — XAML control library prep (item 2), window chrome fix (3a),
   setup-flow automated test + error page (3c), per-group estimate for R-006. HELD until
   docs/design/canvas/canvas.json lands on main (verified absent 2026-11-22): C-96 and the Setup
   screen fidelity (3b).

---

## R-004 (2026-11-22) — follow-up to M-003/R-003: progress + one ruling requested

1. C# (M-003 items 2a/5): origin/pm/order20-installer-layout merged into arena/4bb6cd51-kandoo (merge 361b2fb, tip 03b03d7; packaging/HubPilot/KandooHub.Installer.iss and scripts/Invoke-HubPilotSmoke.ps1 verified untouched by the manager). R-003 filed at 7c6776a on arena/order-15-status; content verified exact.

2. P-31 correction: docs/vectors/customer-api-v1-2-proposed.json is NOT in Kandoo-Digital-Invoice — the manager had pointed the C# lane at the wrong repo; per M-003 the file lives in the PHP repo (majid-ghanbari-dost/kandoo-cloud-php). The C# lane attempted to clone it and was denied (403 — write/read access not granted for the lane credentials). ACCESS DENIED — PM TO GRANT: read access to kandoo-cloud-php is sufficient for the P-31 review; alternatively place the vector file somewhere the C# lane can read. The verdict will land in docs/reports/ORDER-16-C-92.md on arena/4bb6cd51-kandoo once access is granted. PHP keeps P-31 PENDING-CROSS-LANE.

3. PHP (item 1) DONE: origin/main (ad1efce6) merged into arena/f729618f-kandoo-cloud-php (merge 91e2d29); PR #3 head a0fd5d1; CI green (run 45000605111); pm/o15-baseline untouched; no O16 tags. RULING REQUESTED: the PR #3 diff still contains the O15 handoff fix and six O15 acceptance-evidence reports created after the baseline tag 2724969f (PHP kept them to preserve evidence). Options: (a) accept them inside PR #3, or (b) approve landing them on main in a separate small PR first so PR #3 becomes O16-only.

4. Python (item 3): holding as ordered — PR #16 open/create-only; E-73 ACTIVE/PARTIAL (owner-side capture review pending); E-75 deferred.

5. Tag-casing ruling (R-003 item 2) still open.

---

## R-003 (2026-11-22) — answers M-003

M-003 dispatched to all lanes on 2026-11-22. All git-level claims below were re-verified by the
manager via ls-remote/fetch; CI runs are cited by the lanes (the PHP repo is private and not
manager-verifiable).

1. PHP (item 1): merging origin/main (ad1efce6) into arena/f729618f-kandoo-cloud-php and the
   PR #3 "ORDER 16 only" check are in this cycle; P-31 proposed vectors replayed green by the lane
   (runs 44991863283, 44991866487); P-31 stays PENDING-CROSS-LANE; no tags; pm/o15-baseline
   untouched.
2. C# (item 2):
   - 2b closed as BLOCKED: 3 real fix pushes on arena/4bb6cd51-kandoo — b1fecb1 (diagnostic),
     7e24af5 (udev + flags), 0edbef9 (-no-accel); emulator job red in all three (Push 3 run
     44992987552). Verbatim error: "Timeout waiting for emulator to boot." PARTIAL/BLOCKED per
     rule 7; continue-on-error kept. Non-emulator jobs green (C-92 test fix: run 44991041523).
   - Tags verified: o16-C-91 -> f453f93 (CI green on that commit: run 44875204264),
     o16-c90 -> 286f28c, o16-c92 -> 17eba20, o16-c95 -> 08950a5. Spelling is mixed (o16-C-91 vs
     lowercase o16-c90/c92/c95); rule 6 forbids moving existing tags — requesting a ruling;
     proposal: future tags follow the task ID casing.
   - 2a (merge pm/order20-installer-layout) runs in this cycle. C-90 PARTIAL (owner Windows test
     pending), C-95 install smoke passed, C-93 PR #16 open/green, C-94 not started.
   - 2c priority order acknowledged.
3. Python (item 3): the conflict premise was stale — PR #16 was already mergeable (manager live
   check: mergeable=true at 5e4bdbf67 before the latest push). Current head 1e620cc1, CI green
   (runs 44997224552 / 44997224445 / 44997224409); PR #16 stays open, CLEAN, create-only — no
   merge, no rebase. Tags verified: o16-E-70 -> 3bdc648, o16-E-71/E-72/E-74 -> f99ab20; E-73
   PARTIAL (no tag, correct); E-75 deferred.
4. P-31 (item 4): still OPEN — the C# review of docs/vectors/customer-api-v1-2-proposed.json into
   docs/reports/ORDER-16-C-92.md runs in this cycle; PHP keeps PENDING-CROSS-LANE until it lands.
5. Reporting (item 5): acknowledged.
