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

## CI / build

11. **MAUI workloads installed in CI** before building `Kandoo.sln`; ubuntu builds Android (`-p:TargetFramework=net8.0-android`), windows builds Windows (`-p:TargetFramework=net8.0-windows10.0.19041.0`). Why: `Kandoo.App` targets both platforms; hosted runners need `dotnet workload install maui` to build MAUI. Rejected: building both targets on every runner (Windows target cannot build on Linux).
