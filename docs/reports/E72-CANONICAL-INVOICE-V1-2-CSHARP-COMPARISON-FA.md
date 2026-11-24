# گزارش بازبینی ماشهٔ E-72 — تطبیق وکتور Canonical Invoice v1.2 با C#

**ماشهٔ وکتور:** `tests/golden/e72/canonical-invoice-v1-2-draft-vector.json` در ریپوی `majid-ghanbari-dost/Kandoo-Digital-Invoice` @ `1e620cc`
**پیگیر C#:** `src/Kandoo.Core/CanonicalInvoiceConsumer.cs` (مصرف‌کننده strict Canonical Invoice v0)
**نتیجه:** **مغایرت موجود — نیست بایت‌به‌بایت سازگار** (C# وکتور E-72 را رد می‌کند)

## وکتور E-72 (فیلدهای base_invoice)

فیلدهای اصلی وکتور در سطح ریشه `base_invoice`:
- `schema_version`: `"0"` ✓ سازگار با C# v0
- `mode`: `"B"` ✓ مجاز (A یا B)
- `state`: `"DRAFT"` ✓ حالت پیش از إصدار
- `display_number`: `"SYN-E72-001"` ✗ برای DRAFT باید `null` باشد
- `image_sha256`: `null` ✗ فیلد غیرمجاز در سطح ریشه C#
- `template_library_id`: `"synthetic-e72-template"` ✗ فیلد غیرمجاز در سطح ریشه C#
- `line_items[0].quantity`: `1` ✗ C# به `quantity_millis` انتظار دارد

## فهرست دقیق مغایرات (سه مورد)

### مغایرهٔ ۱ — فیلدهای اضافی در سطح ریشه

وکتور E-72 دو فیلد `image_sha256` و `template_library_id` در سطح ریشه `base_invoice` دارد، اما C# `CanonicalInvoiceConsumer` با `RejectUnknown(root, RootAllowed)` هر فیلد غیرمجاز در سطح ریشه را رد می‌کند. `RootAllowed` شامل ۱۶ فیلد مورد نیاز + `issued_content_sha256` است؛ `image_sha256` و `template_library_id` در آن نیستند.

**خطای C#:** `SchemaValidationFailed` — `Unexpected field: image_sha256.` و `Unexpected field: template_library_id.`

### مغایرهٔ ۲ — نام فیلد تعداد در ردیف کالا

وکتور E-72 از نام فیلد `quantity` استفاده می‌کند (در هر دو سطح `line_items` ریشه و `provenance.fields.line_items`). C# نام دقیق `quantity_millis` را انتظار دارد و `quantity` را فیلد ناشناخته می‌پذیرد.

**خطای C#:** `SchemaValidationFailed` — `Required field is missing: quantity_millis.` و `Unexpected field: quantity.`

### مغایرهٔ ۳ — مقدار display_number برای حالت DRAFT

وکتور E-72 برای حالت `DRAFT` مقدار `display_number: "SYN-E72-001` (رشته غیرخالی) و `provenance.fields.display_number: "native` را دارد. اما C# برای حالت‌های پیش از إصدار (DRAFT/EXTRACTED/VALIDATED) درخواست می‌کند که:
- `display_number` مقدار `null` باشد، و
- `provenance.fields.display_number` مقدار `"unavailable"` باشد.

**خطای C#:** `SchemaValidationFailed` — `display_number is unavailable before ISSUE.`

## نتیجه نهایی

**وکتور E-72 بایت‌به‌بایت با پیاده‌سازی C# سازگار نیست.** C# CanonicalInvoiceConsumer وکتور را با سه خطای `SchemaValidationFailed` بالا رد می‌کند.

برای سازگاری، وکتور باید با موارد زیر تصحیح شود:
1. حذف `image_sha256` و `template_library_id` از سطح ریشه (یا افزودن به `RootAllowed` در C# — که قرارداد فروزن است و نمی‌تواند تغییر کند)
2. تغییر نام `quantity` به `quantity_millis` در `line_items` و `provenance.fields.line_items`
3. تنظیم `display_number` به `null` و `provenance.fields.display_number` به `"unavailable"` برای حالت DRAFT

**لینک وکتور:** https://github.com/majid-ghanbari-dost/Kandoo-Digital-Invoice/blob/1e620cc1f990bec51566b5e1af26dcec97f26fdb/tests/golden/e72/canonical-invoice-v1-2-draft-vector.json

**کامیت پایتون:** `1e620cc1f990bec51566b5e1af26dcec97f26fdb` (شاخه `arena/d0587e71-kandoo-digital-invoice`)
