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
