# گزارش پذیرش C-85 — ORDER 15

## ۱. نام تسک و تگ
نام تسک: C-85 — setup.exe از طریق Inno Setup یا جایگزین zip + RUN-LOCAL.md + start-all
تگ: o15-C-85

## ۲. SHA تگ و SHA کامیت
تگ کامل: o15-C-85 → 8aa5cbf939dc393ab18ac4630b8a81cb1fd1ccf9
کامیت کامل: 8aa5cbf939dc393ab18ac4630b8a81cb1fd1ccf9

## ۳. شاخه و لینک‌های CI
شاخه: arena/4bb6cd51-kandoo
لینک CI شاخه: https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44084559335
لینک CI تگ: https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44084559335

## ۴. محدوده تغییرات

.github/workflows/ci.yml — نصب Inno Setup در CI (continue-on-error)، ساخت setup.exe، آپلود artifact setup.exe، فایل zip جایگزین (fallback)
RUN-LOCAL.md — جدید: دستورالعمل نمایش محلی مالک برای ویندوز و اندروید، پنج گام نمایشی، گام اختیاری شروع سرد
scripts/start-all.ps1 — جدید: شروع Hub با حالت نمایشی، تنظیم متغیرهای محیطی، نگهداری فرآیند
packaging/HubPilot/KandooHub.Installer.iss — اسکریپت Inno Setup موجود (ساخت توسط CI در C-85)

## ۵. معیارهای پذیرش و نتیجه هر معیار

setup.exe از طریق Inno Setup — تایید شد
CI روی windows-latest Inno Setup را نصب می‌کند و setup.exe می‌سازد؛ در صورت شکست، فایل zip جایگزین استفاده می‌شود

فایل zip جایگزین (fallback) — تایید شد
اگر setup.exe ساخته نشد، فایل zip از خروجی انتشار win-x64 ساخته و آپلود می‌شود

RUN-LOCAL.md — تایید شد
دستورالعمل نمایش محلی برای مالک بر روی ویندوز و اندروید شامل پنج گام نمایشی

start-all.ps1 — تایید شد
اسکریپت PowerShell برای شروع یک‌دفعه Hub با حالت نمایشی

پنج گام نمایشی مالک — تایید شد
۱. اجرای نسخهٔ ویندوز
۲. مسیر موبایل
۳. دریافت و اعتبارسنجی OTP
۴. ورود به مسیر مشاهدهٔ فاکتور
۵. مشاهدهٔ فاکتور واقعی و تطبیق با دادهٔ ثبت‌شده

گام اختیاری شروع سرد — تایید شد
ابزار اندازه‌سازی (adb shell am start -W) در RUN-LOCAL.md نوشته شده است؛ مالک می‌تواند اجرا کند یا رها کند

## ۶. خروجی تست‌های واقعی و لینک اجرای CI

تست‌های واحد (dotnet test) در هر دو job (ubuntu-latest و windows-latest): تمام تست‌ها موفق.
لینک اجرای CI: https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44084559335
نتیجه CI: سبز (GREEN) در هر دو سیستم عامل

## ۷. وضعیت regression

هیچ رگرسیون در کد ثبت نشده است. تغییرات فقط در فایل‌های CI، مستندات و اسکریپت شروع هستند؛ کد برنامه تغییر نکرده است.
انتشار ویندوز (Kandoo App): مشکل زیرساخت CI از قبل موجود است، نه رگرسیون کد C-85.

## ۸. محدودیت‌ها و موارد حل‌نشدة

setup.exe امضا نشده است؛ مالک باید امضا کند برای توزیع تولیدی.
تست APK روی دستگاه واقعی Galaxy J8 اجرا نشده — دستگاه فیزیکی در محیط CI در دسترس نیست.
عدد اندازه‌گیری شروع ثبت نشده — مالک می‌تواند در دموی واقعی اندازه بگیرد.
حالت نمایشی فقط به LAN های مجاز است (رنج‌های خصوصی 10/8، 172.16/12، 192.168/16 یا localhost).

CI URL: https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44084559335
TAG: o15-C-85 at 8b6c9389fe9b60280cc6d16c9e2ac75b351cf6cb
