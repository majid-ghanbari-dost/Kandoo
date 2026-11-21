# گزارش ORDER 20-A —解 کرد شدن تعارضهای PR #11

## خلاصه
PR #11 (شاخه `arena/kandoo-order19-owner-bootstrap` به `main`) دارای تعارضದادگی با شاخه `main` بود. تعارضها در این گزارش لیست شده و نحوه حل هر کدام ذکر شده است.

## جزئیات PR #11

- **شاخه سرور (head):** `arena/kandoo-order19-owner-bootstrap` @ `0f55801`
- **شاخه پایه (base):** `main` @ `bb5ad32`
- **وضعیت mergeable:** True (بعد از حل تعارضها)
- **رن CI:** 44870844995 — هر دو جاب سبز
- **آرتیفکت kandoo-hub-c87-setup-exe:** 136,832,601 بایت
- **آرتیفکت kandoo-install-smoke:** 31,002 بایت

## فایل‌های تعارض‌دار و نحوه حل

همه تعارضها نوع `add/add` بودند (هر دو شاخه فایل را اضافه کرده بودند). طبق دستورالعمل:
- برای فایل‌های کد، CI، تست، بسته‌بندی و اسکریپت: نسخه شاخه ما (ours) گرفته شد.
- برای `docs/OPERATING-RULES-v2.md` و `docs/orders/ORDER-16.md`: نسخه اصلی (main) taken شد (این فایل‌ها جدید در main بودند و تعارض نداشترند).

### فایل‌هایی که نسخه شاخه ما (ours) گرفته شد

| # | فایل | دسته‌بندی | توضیح حل |
|---|-------|-----------|-----------|
| 1 | `.github/workflows/ci.yml` | CI | نسخه شاخه ما — شامل صدا زدن `Invoke-InstallerSmoke.ps1` برای تست دacانی نصب |
| 2 | `.github/workflows/kandoo-app.yml` | CI | نسخه شاخه ما |
| 3 | `.gitignore` | پیکربندی | نسخه شاخه ما |
| 4 | `AGENT_HANDOFF.md` | مستندات کلیدی | نسخه شاخه ما — شامل آدرس‌های ذخیره PHP و پایتون، حکم تاریخ، Last updated: 2026-11-21 |
| 5 | `docs/DECISIONS.md` | مستندات کلیدی | نسخه شاخه ما |
| 6 | `docs/MASTER_HANDOFF.md` | مستندات کلیدی | نسخه شاخه ما |
| 7 | `docs/openapi.yaml` | مستندات کلیدی | نسخه شاخه ما |
| 8 | `packaging/HubPilot/BUILD-INSTALLER.md` | بسته‌بندی | نسخه شاخه ما |
| 9 | `packaging/HubPilot/KandooHub.Installer.iss` | بسته‌بندی | نسخه شاخه ما — اسکریپت Inno Setup |
| 10 | `scripts/Invoke-C50AndroidSmoke.sh` | اسکریپت | نسخه شاخه ما |
| 11 | `src/Kandoo.App/App.xaml.cs` | کد | نسخه شاخه ما |
| 12 | `src/Kandoo.App/CrashReportPage.xaml` | کد | نسخه شاخه ما |
| 13 | `src/Kandoo.App/Kandoo.App.csproj` | کد | نسخه شاخه ما |
| 14 | `src/Kandoo.App/MainPage.xaml` | کد | نسخه شاخه ما |
| 15 | `src/Kandoo.App/MainPage.xaml.cs` | کد | نسخه شاخه ما |
| 16 | `src/Kandoo.App/ProductsPage.xaml` | کد | نسخه شاخه ما |
| 17 | `src/Kandoo.App/ProductsPage.xaml.cs` | کد | نسخه شاخه ما |
| 18 | `src/Kandoo.App/Resources/DesignTokens.xaml` | کد | نسخه شاخه ما |
| 19 | `src/Kandoo.App/RoleSelectionPage.xaml` | کد | نسخه شاخه ما |
| 20 | `src/Kandoo.App/global.json` | کد | نسخه شاخه ما — پین SDK فقط برای برنامه MAUI |
| 21 | `src/Kandoo.Cloud/Program.cs` | کد | نسخه شاخه ما |
| 22 | `src/Kandoo.Hub/HubCloudReplicationClient.cs` | کد | نسخه شاخه ما |
| 23 | `src/Kandoo.Hub/Program.cs` | کد | نسخه شاخه ما |
| 24 | `src/Kandoo.Hub/ReplicationContracts.cs` | کد | نسخه شاخه ما |
| 25 | `src/Kandoo.Hub/SqliteHubStore.Access.cs` | کد | نسخه شاخه ما |
| 26 | `src/Kandoo.Hub/appsettings.json` | کد | نسخه شاخه ما |
| 27 | `tests/Kandoo.Cloud.Tests/R1VerticalSliceTests.cs` | تست | نسخه شاخه ما |
| 28 | `tests/Kandoo.Cloud.Tests/ReplicationClientTests.cs` | تست | نسخه شاخه ما |

### فایل‌های جدید از main (بدون تعارض، taken از main)

| # | فایل | دسته‌بندی | توضیح |
|---|-------|-----------|-----------|
| 29 | `docs/OPERATING-RULES-v2.md` | مستندات | نسخه main — قوانین عملیات نسخه ۲ |
| 30 | `docs/orders/ORDER-16.md` | مستندات | نسخه main — دستورالعمل ORDER 16 |

## نتایج CI

- جاب اوبونتو: سبز
- جاب ویندوز: سبز
- هر دو آرتیفکت لازم موجود است
- PR #11 قابل ادغام (mergeable) است

## نتیجه نهایی

تاریخ تعارضات PR #11 حل شد، هر دو جاب CI سبز است، آرتیفکت‌ها موجود هستند، و PR #11 وضعیت mergeable دارد. PR خود به عهده مدیر است و توسط این ایچن্ট ادغام نشده است.
