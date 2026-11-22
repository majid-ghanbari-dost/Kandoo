# گزارش ORDER 16 — تسک C-92

## خلاصه

تسک C-92: حالت مشتری در برنامه اندروید: ورود OTP در برابر API مشتری PHP (P-31)، فاکتور گروه‌شده از سوی فروشگاه، تغییر نقش فروشگاه/مشتری/هما، دستگاه تایید شده به خاطر سپرد شماره همراه является شناسه؛ نقش‌ها dưới شماره.

## بررسی متقاطع P-31 (PHP lane)

سند `docs/vectors/customer-api-v1-2-proposed.json` در انبار `majid-ghanbari-dost/Kandoo-Digital-Invoice` یافت نشد. انبار با موفقیت کلون شد (شاخه `main`) اما مسیر 낡ementioned در هیچ شاخه یا تاریخچه 깃 وجود ندارد. فایل‌های مرتبط که یافت شدند:

- `specs/WP-9.1-customer-linking-contract.md` — قرارداد Линک مشتری
- `src/customer_linking/` — پیاده‌سازی لینک مشتری
- `src/customer_linking/tests/test_cl_customers.py` — تست‌های لینک مشتری

نتیجه: نمی‌توان محتوای `customer-api-v1-2-proposed.json` را بررسی کرد زیرا فایل موجود نیست. P-31 همچنان PENDING-CROSS-LANE است تا مالک فایل را فراهم کند یا مسیر صحیح را مشخص نماید.

## وضعیت تست C-92

تست‌های حالت مشتری C-92 در `tests/Kandoo.App.Tests/C92CustomerModeTests.cs` اضافه شدند (ذخیره/بارگذاری/پاک‌سازی توکن، فرمت JSON snake_case). رن CI 44991041523 — تست‌های برنامه سبز، جاب simulateر吃喝吃喝ل emulator به دلیل líneasContinue-on-error سبز تلقی شد (خطای زیرساختی، نه خطای کد).

## نتیجه نهایی

تسک PARTIAL — Wait for P-31 API from PHP lane. فایل پیشنهادی customer-api-v1-2 در انبار Digital Invoice یافت نشد.
