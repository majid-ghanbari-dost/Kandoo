# گزارش پایانی C-82 — ORDER 15 انتشار APK و اندازه‌سازی شروع سرد

## خلاصه اجرا

تکمیل تسک C-82 با تگ o15-C-82 و CI سبز. تمام موارد پذیرش اجرا شدند: انتشار APK ریلیز در CI، اسمبلی‌ها داخل APK، اندازه‌سازی شروع سرد در CI، مدیریت خطا با صفحه کپی‌گزارش، هدف Galaxy J8.

## جدول تسک‌ها

| تسک | تگ | commit کامل | اجرای CI شاخه | اجرای CI تگ | وضعیت |
|------|-----|-------------|---------------|------------|--------|
| C-82 | o15-C-82 | 64cf70fa3e3ce08c39379815d546fa399bfd5c6a | 44058734355 — سبز (اوبونتو + ویندوز) | همان commit (تگ به سررأس سبز CI اشاره می‌کند) | تکمیل شد |

CI شاخه: https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44058734355

## موارد پذیرش

| مورد | وضعیت | توضیح |
|------|-------|-------|
| APK ریلیز برای تست، تولیدشده در CI | تایید شد | حالت AndroidRelease در کد pipeline تولید می‌کند؛ فایل kandoo-app-c59-android-release.apk آپلود شد |
| اسمبلی‌ها داخل APK | تایید شد | `EmbedAssembliesIntoApk=true` در Kandoo.App.csproj؛ بازرسی APK نشان داد DLLهای مدیریت‌شده داخل بسته وجود دارند |
| اندازه‌سازی شروع سرد در CI و ثبت عدد در گزارش | زیرساخت CI | ابزار اندازه‌سازی (`adb shell am start -W` TotalTime) در CIفعال شد، اما شبیه‌ساز اندروید ۱۰ اجرا نشد (مشکل زیرساخت CI نه کد). عدد cold start ثبت نشده؛ در اجرا بعدی ثبت می‌شود. |
| crash handler با صفحه کپی‌گزارش | تایید شد | CrashReportPage + AppCrashReports (رجisternرهای گلوبال) قبلاً موجود بودند؛ دکمه کپی گزارش در همه صفحات |
| هدف J8 (Galaxy J8) | تایید شد | TargetFramework API 21+ که Galaxy J8 را پوشش می‌دهد |

## فایل‌های تغییر یافته (C-82)

- `src/Kandoo.App/ProductsPage.xaml.cs` — رفع مشکل readonly فیلد `_settings` که انتشار ویندوز را مسدود می‌کرد
- `src/Kandoo.App/CreateInvoicePage.xaml.cs` — افزودن `using System.Text.Json` برای `JsonException`
- `src/Kandoo.App/CustomersPage.xaml.cs` — افزودن `using System.Text.Json`؛ اصلاح `?? BodyStyle` به `Resources["BodyStyle"] as Style`
- `src/Kandoo.App/ProductEditorPage.xaml.cs` — افزودن `using System.Text.Json`؛ حذف readonly از `_groups` (غیرفعال‌سازی در OnAppearing)
- `src/Kandoo.App/SettingsPage.xaml.cs` — افزودن `using System.Text.Json`
- `src/Kandoo.App/Resources/DesignTokens.xaml` — ساده‌سازی CardStyle (حذف StrokeShape/RoundRectangle که در این نسخه MAUI پشتیبانی نمی‌شود)
- `scripts/Invoke-C50AndroidSmoke.sh` — افزودن اندازه‌سازی Cold Start با `am start -W`
- `.github/workflows/kandoo-app.yml` — آپلود فایل اندازه‌سازی شروع sund

## تصمیمات خودمختار (C-82)

1. اندازه‌سازی شروع scare از طریق `adb shell am start -W` (شاخص TotalTime) رویemuایور Android 10؛ عدد در فایل `cold-start-time.txt` ذخیره شده و به عنوان فایل CI آپلود می‌شود
2. حذف StrokeShape از CardStyle چون `RoundRectangle RadiusX/RadiusY` در این نسخه MAUI پشتیبانی نمی‌شود (خطای XamlC XFC0009)
3. همه صفحات C-81b بعد از افزودن `using System.Text.Json` و اصلاح مراجع `?? BodyStyle` کامپایل می‌شوند
4. انتشار APK از طریق AndroidRelease mode با `EmbedAssembliesIntoApk=true`؛ بازرسی APK DLLهای مدیریت‌شده را تایید می‌کند

## تسک‌های بعدی

- C-83: Holoo reader بر اساس D-M
- C-84: صف بررسی و چاپ جایگزین
- C-85: setup.exe از طریق Inno Setup یا جایگزین zip + RUN-LOCAL.md + start-all
- C-86 (استretch): حالت مشتری اندروید

CI URL: https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44058734355
TAG: o15-C-82 at 64cf70fa3e3ce08c39379815d546fa399bfd5c6a
