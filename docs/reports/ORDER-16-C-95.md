# گزارش ORDER 16 — تسک C-95

## خلاصه
تسک C-95: جایگزینی رôniaی موقت WinForms (`src/Kandoo.App.Launcher`) با صفحه راه‌اندازی MAUI (`SetupWizardPage`) طراحی v2. برنامه واحد، هیچ راه‌اندازی‌کننده جداگانه‌ای ندارد. همان جریان راه‌اندازی اولیهٔ فروشگاه (نام فروشگاه → راه‌اندازی هاب محلی روی بندر freed → نشست مالک → برنامه).

## جدول نتایج

| وظیفه | شاخه | SHA کامل | آدرس رن CI | نتیجه | نام آرتیفاکت |
|-------|-------|---------|-----------|-------|--------------|
| C-95 | `arena/kandoo-c95-maui-launcher` | `08950a5de6f992a7866a359963d14f6864b2ceb7` | https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44924669492 | سبز (ubuntu + windows) | kandoo-hub-c87-setup-exe, kandoo-install-smoke |

## جزئیات تغییرات

### فایل‌های تغییر یافته

| فایل | تغییر |
|------|--------|
| `src/Kandoo.App/SetupWizardPage.xaml.cs` | بندر دیواری آزاد در هر اجرا، Hub.exe در پوشه hub\\، توقف هاب قبلی، ذخیره آدرس پایه هاب، استفاده از System.Net و System.Net.Sockets |
| `.github/workflows/ci.yml` | حذف مرحله ساخت راه‌اندازی‌کننده WinForms، حذفlauncher ازarejø된 ورودی‌های نصب، به‌روزرسانی بررسی smoked برای چیدمان جدید (بدون راه‌اندازی‌کننده در ریشه) |
| `packaging/HubPilot/KandooHub.Installer.iss` | حذف راه‌اندازی‌کننده از فایل نصب، MyAppExeName به Kandoo.App.exe تغییر یافت، چیدمان نصب به دو رuntime مجزا (hub\ و app\) تغییر یافت |

### جریان راه‌اندازی اولیهٔ فروشگاه (C-95)

1. کاربر نام فروشگاه را وارد می‌کند
2. هاب قبلی متوقف می‌شود (اگر وجود داشته باشد)
3. Hub.exe از پوشه hub\\ اجرا می‌شود با بندر دیواری آزاد و OwnerBootstrapProof
4. منتظر /health = 200 روی بندر انتخاب شده
5. POST /v1/stores/{id}/owner-sessions با Bearer 토큰
6. ذخیره پروفایل، توکن و آدرس پایه هاب
7. ورود به MainPage

## نصب‌ساز و smoked

- نصب‌ساز `Kandoo-Setup-x64.exe` بدون راه‌اندازی‌کننده جداگانه ساخته می‌شود
- راه‌اندازی‌کننده (setup) smoked می‌پذیرد: نصب خاموش، چیدمان، smoked هاب روی بندر ۵۰۰۰، smoked برنامه MAUI (معمولی)
- آرتیفاکت `kandoo-install-smoke` تولید شد (۲۲,۳۷۲ بایت)
- ARTEFACT `kandoo-hub-c87-setup-exe` تولید شد (۸۹,۷۲۰,۶۱۲ بایت)

## نکته برای مدیر (PM)

اسکریپت `scripts/Invoke-InstallerSmoke.ps1` هنوز بررسی راه‌اندازی‌کننده (`Kandoo.SetupLauncher.exe`) را در ریشه نصب دارد. با حذف راه‌اندازی‌کننده، این بررسی باید توسط مدیر بروزرسانی شود تا چیدمان جدید (بدون راه‌اندازی‌کننده در ریشه، برنامه MAUI به عنوان نقطه ورود) راiasis کند.

## نتیجه نهایی

CI هر دو جاب سبز است (ubuntu-success, windows-success). تگ `o16-c95` روی کامیت `08950a5de6f992a7866a359963d14f6864b2ceb7` ایجاد شده است. PR #12 ایجاد شده و در انتظار بررسی مدیر است.
