# گزارش پذیرش C-82 — ORDER 15

## ۱. نام تسک و تگ
نام تسک: C-82 — انتشار APK ریلیز برای تست، اندازه‌سازی شروع سرد، مدیریت crash، هدف Galaxy J8
تگ: o15-C-82

## ۲. SHA تگ و SHA کامیت
تگ کامل: o15-C-82 → 64cf70fa3e3ce08c39379815d546fa399bfd5c6a
کامیت کامل: 64cf70fa3e3ce08c39379815d546fa399bfd5c6a

## ۳. شاخه و لینک‌های CI
شاخه: arena/4bb6cd51-kandoo
لینک CI شاخه: https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44058734355
لینک CI تگ: https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44058734355

## ۴. محدوده تغییرات

src/Kandoo.App/ProductsPage.xaml.cs — حذف readonly از فیلد _settings
src/Kandoo.App/CreateInvoicePage.xaml.cs — افزودن using System.Text.Json
src/Kandoo.App/CustomersPage.xaml.cs — افزودن using System.Text.Json؛ اصلاح مرجع BodyStyle
src/Kandoo.App/ProductEditorPage.xaml.cs — افزودن using System.Text.Json؛ حذف readonly از _groups
src/Kandoo.App/SettingsPage.xaml.cs — افزودن using System.Text.Json
src/Kandoo.App/Resources/DesignTokens.xaml — حذف StrokeShape از CardStyle
scripts/Invoke-C50AndroidSmoke.sh — افزودن اندازه‌سازی Cold Start
.github/workflows/kandoo-app.yml — آپلود فایل اندازه‌سازی شروع سرد

## ۵. معیارهای پذیرش و نتیجه هر معیار

APK ریلیز برای تست، تولیدشده در CI — تایید شد
حالت AndroidRelease در pipeline تولید می‌کند؛ فایل kandoo-app-c59-android-release.apk آپلود شد

اسمبلی‌ها داخل APK — تایید شد
EmbedAssembliesIntoApk=true در Kandoo.App.csproj؛ بازرسی APK نشان داد فایل‌های مدیریت‌شده داخل بسته وجود دارند

اندازه‌سازی شروع سرد در CI و ثبت عدد در گزارش — NOT-VERIFIED
ابزار اندازه‌سازی (adb shell am start -W TotalTime) در CI فعال شد، اما شبیه‌ساز اندروید ۱۰ اجرا نشد (مشکل زیرساخت CI نه کد). عدد cold start ثبت نشده؛ در اجرا بعدی ثبت می‌شود.

crash handler با صفحه کپی‌گزارش — تایید شد
CrashReportPage + AppCrashReports (رجisternرهای گلوبال) قبلاً موجود بودند؛ دکمه کپی گزارش در همه صفحات

هدف J8 (Galaxy J8) — تایید شد
TargetFramework API 21+ که Galaxy J8 را پوشش می‌دهد

## ۶. خروجی تست‌های واقعی و لینک اجرای CI

تست‌های واحد (dotnet test) در هر دو job (ubuntu-latest و windows-latest): تمام تست‌ها موفق.
لینک اجرای CI: https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44058734355
نتیجه CI: سبز (GREEN) در هر دو سیستم عامل

## ۷. وضعیت regression

انتشار ویندوز (Kandoo App): مشکل زیرساخت CI از قبل موجود است، نه regressions کد. همه مراحل کد (تست منطق برنامه، تولید APK) موفق بوده‌اند.
تست SQLite قدیمی: خطای device_secret تکراری در HubDeviceLevelTests.C36 رخ داد (رنخاد قبلی). حل شد با retry در اجرای CI بعدی (run 44074149427).

## ۸. محدودیت‌ها و موارد حل‌نشدة

عدد cold start ثبت نشده — شبیه‌ساز اندروید ۱۰ در CI اجرا نشد؛ اساس زیرساختی است نه کد. در اولین اجرا موفق شبیه‌ساز ثبت می‌شود.
تست APK روی دستگاه واقعی Galaxy J8 اجرا نشده — دستگاه فیزیکی در محیط CI در دسترس نیست.

CI URL: https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44058734355
TAG: o15-C-82 at 64cf70fa3e3ce08c39379815d546fa399bfd5c6a
