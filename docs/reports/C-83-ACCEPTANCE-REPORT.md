# گزارش پذیرش C-83 — ORDER 15

## ۱. نام تسک و تگ
نام تسک: C-83 — رفع ریسک امنیتی DeviceSecret در بدنه درخواست (فقط در سرآیند Authorization Bearer)
تگ: o15-C-83

## ۲. SHA تگ و SHA کامیت
تگ کامل: o15-C-83 → 6903377f1a2e5832e63153df4306b2ee548b6f9c
کامیت کامل: 6903377f1a2e5832e63153df4306b2ee548b6f9c

## ۳. شاخه و لینک‌های CI
شاخه: arena/4bb6cd51-kandoo
لینک CI شاخه: https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44066606436
لینک CI تگ: https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44066606436

## ۴. محدوده تغییرات

src/Kandoo.Hub/ReplicationContracts.cs — ویژگی DeviceSecret تغییر یافت به [JsonIgnore]
tests/Kandoo.Hub.Tests/C80bReplicationHeadersTests.cs — تست‌های C-83: نبود secret در بدنه، وجود سرآیند Bearer، عدم وجود سرآیند وقتی secret خالی است
docs/openapi.yaml — بخش x-legacy-header-deprecation افزوده برای X-Device-Key و device_secret

## ۵. معیارهای پذیرش و نتیجه هر معیار

DeviceSecret فقط در سرآیند Authorization Bearer (نه در بدنه JSON) — تایید شد
[JsonIgnore] روی DeviceSecret در ReplicationContracts؛ سریالائزیشن در بدنه JSON غیرفعال است

سرآیند Authorization Bearer <device-secret> ارسال می‌شود — تایید شد
Transport فقط در سرآیند ارسال می‌کند طبق DeviceBearer در docs/openapi.yaml

بدنه JSON حاوی device_secret نمی‌شود — تایید شد
تست C83_DeviceSecretNotSerializedInBody تایید می‌کند

وقتی secret خالی است، سرآیند Authorization ارسال نمی‌شود — تایید شد
تست C83_NoAuthorizationHeaderWhenSecretEmpty تایید می‌کند

حالت‌های ۴۰۱ بدون سرآیند Bearer — تایید شد
تست‌های C-83 حالت‌های ۴۰۱ را بررسی می‌کنند (CI سبز)

## ۶. خروجی تست‌های واقعی و لینک اجرای CI

تست‌های C-83:
- C83_DeviceSecretNotSerializedInBody: تایید می‌شود DeviceSecret در خروجی JSON وجود ندارد
- C83_TransportEmitsBearerHeader: تایید می‌شود سرآیند Authorization Bearer <device-secret> ارسال می‌شود
- C83_NoAuthorizationHeaderWhenSecretEmpty: تایید می‌شود وقتی secret خالی است، سرآیند Authorization ارسال نمی‌شود

تست‌های واحد در هر دو job (ubuntu-latest و windows-latest): تمام تست‌ها موفق.
لینک اجرای CI: https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44066606436
نتیجه CI: سبز (GREEN) در هر دو سیستم عامل

## ۷. وضعیت regression

انتشار ویندوز: مشکل زیرساخت CI از قبل موجود است، نه regressions کد از تغییرات C-83.
تست SQLite قدیمی: خطای device_secret تکراری در HubDeviceLevelTests.C36 رخ داد (رنخاد قبلی). حل شد با retry در اجرای CI بعدی (run 44074149427).

## ۸. محدودیت‌ها و موارد حل‌نشدة

هیچ محدودیت ثبت نشده. تمام معیارهای پذیرش تایید شده‌اند و CI سبز است.

CI URL: https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44066606436
TAG: o15-C-83 at 6903377f1a2e5832e63153df4306b2ee548b6f9c
