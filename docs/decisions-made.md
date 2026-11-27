# Kandoo — Order 15 Technical Decisions (Demo Week)

Autonomous technical decisions, one per line (what / why / rejected option), per Order 15 rules. No technical question was asked of the PM or owner.

## C-80 — Demo profile (D-L)

1. **Demo profile = one Hub setting + one app setting.** Hub `appsettings.json` gets `Kandoo:DemoMode` (bool) and `Kandoo:Demo:BaseUrl` (single LAN base address per D-L). The app reads its Hub base address from one `KandooDemo:HubBaseUrl` setting. Why: D-L requires "آدرس پایه کلاود = یک تنظیم" (one cloud base address setting). Rejected: hardcoding the Hub URL in each client (not configurable; breaks LAN demo).
2. **Demo data seeded into Hub SQLite on startup** when `DemoMode=true`: ~40 Persian products (group/subgroup + legacy code), 20 customers, ~12 invoices. Why: Hub is the single LAN authority (D7); both Windows and Android clients read demo data via the same Hub API, avoiding divergent data. Rejected: embedding demo JSON separately in each client (data would diverge between Windows/Android).
3. **Demo SMS provider (`DemoSmsProvider`)** implements `ISmsProvider`; writes each demo SMS to a local outbox file (`outbox/demo-sms.log`) plus an in-memory log exposed on the admin/settings page; real SMS is disabled (no gateway credentials in repo). Why: D-L says "SMS واقعی خاموش؛ SMS دمو در outbox اپ + صفحه admin + لاگ". Rejected: real Faraz/gateway integration (D4/ASM-27 stub-only until provider chosen; no credentials in repo).
4. **Android cleartext HTTP restricted to private LAN in demo build.** App validates the Hub base URL at startup: in demo mode, `http://` is allowed only to private LAN ranges (10/8, 172.16/12, 192.168/16) or localhost; `https://` allowed anywhere; `http://` to public IPs rejected. `AndroidManifest` sets `android:usesCleartextTraffic="false"` in demo. Why: D-L raw HTTP only to private LAN ranges; security-conscious default. Rejected: `usesCleartextTraffic="true"` (too broad; cleartext to public IPs).

## Cross-cutting (all tasks)

5. **Single MAUI project for Windows + Android** (`net8.0-android;net8.0-windows10.0.19041.0`), already in `Kandoo.App.csproj`. Why: D6 one-app architecture; existing project already targets both. Rejected: separate Windows/Android projects (duplication; D6).
6. **Local app data in SQLite** via `Microsoft.Data.Sqlite` (matches Hub), stored in app data folder. Why: consistent with Hub store; lightweight; no ORM needed for demo. Rejected: EF Core (heavier than needed for a demo).
7. **Style v2 = existing DesignTokens + new 16px-radius card style.** Colors Honey/Ink/Cream/Wax/Green/Review, Vazirmatn font, RTL FlowDirection, 48px touch targets already present. Added `CardStyle` (Border, CornerRadius=16). Why: matches Order 15 style v2 spec exactly; no new color palette. Rejected: new colors/fonts (would diverge from spec).
8. **Windows app role = Store only** (per existing `AppRolePolicy.IsSupported`: Windows → Store; Android → Store/Customer/Both). Why: existing policy already encodes this; Android customer mode (C-86 stretch) is Android-only per PM ORDER 9 D1. Rejected: Windows customer mode (not in MVP; ORDER 9).

## C-83 — Holoo reader (D-M)

9. **Read-only SQL Server via `Microsoft.Data.SqlClient`** on owner's local `HolooCopy`. Tier 1: one-time Elevated installer creates a read-only SQL login itself when the Windows account has SQL admin; consent page (one tick + manager name) recorded locally; uninstall removes the login. Tier 2: one-time SQL admin password field, never stored. Tier 3: automatic fallback to print capture. Why: D-M read-only connection; Hub never writes to Holoo; no password guessing / no reading stored credentials. Rejected: storing the SQL admin password (explicitly forbidden); direct Holoo writes (D-M).

## C-85 — setup.exe

10. **Inno Setup on windows-latest CI runner** for `setup.exe`; one honest attempt; on failure, publish a zip + one-click launcher as fallback with evidence. Why: Order 15 explicitly names Inno Setup or WiX; an Inno Setup authoring script already exists (`packaging/HubPilot/KandooHub.Installer.iss`). Rejected: WiX (steeper learning curve; existing Inno script).

## C-80b — Replication headers per Cloud contract

12. **Hub replication sends `Authorization: Bearer <device-secret>` header** on every `ingestOperationBatch` request to Cloud, matching the `DeviceBearer` scheme in the existing Cloud contract (`docs/openapi.yaml`). Cloud-side authentication is untouched; the value is the per-device secret already resolved by `HubCloudReplicationClient.AttemptAsync`. Why: Cloud returns 401 without device credentials per D-B; PHP agent (P-23) was blocked by this contract violation. Rejected: sending device secret only in the batch body (not enough; Cloud requires transport-level auth header); adding a custom `X-Device-Key` header (not in the existing Cloud contract; `DeviceBearer` is `Authorization: Bearer`).

13. **v1.1 fields (`payload_version`, `currency`, `invoice_payloads`) included on the batch** when `HubReplicationOptions.PayloadV11Enabled` is true, via `HubReplicationPayloadV11.Apply`. Cloud-side auth untouched. Why: C-45b v1.1 extension must be present on replication requests when Cloud advertises v1.1. Rejected: always sending v1.1 (fail-closed per existing behavior; only enabled when Cloud advertises).

## C-81 — Windows app Stage-A pages style v2

14. **CardStyle with 16px corner radius** added to `DesignTokens.xaml` and applied to all Stage-A page containers and row items. Why: matches Order 15 style v2 spec exactly (card 16px radius). Rejected: different corner radius (would diverge from spec).

15. **SharedTokenFile persists device/session token** to local app data (`kandoo.device-token.v1.json`); read by all pages that need Hub connection settings. Why: eliminates constructor parameter passing through navigation stack; single source of truth for the device token. Rejected: passing HubConnectionSettings through every page constructor (fragile; breaks when navigation depth changes).

16. **Empty/error states show copy-report button**; error text copied to clipboard via Clipboard API. Why: users need to capture and share error details with support. Rejected: showing raw exception text without copy ability (hard to report errors accurately).

## C-81b — Windows app Stage-A remaining pages

17. **SetupWizardPage is the app entry point** when no user profile exists (`App.xaml.cs`); writes profile + token to `SharedTokenFile`. Why: first-run experience must collect role + Hub settings before any Hub interaction. Rejected: keeping `RoleSelectionPage` as entry point (no Hub settings step).

18. **Invoice create is digital-first; print optional.** Item lines required; customer phone optional; operation idempotency via operation ID header. Why: digital invoice is the primary channel per Order 15; print is secondary. Rejected: print required (would block digital-only workflows).

19. **Invoice details page supports resend SMS and void-by-event** (recorded as local intent; Hub v1 has no direct void endpoint). Why: D4 requires SMS resend; void as event preserves immutability of issued invoices. Rejected: direct void endpoint (not in Hub v1 contract).

20. **Product editor has group/subgroup dropdown and stock adjustment recorded as an event.** Why: products need categorization; stock changes must be event-led per D10. Rejected: direct stock mutation (bypasses event ledger).

21. **Customers page lists customers** with phone + total display. Why: basic customer directory needed for invoice creation. Rejected: full customer CRUD (beyond Stage-A scope).

22. **Settings page shows Hub status/stats, demo SMS outbox, local DB backup.** Why: operators need to verify Hub connectivity and view demo SMS outbox. Rejected: real SMS gateway config (demo mode only).

23. **Every page has empty/error states with copy-report button** (Clipboard API). Why: consistent error reporting across all Stage-A pages. Rejected: per-page error handling (inconsistent UX).

24. **All Stage-A pages use CardStyle (16px radius) and are role-aware** (Store/Operator/Manager). Why: matches Order 15 style v2 spec; role-aware UI per existing `AppRolePolicy`. Rejected: different styling per page (visual inconsistency).

## CI / build

11. **MAUI workloads installed in CI** before building `Kandoo.sln`; ubuntu builds Android (`-p:TargetFramework=net8.0-android`), windows builds Windows (`-p:TargetFramework=net8.0-windows10.0.19041.0`). Why: `Kandoo.App` targets both platforms; hosted runners need `dotnet workload install maui` to build MAUI. Rejected: building both targets on every runner (Windows target cannot build on Linux).

## C-82 — Release APK, cold start, crash handler, target J8

12. **Cold start measured via `adb shell am start -W` TotalTime** on Android 10 emulator; result saved to `cold-start-time.txt` and uploaded as CI artifact `kandoo-app-c82-cold-start`. Why: standard Android cold start metric; needs emulator-based measurement. Rejected: measuring on physical Galaxy J8 (not available in CI).

13. **CardStyle StrokeShape removed** (`RoundRectangle RadiusX/RadiusY` unsupported in this MAUI version). Why: XamlC error XFC0009 blocked Windows build. Rejected: keeping RoundRectangle (build-breaking).

14. **All C-81b pages compiled** after adding missing `using System.Text.Json` and fixing `?? BodyStyle` references. Why: code-behind cannot reference XAML styles as bare C# identifiers. Rejected: per-page custom style classes (over-engineering).

15. **Release APK via AndroidRelease mode** with `EmbedAssembliesIntoApk=true`; APK inspection verifies managed DLLs inside APK. Why: Galaxy J8 crash was caused by missing managed DLLs in Debug APK. Rejected: Debug APK for release testing.

## C-83 — DeviceSecret only in Bearer header (not in JSON body)

16. **DeviceSecret removed from JSON body serialization** in ReplicationBatch via [JsonIgnore]. It is sent only via Authorization: Bearer header (DeviceBearer scheme per docs/openapi.yaml). Why: Cloud contract requires device secret in header only; serializing it in the body leaks the per-device secret in request logs and body snapshots. Rejected: keeping device_secret in JSON body (P-27 debt from PHP agent; Cloud rejects it).

17. **Legacy header deprecation recorded in openapi.yaml** (x-legacy-header-deprecation): X-Device-Key header and device_secret JSON body field deprecated in P-27, removed in C-83. Why: document the P-27 debt and the migration to DeviceBearer Authorization header. Rejected: silently removing without documentation (breaks API consumers).

## C-84 — Review Queue page + print fallback

18. **Print fallback saves preview to local file + clipboard** when print adapter returns PRINT_ADAPTER_DISABLED or PRINT_FAILED. Why: Galaxy J8 crash was caused by missing managed DLLs; print may not be available on all devices. Fallback ensures print intent is preserved. Rejected: silently ignoring print failures (operator would not know print was skipped).

19. **ReviewQueuePage uses ReviewQueueCoordinator** for fake/real mode switching. Why: consistent with existing Stage-A page pattern; single coordinator manages data source selection. Rejected: per-page data source instantiation (inconsistent).

20. **CheckBoxStyle added to DesignTokens.xaml** for consistent checkbox styling across Stage-A pages. Why: style v2 consistency. Rejected: inline checkbox styling (visual inconsistency).

## C-85 — setup.exe REJECTED at product level

21. **C-85 rejected: output is only Hub executable, not full Windows software.** Running Kandoo.Hub.exe starts only a local service on port 5000; browser page is local Pairing only, not the real software environment. `OwnerBootstrapProof` in `appsettings.json` is empty and the formal owner session creation path is unused. Install experience, first-run setup, automatic database creation, shop identity, and entry to the real environment were not proven.
   Why: owner ran C-85 output on a real machine and found product-level defect. Rejected: accepting C-85 as delivered.

## C-87 — Real installer + First-Run Shop Setup (activated)

22. **Installer output: standalone `Kandoo-Setup-x64.exe` downloadable from GitHub.** Why: owner requires a single downloadable file, double-click install, standard installation steps. Rejected: zip fallback or manual PowerShell/JSON steps.
23. **First-Run Setup via software's own GUI.** On first launch with no config, the app shows a graphical setup wizard that auto-creates database, configures services/settings/prerequisites, and resolves `OwnerBootstrapProof` graphically — no PowerShell, no terminal, no JSON editing, no manual token creation. Why: owner requires entry to real Kandoo environment without technical intervention. Rejected: Pairing page counts as real environment (it does not).

## Order 19 — Hub process lifetime (C-87 owner bootstrap fix)

24. **Hub keeps running after the first-run wizard closes.** The launcher starts `Kandoo.Hub.exe` on a free random localhost port, runs the owner-bootstrap proof + owner-session flow, saves the session token + Hub base URL to local app data, then launches `Kandoo.App.exe` and exits. The Hub process is intentionally not stopped so the app can continue using it. Why: the app needs the Hub to stay alive after setup completes; stopping it would break the real environment the wizard just created. Rejected: stopping Hub on wizard close (app would lose its LAN authority); making Hub a Windows service (no installer service yet; acceptable for this order).
25. **Hub is not auto-restarted on next Windows login/launch.** There is no installer service or startup task yet; the Hub is started by the launcher on each first-run. On subsequent launches, Kandoo.App reads the saved Hub base URL and session token and connects to an already-running Hub, or re-runs the launcher if the Hub is not reachable. Why: keeps scope minimal (no service code, no startup registry). Rejected: installer-registered Windows service for Hub (deferred to a later order).

## Order 20/21 — lane dispatch structure

26. **ONE agent per repo; parallelism is ACROSS repos only.** Exactly three agents run in parallel: C# -> `Kandoo` (`TASKS/CSHARP-T3.md`, single sequential chain C-98 -> C-96 G1–G4 -> Settings/Devices -> hardening), PHP -> `kandoo-cloud-php` (`TASKS/PHP-T3.md`), Python -> `Kandoo-Digital-Invoice` (`TASKS/PY-T3.md`). The earlier two-session C# dispatch (CSHARP-T3A/T3B) was merged into `CSHARP-T3.md` and both files deleted (owner ruling 2026-11-25: peer agents on ONE repo do NOT run in parallel). Why: owner clarified the parallel-agent capability is per-repo, not per-track within a repo. Rejected: two concurrent C# sessions with file-ownership partitioning (not supported by the owner's agent invocation model). **[LIFTED 2026-11-27 by the owner's direct order + M-013 PM-32 — see Decision 33.]**

## E-74 — accuracy scoring source (Python lane, M-010 sprint)

27. **E-74 scores against the certified fictional raster already in `samples/real-layout-fictional/`** (the invoice-34.png duplicate inside the approved capture samples) — Option 1 of the agent's question, confirmed 2026-11-25. The PY-T3 Stage 2 labels (invoice no. 34, date 1405.08.16, FOUR lines, payable 49,547,790) are the operator-verified labels (manager raster verification 2026-11-25; supersedes the R-008 five-line/49,547,706 reading). Honest-verification protocol: the agent re-reads the raster first; any contested field (line count, date, payable) is reported PARTIAL with crop citations, not scored; the 503,639 line-sum vs payable gap is resolved from the raster itself (visible discount row -> "invoice-level discount"; else "unexplained by the raster" with crops — no guessing). No new uploads; no real customer data (PM-06/PM-17). Why: the certified sample is the only PM-17-compliant raster available and the labels were already operator-verified against it; a new sanitized upload (option 2) adds delay with no evidentiary gain, and keeping E-74 blocked (option 3) contradicts the held evidence. Rejected: option 2 (new upload), option 3 (stay blocked). Encoded as addendum in `TASKS/PY-T3.md` on `Kandoo-Digital-Invoice` main (commit `6dd7bd8e`).

## E-74 training sample (M-012 Q5 / R-011 item 3; numbering canonical per R-012 item 3)

28. **E-74 training sample = a second, PO-certified real-layout + fictional-content sample, uploaded by the owner under `samples/real-layout-fictional/` in a NEW folder.** The lane's diagnosis is accepted: training on invoice 34 itself would invalidate the holdout, and the 4-of-8 quantity/line-total misreads need a second same-layout sample to fix. Option 2 APPROVED as the target state, with conditions (all mandatory): (a) the sample comes from the PO/owner — the lane does NOT fabricate, synthesize or substitute and reuses no real capture (PM-06/PM-17 unchanged); (b) PO-certified real-layout + fictional-content with a certified README; same layout family as invoice-34, DISTINCT content; (c) operator-confirmed quantity AND line-total for EVERY line; payable stays EXCLUDED from scoring and REPORT; (d) invoice 34 remains the ONLY official E-74 scoring source/holdout (Decision 27 unchanged); once the sample lands, the EXTRACTED-but-wrong = 0 gate is evaluated on BOTH rasters. Until the sample lands, official E-74 stays 0/9 PARTIAL (NOT-RUN ≠ zero) and the lane does not idle: Stage 3 (E-70) first, then Stage 4 (E-72 round 3; the C# verdict INCOMPATIBLE stands). Why: only the PO can certify a layout-identical, content-distinct sample without violating PM-06/PM-17, and the lane cannot fix systematic quantity/line-total misreads without an independent sample. Rejected: option 1 (score on invoice 34 alone — training on it invalidates the holdout), option 3 (stay blocked — contradicts M-011 "agents never idle"). First published as MANAGER RULING 2 in `TASKS/PY-T3.md` (`Kandoo-Digital-Invoice` main @ `8ad07bed`) and R-011 item 3. **OVERRIDDEN by Decision 30 below (owner direct order, 2026-11-27).**

## E-72 Cross-Lane Review (O17)

29. **E-72 Canonical Invoice v1.2 vector (tag o17-E-72, commit 1e029bd) is INCOMPATIBLE with C# CanonicalInvoiceConsumer — verdict unchanged after correction.** Three defect categories: (a) invoice wrapped under `base_invoice` instead of flat root; (b) all 16 `RootRequired` fields absent at root (schema_version, mode, invoice_id, display_number, operation_id, device_id, public_token, store_id, customer, state, state_history, line_items, summary, attention_cells, header, provenance); (c) 4 extra root fields not in `RootAllowed` (`base_invoice`, `synthetic_only`, `v1_2_draft_addendum`, `vector_id`) — note `v1_2_draft_addendum` carries v1.2-only content (`currency_unit`, `invoice_discount`, `payments`, `walk_in_customer`) with no v0 home and therefore needs a separate contract decision, not just flattening. C# is fail-fast: the single error is `SchemaValidationFailed | Unexpected field: base_invoice.` (CanonicalInvoiceConsumer.cs lines 109–112, 53–60) — not 14 accumulated errors. **PHP's objection is UPHELD in part:** the earlier C# claim that `image_sha256`/`template_library_id` were duplicated is FALSE — each string occurs exactly once in the 1518-byte vector (byte offsets 955 and 975), both inside `provenance`; the pre-issue 1e620cc fixes (quantity_millis, null display_number, "unavailable" provenance label) are all accepted. **PHP's conclusion is REJECTED:** placing those two fields inside `provenance` is exactly what the frozen contract requires (line 315), and C# is not wrong about field location — the defect is that `provenance` itself sits inside `base_invoice` instead of at root (line 140). Decisive test: promoting `base_invoice` to root makes the vector pass every C# validation step. No changes to frozen `CanonicalInvoiceConsumer.cs`; the fix belongs in the Python lane. [Renumbered from branch-local "28" to 29 at back-fill time per R-012 item 3; arrived on `main` via the PR #21 merge.]

## Owner override — boundaries lifted until project delivery (2026-11-27, R-014)

30. **The OWNER's direct order (2026-11-27): remove every boundary built so far, until project delivery, and move forward with speed only; executed immediately.** The owner CERTIFIES that no real data exists anywhere in this project — all invoice data in invoices and samples is FICTIONAL — and his certification as PO/data owner supersedes PM-06/PM-17 and the conditions below. Consequences: (a) **PM-06/PM-17 real-data restriction is LIFTED** for the remainder of the project; (b) **Decision 28 conditions (a) "sample comes from the PO/owner — the lane does NOT fabricate", (b) "PO-certified", (c) "operator-confirmed labels" are OVERRIDDEN** — the Python lane is authorized to fabricate/synthesize the second E-74 sample ITSELF, now (same print-layout family as invoice-34, distinct fictional content, exact self-labels by construction, README marked fabricated, under `samples/real-layout-fictional/lane-synthetic-01/`); (c) **honesty labeling replaces the data ban** — every generated artifact is clearly marked synthetic/fictional, never presented as a real scan; (d) **Decision 27 holdout unchanged** — invoice 34 stays the official E-74 scoring source with its Decision 27 labels; E-74 is re-scored on BOTH rasters once the synthetic sample lands, payable scored on the synthetic one (Decision 28(c)'s payable exclusion applied to an owner-provided sample only); (e) waiting-for-upload and approval gates on non-destructive lane steps (pushes, CI starts, draft -> ready, tags) are removed for all three lanes. Encoded as MANAGER RULING 3 in `TASKS/PY-T3.md` on `Kandoo-Digital-Invoice` `main` (commit `846a2016`) and R-014 (`docs/messages/R-014.md`, branch `arena/order-15-status` of this repo, commit `cc2f518`). KEPT (structural, flagged to the owner per his "report problems" instruction): Decision 26 one-agent-per-repo, lane/PM-PO merge discipline, PM-24 no-history-rewrite of `main`. Rejected: keeping the owner-upload gate (contradicts the owner's explicit order and his certification that all project data is fictional). **[Decision 26's one-agent rule LIFTED by M-013 PM-32 on 2026-11-27 — see Decision 33.]**

## M-013 execution (2026-11-27) — manager decisions under delegated authority (PM-30), one line each

31. **PM-30 delegated authority ACCEPTED:** the manager decides all technical, process, scope-order and merge questions directly and records each here in one line without waiting for an answer; the PM is consulted only for conflicts with the boards / `docs/product-ux-spec.md` / the product vision (`[م]` items) and anything that costs money; the PM audits afterwards.
32. **PM-31 agent merging:** agents MAY merge their OWN PRs into `main` once the required checks are green (workflow-touching PRs included, since agent credentials can write workflows); mandatory discipline: merge `origin/main` into the branch FIRST, never force-push or rewrite `main` (PM-24 stays), small PRs, squash or merge commit; the PM verifies `main` after the fact and the last merger fixes a red `main` first. The old "agents never merge" rule is LIFTED by M-013 §2.
33. **PM-32 team expansion:** Decision 26 (one agent per repo) and PM-29 are LIFTED by the owner (any team size authorized); TEN agents are dispatched per the path-ownership map in `TASKS/MVP1-TEAM.md` — C-PLAT, C-WIN-A, C-WIN-B, C-AND-STORE, C-AND-CUST, C-HOLOO on `Kandoo`; PHP-A, PHP-B on `kandoo-cloud-php`; PY-A, PY-B on `Kandoo-Digital-Invoice` — ONE owner per path at any time; touching another owner's path goes through a PR to that owner's branch.
34. **CI speed (C-PLAT first task, M-013 §4):** PR checks target under 8 minutes — NuGet + npm caching, Windows job split into parallel jobs (build / tests / installer / smoke), installer smoke gated to changes under `src/Kandoo.App`, `packaging/`, `scripts/` or on `main`, per-lane path filters, `concurrency` cancel of superseded runs; the Android 10 emulator job is REMOVED completely (it always fails; C-91 acceptance = signed-APK artifact + emulator-free tests + the PO's J8 test).
35. **PM-33 reporting:** no per-task Persian report files; status lives in PR descriptions (task, SHA, run URL, one-line result), tags, and one auto-generated `docs/STATUS.md` (small script run by CI on `main`: open PRs, last green run, tags, installer artifact link); the manager files at most one short R per day with BLOCKED items and the team plan; questions to the PM only when a PM decision is genuinely needed.
36. **PM-34 MVP-1 definition + countdown:** delivered when ALL are green on `main` — (1) DoD-A of M-010 items 1-4 (installer + first-run per the board, Windows v2 screens for Setup, Review, Invoice, Invoices, Products, Settings, Devices, local end-to-end with the SMS line in the outbox and the customer web page over the LAN, `demo-check.ps1`); (2) Android store app APK with pairing, products, invoice issue, review queue, invoices list (C-91 + C-97); (3) customer web page v2 (P-36) and customer mode (C-92 + P-31 vectors accepted); every merge to `main` publishes the installer artifact + signed APK; per-agent hour estimates and the three delivery dates in R-015, updated daily.
## C-HOLOO (O17) — Holoo read-only connector + capture-to-review integration

37. **Read-only `Microsoft.Data.SqlClient` 6.1.8 + `Microsoft.Data.Sqlite` 8.0.11 in `Kandoo.HolooConnector` (C-93 follow-ups):** implements D-M Tiers 1–3 (`SqlConnection` with `ApplicationIntent=ReadOnly`, fail-closed `HolooQueryGuard`, zero persisted admin passwords, automatic Tier 3 print-capture fallback), structural `structure.csv` + SQL `INFORMATION_SCHEMA` fingerprinting (`HolooSchemaVersion`), and durable SQLite shadow store + sync checkpoints while keeping `Kandoo.HolooDiscovery` strictly isolated per `Test-C52Boundaries.py`. Why: delivers real SQL Server + cross-platform synthetic SQLite verification without NU19xx warnings or breaking C-52 isolation. Rejected: persisting SQL admin secrets or skipping SQLite durability.
38. **Real H1-6 schema mapping & `LastSoldPrice` chronological precedence:** `HolooMapper` maps real `bundle/structure.csv` columns (`C_Tel`/`C_Mobile`/`Zip_Code`/`National_Code`, `ARTICLE.Sel_Price`, `ArticlePrice.Sel_Price`, `FACTURE`/`FACTART`) alongside bundle aliases; `HolooPriceResolver` treats `Sel_Price <= 0` as absent, picks the newest non-deleted sale invoice by parsed date (avoiding lexical US-date misordering and Jalali `< 1900` confusion), never falls back to `Buy_Price`, and every Holoo upsert forces `StockUnverified=true`. Why: real bundle data (`P-0001`, `F-0001` vs `F-0010`) proves `0` sale prices and non-chronological invoice IDs occur in Holoo H1-6 exports. Rejected: lexical date sorting or promoting `Buy_Price`.
39. **Fail-closed capture-folder -> review-item bridge inside `Kandoo.Capture` (`Kandoo.Core` reference):** adds `PersianNumberParser`, `CaptureFolderBuilder`, `CaptureIntakeValidator` (E-73 intake + batch duplicate/wrong-folder triage), `ICaptureFolderExtractor`/`CaptureFolderResultParser` (`capture-folder-result-v1`), `CaptureReviewItemAssembler` (self-verified through `CanonicalInvoiceConsumer.ConsumeForReview`), and `CaptureFolderSpoolSink`, while staging thin `src/Kandoo.Hub/` wiring via a Phase 2 PR against `C-PLAT`'s branch per PM-32. Why: delivers full end-to-end capture-to-canonical-review conversion strictly inside `C-HOLOO`-owned paths without circular project references or cross-owner merge conflicts. Rejected: modifying `C-PLAT`-owned `src/Kandoo.Hub/` files directly in Phase 1.


## C-PLAT CI-speed execution (M-013 §4, 2026-11-27)

40. **CI speed decisions (M-013 §4):** the single 8-minute Windows job became six lanes on the
    change-set graph (build / tests / Hub+smokes / app+screenshots / installer / installer-smoke)
    gated by one cheap ubuntu change-set job; NuGet is cached through a local composite action;
    `concurrency` cancels superseded pushes; docs-only pushes skip CI; the Android 10 emulator job
    is DELETED (never booted within the runner budget — three fix attempts, all failed) with its
    intent carried by the signed release APK artifact, `Kandoo.App.Tests` and the PO's J8 test.
    Measured on run `45964666504`: **8m04s -> 6m03s wall**, sum of job time 841 s across 8 jobs
    (free of charge: this repository is public, so Actions minutes are not billed — the win is
    reviewer latency and per-push runner load).
41. **Build artifacts are not shuttled between lanes:** each lane does its own restore+build
    (~13 s warm, NuGet cached) instead of downloading a shared `bin/` artifact, which for this
    solution would be hundreds of MB and cost more to move than to rebuild. Only small outputs
    travel: the Hub publish, the setup.exe, the app publish, and evidence JSON/logs.
42. **`Kandoo.App` is not in `Kandoo.sln` and references only `Kandoo.Core`**, so the MAUI lane
    does not wait for the solution build and does not repeat it (that alone was ~60 s of the old
    job and it sat on the critical path). Critical path is now app -> installer -> smoke.
43. **Artifact layout is resolved, not memorised:** `upload-artifact` roots an artifact at the
    least-common-ancestor of its paths, so the Hub publish (folder + two C-14 evidence files)
    arrives one level deeper than the app publish. Each artifact is now downloaded into a private
    `.ci-downloads/<artifact>` root and normalised from there by
    `scripts/Resolve-CiArtifactLayout.ps1`; sharing one download root was a real bug (it swept Hub
    files into the app folder) and is rejected.
44. **Installer + smoke gating:** both run only when `src/Kandoo.App`, `packaging/`, `scripts/` or
    a workflow changed, and always on `main`, so the D1 release kit keeps continuous evidence
    without taxing every PR. The change-set script fails SAFE: any doubt (API error, no base
    commit, truncated 300-file diff) runs every job, because a skipped job is invisible while a
    needless one only costs time.

## C-AND-CUST — customer mode (C-92/C-97 customer boards), 2026-11-27 (PM-30, one line each)

45. **C-97 customer chrome tie-break:** where the v1 boards and `Customer-Invoices-v2` disagree on shared chrome, the newer v2 board governs (bottom-nav middle tab reads «فروشگاه‌ها» per Customer-Invoices-v2 instead of «خرید (به‌زودی)» per v1/UX-spec §6, and the page background is wax `#F5E9C8` per v2); the tab stays disabled («به‌زودی», after MVP) per the UX spec; every state screen keeps its own board's content verbatim.
46. **Customer E2E strategy:** the accepted P-31 vectors (`docs/vectors/customer-api-v1-2-proposed.json`, PM-13 ACCEPT) are replayed end-to-end through the app's `CustomerApiClient` in the emulator-free CI lane against an in-memory double of the PHP customer API whose semantics are mirrored from its implementation (device-bound challenges, idempotency locks, replay rejection, one-shot revocation); the live leg runs against the real PHP Cloud when armed by `KANDOO_CLOUD_BASE_URL` (+ `KANDOO_CLOUD_OTP`/`KANDOO_CLOUD_OTP_FILE`), with `KANDOO_CLOUD_E2E_STRICT=1` as the D3 gate; a PHP+MariaDB live job in CI belongs to C-PLAT's workflow path (flagged, not touched).
47. **Android cleartext for the LAN kit:** `usesCleartextTraffic=true` in the Android manifest (Q-C98-01=A serves the kit over plain http on the LAN; the production default base URL stays `https://api.kandoo.cloud`); the demo override (`api_base_url`, `telegram_url`) lives in `kandoo.customer-config.json` in app data so no settings screen is invented.
48. **Customer detail display policy mirrors the PHP customer web page:** recorded values only, verbatim data-gap copy for `customer_name`/payments, `remaining_balance` shown as recorded and never computed, the unit word follows the recorded `currency_unit` (default ریال), and payment methods use board/spec wording («کارت‌خوان / کارت‌به‌کارت / نقدی / نسیه»).
49. **Role-switch plumbing is C-AND-CUST scope:** the routing-only edits in `App.xaml.cs` (start page per remembered role) and `RoleSelectionPage.xaml.cs` (`Select()` routes via `RoleStartPages`) belong to the assigned «role switch» task; `RoleSelectionPage`'s board-match surface (copy/layout) stays with C-WIN-A and is untouched.
50. **P-31 request bodies are serialized with `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` through the concrete generic `JsonSerializer.Serialize<T>` overload:** the default STJ encoder escapes `+` inside strings (`\+` in the wire body), which corrupts mobile numbers like `+989...` and would fail the PHP side's `^\+[1-9][0-9]{7,14}$` phone validation; API bodies are machine-consumed JSON (never HTML-embedded), so the relaxed encoder is safe, and the guard test `P31_Request_Body_Serialization_Keeps_Phone_Intact` pins the exact wire bytes.
