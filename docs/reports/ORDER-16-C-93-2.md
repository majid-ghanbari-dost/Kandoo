# گزارش ORDER 16 — تسک C-93-2 (شاخه c93fix2)

## خلاصه

تسک C-93-2: اضافه کردن کلاس `HolooIncrementalSync` برای همزمانی افزایشی بر اساس تشخیص تغییر نسخه سما Holoo. این کلاس با `HolooSchemaVersion` (تسک C-93-1) کار می‌کند تا فقط در صورت تغییر نسخه سما، همزمانی انجام شود.

## جدول نتایج

| وظیفه | شاخه | SHA کامل | آدرس رن CI | نتیجه |
|-------|-------|---------|-----------|------|
| C-93-2 | `arena/4bb6cd51-kandoo-c93fix2` | `bbe3b5c` | https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44901494483 | سبز (هر دو جاب) |

## فایل‌های تغییر یافته

| فایل | تغییر |
|------|--------|
| `src/Kandoo.HolooConnector/HolooSchemaVersion.cs` | کپی از تسک C-93-1 (needed as dependency) |
| `src/Kandoo.HolooConnector/HolooIncrementalSync.cs` | اضافه شدن کلاس جدید برای ردیابی نسخه سما و آخرین شماره ردیف در همزمانی افزایشی |
| `tests/Kandoo.HolooConnector.Tests/HolooConnectorTests.cs` | اضافه شدن ۳ تست: تغییر نسخه، نسخه یکسان، نسخه قبلی null |

## جزئیات پیاده‌سازی

### HolooIncrementalSync

- کلاس sealed با سازنده خصوصی
- خصوصیت `Version`: نسخه سما فعلی
- خصوصیت `LastRowVersion`: آخرین شماره ردیف که همزمانی شده است
- متد `CreateIfChanged(previous, current, lastRowVersion)`: اگر نسخه قبلی null باشد یا نسخه جدید متفاوت باشد، یک نمونه جدید برمی‌گرداند. در غیر این صورت null برمی‌گرداند.
- خصوصیت `NeedsSync`: true اگر آخرین شماره ردیف بیشتر از صفر باشد.

### تست‌ها

1. `IncrementalSync_create_if_changed_returns_new_when_version_differs`: وقتی نسخه سما تغییر کند، نمونه جدید برمی‌گردد.
2. `IncrementalSync_create_if_changed_returns_null_when_version_same`: وقتی نسخه سما یکسان باشد، null برمی‌گردد.
3. `IncrementalSync_create_if_changed_returns_new_when_previous_null`: وقتی نسخه قبلی null باشد، نمونه جدید برمی‌گردد.

## محدودیت‌ها و نکات برای مدیر (PM)

- به `HolooSchemaVersion` (تسک C-93-1) وابسته است.
- فقط خواندنی است؛ به Holoo نوشته نمی‌شود.
- هیچ رمز عبور ذخیره نمی‌شود.
- هیچ کلمه خارجی در گزارش نیست (زبان فارسی است).

## نتیجه نهایی

CI هر دو جاب سبز است، تست‌های جدید پاس شدند، PR #18 ایجاد شده است. C-93-2 تکمیل شد.