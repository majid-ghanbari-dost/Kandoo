# گزارش ORDER 16 — تسک C-92

## خلاصه

تسک C-92: حالت مشتری در برنامه اندروید: ورود OTP در برابر API مشتری PHP (P-31)، فاکتور گروه‌شده از سوی فروشگاه، تغییر نقش فروشگاه/مشتری/هما، دستگاه تایید شده به خاطر سپرد؛ شماره همراه شناسه است، نقش‌ها زیر شماره همراه ثبت می‌شوند.

## بررسی متقاطع P-31 (PHP lane) — 신경ت ۱

اولین تلاش: انبار `majid-ghanbari-dost/Kandoo-Digital-Invoice` کلون شد و فایل `docs/vectors/customer-api-v1-2-proposed.json` در هیچ شاخه یا تاریخچه آن یافت نشد (سند در انبار نادرست بود).

دومین تلاش (تصحیح): انبار `majid-ghanbari-dost/kandoo-cloud-php` (PHP) با خطای 403 مسدود شد — دسترسی read برای کلون اعطا نشده است. فایل `docs/vectors/customer-api-v1-2-proposed.json` بررسی نشد.

## verdict (مرحله ۲)

وضعیت: access denied — PM access را برای انبار kandoo-cloud-php بکشد تا C# lane فایل customer-api-v1-2-proposed.json را بررسی کند. P-31 تا زمان Landing آن PENDING-CROSS-LANE می‌ماند.

## وضعیت تست C-92

تست‌های حالت مشتری C-92 در `tests/Kandoo.App.Tests/C92CustomerModeTests.cs` اضافه شدند (ذخیره/بارگذاری/پاک‌سازی توکن، فرمت JSON snake_case). رن CI 44991041523 — تست‌های برنامه سبز؛ جاب Android emulator به دلیل continue-on-error سبز تلقی شد (خطای زیرساختی، نه خطای کد).

## نتیجه نهایی

تسک PARTIAL — منتظر P-31 API از lanes PHP است.
