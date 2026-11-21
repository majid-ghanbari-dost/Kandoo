# گزارش پایانی C-83 —ORDER 15 رفع ریسک امنیتی DeviceSecret در بدنه درخواست

## خلاصه اجرا

تکمیل تسک C-83 با تگ o15-C-83 و CI سبز. Finding ایجانت PHP (P-23) اصلاح شد: DeviceSecret فقط در سرآیند Authorization: Bearer ارسال شود؛ از بدنه JSON حذف گردد.

## جدول تسک‌ها

| تسک | تگ | commit کامل | اجرای CI شاخه | اجرای CI تگ | وضعیت |
|------|-----|-------------|---------------|------------|--------|
| C-83 | o15-C-83 | 6903377f1a2e5832e63153df4306b2ee548b6f9c | 44066606436 — سبز (اوبونتو + ویندوز) | همان commit (تگ به سررأس سبز CI اشاره می‌کند) | تکمیل شد |

CI شاخه: https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44066606436

## یافتن ایجانت PHP (P-23)

DeviceSecret علاوه بر سرآیند Bearer در JSON بدنه درخواست هم سریالایز می‌شد. این نقض Cloud contract است که DeviceBearer را سرآیند Authorization: Bearer می‌طلبد.

## اصلاحات اعمال شده

| فایل | تغییر |
|------|-------|
| `src/Kandoo.Hub/ReplicationContracts.cs` | `[JsonIgnore]` افزوده به ویژگی DeviceSecret؛داخل بدنه JSON سریالائز نمی‌شود |
| `tests/Kandoo.Hub.Tests/C80bReplicationHeadersTests.cs` | تست‌های C-83: نبود secret در بدنه، وجود سرآیند Bearer، عدم وجود سرآیند وقتی secret خالی است |
| `docs/openapi.yaml` | x-legacy-header-deprecation افزوده برای X-Device-Key و device_secret (بدهی P-27) |

## تست‌های C-83

| تست | توضیح |
|------|-------|
| C83_DeviceSecretNotSerializedInBody | تایید می‌شود DeviceSecret در خروجی JSON وجود ندارد |
| C83_TransportEmitsBearerHeader | تایید می‌شود سرآیند Authorization: Bearer <device-secret> ارسال می‌شود |
| C83_NoAuthorizationHeaderWhenSecretEmpty | تایید می‌شود وقتی secret خالی است، سرآیند Authorization ارسال نمی‌شود |

## تصمیمات خودمختار (C-83)

1. DeviceSecret از سریالیزاسیون بدنه JSON حذف شد؛ فقط در سرآیند Authorization: Bearer نگهدارنده است طبق DeviceBearer در docs/openapi.yaml
2. x-legacy-header-deprecation در openapi.yaml ثبت شد (بدهی ثبت‌شده ایجانت PHP از P-27)
3. پذیرش تست: نبود secret در بدنه + بقای احراز هویت با Bearer + حالت‌های ۴۰۱

## تسک‌های بعدی

- C-84: صف بررسی و چاپ جایگزین
- C-85: setup.exe از طریق Inno Setup یا جایگزین zip + RUN-LOCAL.md + start-all
- C-86 (استretch): حالت مشتری اندروید

CI URL: https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44066606436
TAG: o15-C-83 at 6903377f1a2e5832e63153df4306b2ee548b6f9c
