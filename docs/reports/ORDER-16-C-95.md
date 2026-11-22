# گزارش ORDER 16 — تسک C-95

## خلاصه

تسک C-95: جایگزینی رونهای موقت WinForms (`src/Kandoo.App.Launcher`) با صفحه راه‌اندازی MAUI (`SetupWizardPage`) طراحی v2. برنامه واحد، هیچ راه‌اندازی‌کننده جداگانه‌ای ندارد. همان جریان راه‌اندازی اولیهٔ فروشگاه (نام فروشگاه → راه‌اندازی هاب محلی روی بندر آزاد → نشست مالک → برنامه).

**وضعیت: COMPLETE** — نصب‌ساز و smoked-installer با چیدمان v2 (بدون راه‌اندازی‌کننده در ریشه) هر دو جاب CI سبز هستند.

## جدول نتایج

| وظیفه | شاخه | SHA کامل | آدرس رن CI | نتیجه |
|-------|-------|---------|-----------|-------|
| C-95 | `arena/kandoo-c95-maui-launcher` | `08950a5de6f992a7866a359963d14f6864b2ceb7` | https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44924669492 | سبز (ubuntu + windows) |

## ویژگی‌های انجام شده

| ویژگی | فایل | توضیح |
|-------|-------|-------|
| برنامه واحد (بدون launcher) | `src/Kandoo.App/App.xaml.cs` | `SetupWizardPage` نقطه ورود است وقتی پروفایل وجود ندارد |
| صفحه راه‌اندازی فروشگاه | `src/Kandoo.App/SetupWizardPage.xaml.cs` | نام فروشگاه → هاب محلی روی بندر آزاد → نشست مالک → برنامه |
| بندر هاب آزاد | `SetupWizardPage` | `GetFreePort()` از `TcpListener` روی loopback، پند ۰ |
| توقف هاب قبلی | `SetupWizardPage.StopPreviousHub()` | هاب اجرا شده در اجرا قبلی متوقف می‌شود |
| OwnerBootstrapProof | `SetupWizardPage` | GUID تولید شده، منتقل می‌شود به هاب از طریق متغیر محیطی |
| نشست مالک | `SetupWizardPage` | POST `/v1/stores/{id}/owner-sessions` با Bearer proof |

## نصب‌ساز و smoked-installer

نصب‌ساز `Kandoo-Setup-x64.exe` بدون راه‌اندازی‌کننده جداگانه ساخته می‌شود.

**Installer smoke (Invoke-InstallerSmoke.ps1):** اجرا شده و **سبز** در رن CI `44924669492` (job windows-latest). تمامی hard checks پاس شدند:
- install: نصب خاموش، کد خروج ۰
- layout: `hub\Kandoo.Hub.exe` و `app\Kandoo.App.exe` وجود دارند، هر پوشه runtime جداگانه دارد، nessun exe در ریشه نصب، هیچ `Kandoo.SetupLauncher.exe` surviving نیست
- hub-health: `GET /health = 200` پس از启动 هاب از پوشه نصب
- maui-app (soft): برنامه仍 در حال اجرا بعد از ۱۵ ثانیه

## notices

- تگ `o16-c95` روی کامیت `08950a5de6f992a7866a359963d14f6864b2ceb7` ایجاد شده است.
- PR #12 ایجاد شده و در انتظار بررسی مدیر است.

## نکته برای مدیر (PM)

اسکریپت `scripts/Invoke-InstallerSmoke.ps1` هنوز بررسی `Kandoo.SetupLauncher.exe` را در ریشه نصب دارد (check "layout": اگر فایل وجود داشته باشد، خطا می‌دهد). با حذف راه‌اندازی‌کننده، این بررسی jetzt پاس می‌دهد (فایل وجود ندارد). اما PM باید اسکریپت را بررسی کند تا با چیدمان جدید C-95 هم完全调ंभ باشد.

## نتیجه نهایی

CI هر دو جاب سبز است (ubuntu-success, windows-success). Installer smoke با تگ `o16-c95` روی کامیت سبز اجرا شده و پاس شده است. PR #12 در انتظار بررسی مدیر است.
