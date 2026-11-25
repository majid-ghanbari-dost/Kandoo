# گزارش ORDER 16 — ادغام PM-22 (یک PR یکپارچه)

**شاخه:** `arena/4bb6cd51-kandoo`
**نقطهٔ شروع:** `dca2e1e` (E-72 close-out)
**نقطهٔ پایان:** `932b17c`
**تاریخ:** 2026-11-25

---

## ۱. ادغام شش هد گمشده (M-009 §1.1)

همهٔ شش هد زیر با `git merge-base --is-ancestor` تأیید شدند که اکنون ancestor شاخه هستند:

| شاخه | SHA | نتیجهٔ ادغام |
|---|---|---|
| `arena/kandoo-c95-maui-launcher` | `c76046a` | پاک (فقط `docs/reports/ORDER-16-C-95.md`) |
| `arena/kandoo-c90-windows-store` | `15aadc6` | تعارض در `docs/reports/ORDER-16-C-90.md` → حل شد |
| `arena/kandoo-c91-android-store` | `39bca96` | پاک |
| `arena/kandoo-c92-customer-mode` | `992b0a0` | تعارض add/add در `docs/reports/ORDER-16-C-92.md` → حل شد |
| `arena/kandoo-c93-holoo-connector` | `e73b1c9` | تعارض در `HolooSchemaVersion.cs` و `HolooConnectorTests.cs` → حل شد (policy: این شاخه) |
| `pm/order21-selfcheck` | `f4debb2` | پاک (فقط `scripts/Invoke-InstallerSmoke.ps1`) |

**سیاست تعارض اعمال‌شده:** این شاخه برای کد/CI/تست برنده شد. `scripts/Invoke-InstallerSmoke.ps1` و `packaging/HubPilot/KandooHub.Installer.iss` عیناً از `pm/order21-selfcheck` آمدند و تأیید شد که هیچ تفاوتی با نسخهٔ PM ندارند. اسکریپت smoke ویرایش **نشد**.

### جزئیات حل تعارض C-93

`HolooSchemaVersion.cs`: نسخهٔ این شاخه نگه داشته شد (nullable `Known()` + `TryParse` + `TrustedVersions`) چون `Known()` شاخهٔ C-93 غیر-nullable بود و با تست‌ها ناسازگار می‌شد؛ نسخهٔ این شاخه API کامل‌تری دارد و `HolooConnectorTests` هم بر همان اساس نوشته شده است. `HolooConnectorTests.cs` نیز نسخهٔ این شاخه.

### اصلاح متن‌های خراب

سه گزارش ORDER-16 (C-90، C-92) حاوی متن‌های خراب از ادغام‌های قبلی بودند (نویسه‌های ترکیبی عبری/ویتنامی/چینی در میان فارسی). این‌ها در جریان حل تعارض تمیز شدند.

---

## ۲. خطاهای verbatim جاب شکست‌خورده و رفع آن‌ها

جاب شکست‌خورده: **`App tests + Windows + Android`**، گام `Publish self-contained Windows app and zip`، `exit_code=1` — روی `207ebeb` (و تکرار روی `6aea923`).

خطاهای verbatim استخراج‌شده از annotations چک‌ران:

```
src\Kandoo.App\Program.cs(12,23): error CS0017: Program has more than one entry point
  defined. Compile with /main to specify the type that contains the entry point.
  [src\Kandoo.App\Kandoo.App.csproj::TargetFramework=net8.0-windows10.0.19041.0]

src\Kandoo.App\Program.cs(29,26): error CS0103: The name 'HubLocator' does not exist
  in the current context

src\Kandoo.App\Program.cs(92,46): error CS0234: The type or namespace name
  'StringContent' does not exist in the namespace 'System.Net'
  (are you missing an assembly reference?)

src\Kandoo.App\SetupWizardPage.xaml.cs(126,32): error CS0103: The name 'hubDir'
  does not exist in the current context

CI_STEP_FAILURE step=Publish self-contained Windows app and zip exit_code=1
```

### رفع هر خطا

| خطا | علت ریشه‌ای | رفع |
|---|---|---|
| **CS0017** | SDK وین‌یو‌آی خودش `Main` در `App.g.i.cs` می‌سازد؛ با `Program.Main` (HOTFIX-1) برخورد دارد | `DISABLE_XAML_GENERATED_MAIN` فقط برای target ویندوز تعریف شد |
| **CS0103 `HubLocator`** | کلاس در namespace `Kandoo.App.Logic` است | `using Kandoo.App.Logic;` اضافه شد |
| **CS0234 `StringContent`** | در `System.Net.Http` است نه `System.Net` | `System.Net.Http.StringContent` |
| **CS0103 `hubDir`** | refactor HOTFIX-1 متغیر را حذف کرد ولی در `WorkingDirectory` مانده بود | از مسیر exe حل‌شده مشتق شد |

**نکتهٔ مهم در CS0017:** چون define تولید `Main` را حذف می‌کند، `Program.Main` باید خودش استارت WinUI را انجام دهد، وگرنه برنامه host را می‌سازد و بدون message loop خارج می‌شود (پنجره هیچ‌وقت ظاهر نمی‌شد). پس `Program.Main` اکنون `ComWrappersSupport.InitializeComWrappers()` و `Application.Start` با `DispatcherQueueSynchronizationContext` را انجام می‌دهد — دقیقاً همان کاری که `Main` تولیدی می‌کرد.

### خطاهای بعدی که پس از رفع چهار مورد بالا آشکار شدند

این‌ها زنجیره‌ای بودند؛ هر رفع، مرحلهٔ بعدی کامپایل را جلو می‌برد:

| # | خطای verbatim | علت | رفع |
|---|---|---|---|
| 1 | `MSB4025: An XML comment cannot contain '--'` | کامنت XML من شامل نام سوییچ با دو خط تیره بود | کامنت بازنویسی شد؛ همهٔ csprojها با XML parser اعتبارسنجی شدند |
| 2 | `CS0029: Cannot implicitly convert 'Kandoo.App.WinUI.App' to 'ApplicationInitializationCallbackParams'` | پارامتر lambda را `_` گذاشته بودم، پس `_ = new App()` به پارامتر انتساب می‌کرد نه discard | پارامتر به `callbackParams` تغییر نام یافت |
| 3 | `CS1729: 'ReturnsPage' does not contain a constructor that takes 1 arguments` | `MainPage` با `new ReturnsPage(settings)` صدا می‌زند ولی سازنده بی‌پارامتر بود | `ReturnsPage(HubConnectionSettings? settings = null)` — مطابق convention موجود `CustomersPage`/`SettingsPage` |
| 4 | `XamlC XFC0009: No property ... "BorderBrush"` | `StatesPage.xaml` از نام‌های WPF استفاده کرده بود | `Stroke` / `StrokeThickness` (نام‌های MAUI) |

پیش از push، همهٔ کلیدهای `StaticResource` در XAML بررسی شدند که تعریف دارند (هیچ خطای XamlC دیگری در صف نباشد).

---

## ۳. خطای تست هاب و رفع آن

جاب `Build and test (windows-latest)`:

```
Kandoo.Hub.Tests.WritePathVerificationTests.C67_PairingGrantDoesNotCreateOperationLogEntry:
Microsoft.Data.Sqlite.SqliteException : SQLite Error 1: 'duplicate column name: server_key_issued_at'.
```

**علت ریشه‌ای:** `EnsureColumn` به‌صورت read-then-write کار می‌کند (`PRAGMA table_info` و سپس `ALTER TABLE ADD COLUMN`). `Program.cs` یک `SqliteHubStore` روی connection string هاب می‌سازد و `HubApiFactory` هم مستقلاً یکی دیگر روی **همان** فایل دیتابیس تست. وقتی هر دو هم‌زمان initialize می‌شوند، یکی ممکن است ستون را بین PRAGMA و ALTER دیگری اضافه کند و ALTER با «duplicate column name» بشکند — در حالی که وضعیت نهایی مطلوب همان است.

**رفع:** فقط `SqliteErrorCode == 1` که پیامش «duplicate column name» دارد catch و موفق تلقی می‌شود؛ هر خطای دیگر SQLite همچنان propagate می‌شود. `EnsureColumn` تنها کانال همهٔ ۱۶ فراخوانی migration ستون است، پس جای دیگری نیاز به گارد ندارد. رفتار production تغییر نمی‌کند.

---

## ۴. بازگرداندن گیت نصب‌کننده (مهم)

`ci.yml` شاخه نسبت به `main` حدود ۲۴۰ خط کم داشت — از جمله کل بلوک installer smoke که دو آرتیفکت مورد نیاز گیت M-009 را تولید می‌کند (`kandoo-hub-c87-setup-exe`، `kandoo-install-smoke`) و خروجی `SMOKE_STEP=1..4`. این از یک نسل کهنه پس از ریست‌های مکرر شاخه مانده بود، نه یک تغییر عمدی.

سیاست تعارض PM-22 می‌گوید «این شاخه برای CI برنده است»، اما اعمال تحت‌اللفظی آن همان گیتی را حذف می‌کرد که PR یکپارچه باید ارضا کند. بنابراین نتیجه از `main` بازسازی شد:

```
ci.yml  =  main + فقط افزوده‌های C-93 این شاخه
           - گام «Test HolooConnector and write TRX» در هر دو جاب
           - افزودن TRX هالو به فهرست آرتیفکت ubuntu
```

`kandoo-app.yml` نسخهٔ شاخه نگه داشته شد؛ تنها تفاوتش یک کلید تکراری `continue-on-error` و یادداشت کهنهٔ KVM در `main` است.

**اعتبارسنجی:** ۵۱۹ خط، صفر tab، جاب‌های `build-and-test-linux` و `build-and-test-windows`، گام‌های smoke و هر دو نام آرتیفکت موجود.

---

## ۵. نتیجهٔ CI روی نقطهٔ نهایی

**کامیت:** `932b17c`
**رن:** [45571592358](https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/45571592358)

| جاب | نتیجه |
|---|---|
| `Build and test (ubuntu-latest)` | ✅ success |
| `Build and test (windows-latest)` | ✅ success |
| `Install and smoke-test the installer (C-87-fix-12)` | ✅ success |

### گیت‌های M-009 §2

| گیت | وضعیت |
|---|---|
| CI سبز روی هر دو جاب | ✅ |
| `SMOKE_ALL_HARD_CHECKS_PASSED` | ✅ (فقط پس از گذر همهٔ hard checks چاپ می‌شود؛ گام exit 0) |
| آرتیفکت `kandoo-hub-c87-setup-exe` | ✅ ۸۹,۸۳۸,۴۲۹ بایت |
| آرتیفکت `kandoo-install-smoke` | ✅ ۲۲,۹۱۸ بایت |

### رن‌های شاهد برای HOTFIX-1 (اصلاح‌شده)

| کامیت | ورکفلو | رن | نتیجه |
|---|---|---|---|
| `29a9c3a` | Kandoo App | [45568994935](https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/45568994935) | ✅ success (جاب `App tests + Windows + Android` سبز شد) |
| `51b902f` | CI | [45570803987](https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/45570803987) | ✅ ubuntu + windows |
| `932b17c` | CI | [45571592358](https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/45571592358) | ✅ ubuntu + windows + installer smoke |

نکته: جاب `Android 10 emulator launch + 15s liveness` همچنان fail است ولی `continue-on-error: true` دارد (PM-16) و رن کلی `Kandoo App` را سبز نمی‌شکند — همان‌طور که روی `main` هم هست.

---

## ۶. وضعیت تسک‌ها در PR یکپارچه

| تسک | شاخه | SHA | تگ |
|---|---|---|---|
| C-90 | `arena/kandoo-c90-windows-store` | `15aadc6` | `o16-c90` |
| C-91 | `arena/kandoo-c91-android-store` | `39bca96` | `o16-C-91` |
| C-92 | `arena/kandoo-c92-customer-mode` | `992b0a0` | `o16-c92` |
| C-93 | `arena/kandoo-c93-holoo-connector` | `e73b1c9` | — |
| C-95 | `arena/kandoo-c95-maui-launcher` | `c76046a` | `o16-c95` |
| C-96 g1–g4 | `arena/4bb6cd51-kandoo` | `6aea923` | `o16-C-96-1..4` |
| HOTFIX-1 | `arena/4bb6cd51-kandoo` | `207ebeb` (اصلاح: `7844b3d`→`29a9c3a`) | `o16-HOTFIX-1` |
| pm/order21 | `pm/order21-selfcheck` | `f4debb2` | — |

---

## ۷. اقدام لازم PM

1. PR یکپارچه را بررسی و merge کند (PO اطلاع داده شود — workflow-touching).
2. پس از merge، #12، #13، #14، #15، #16، #18، #19 با اشاره به شمارهٔ PR یکپارچه بسته شوند.
3. تصمیم دربارهٔ تگ `o17-E-72-REVIEW` (روی کامیت `ffe6c05` با گزارش نادرست) — جابه‌جا نشد.
4. دسترسی نوشتن روی `Kandoo-Digital-Invoice` برای کامنت PR #16 (فعلاً ۴۰۳).
