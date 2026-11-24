# گزارش بازبینی ماشهٔ E-72 (تگ o17-E-72) — تطبیق وکتور Canonical Invoice v1.2 با C#

**ماشهٔ وکتور:** `tests/golden/e72/canonical-invoice-v1-2-draft-vector.json` در ریپوی `majid-ghanbari-dost/Kandoo-Digital-Invoice` @ `1e029bd` (تگ `o17-E-72`)
**پیگیر C#:** `src/Kandoo.Core/CanonicalInvoiceConsumer.cs` (مصرف‌کننده strict Canonical Invoice v0 — قرارداد منجمد)
**نتیجه:** **NASĀZGAR (INCOMPATIBLE)** — C# وکتور o17-E-72 را رد می‌کند

## وکتور o17-E-72 (ساختار ریشه)

کلیدهای سطح ریشه وکتور:
`base_invoice`, `public_token`, `schema_version`, `state`, `state_history`, `store_id`, `summary`, `synthetic_only`, `v1_2_draft_addendum`, `vector_id`

فaturas اصلی داخل `base_invoice` است، نه مستقیماً در سطح ریشه.

## فهرست دقیق مغایرات (چهار دسته)

### مغایرهٔ ۱ — بسته‌بندی شWrapper `base_invoice` (ساختمانی)

وکتور o17-E-72 کل فاکتور را dưới کلید `base_invoice` در سطح ریشه قرار می‌دهد. اما C# `CanonicalInvoiceConsumer.ConsumeForReview` (خط ۹۳) ریشه JSON را مستقیماً به `ValidateRoot` می‌فرستد و می‌طلبد که `schema_version`, `mode`, `invoice_id`, `display_number` و سایر فیلدهای ضرور **مستقیماً در سطح ریشه** باشند (خطوط ۵۳–۵۸ و ۱۱۱–۱۱۲).

### مغایرهٔ ۲ — فیلدهای ضرور الغا‌شده در سطح ریشه (۱۰ مورد)

C# می‌طلبد این ۱۰ فیلد مستقیماً در سطح ریشه باشد، اما وکتور o17-E-72 آن‌ها را تنها داخل `base_invoice` دارد:

| # | فیلدガぶ Hilfe | وضعیت در o17-E-72 ریشه | خطای C# |
|---|---|---|---|
| 1 | `mode` | غایب (داخل `base_invoice`) | `Required field is missing: mode.` |
| 2 | `invoice_id` | غایب (داخل `base_invoice`) | `Required field is missing: invoice_id.` |
| 3 | `display_number` | غایب (داخل `base_invoice`) | `Required field is missing: display_number.` |
| 4 | `operation_id` | غایب (داخل `base_invoice`) | `Required field is missing: operation_id.` |
| 5 | `device_id` | غایب (داخل `base_invoice`) | `Required field is missing: device_id.` |
| 6 | `customer` | غایب (داخل `base_invoice`) | `Required field is missing: customer.` |
| 7 | `line_items` | غایب (داخل `base_invoice`) | `Required field is missing: line_items.` |
| 8 | `attention_cells` | غایب (داخل `base_invoice`) | `Required field is missing: attention_cells.` |
| 9 | `header` | غایب (داخل `base_invoice`) | `Required field is missing: header.` |
| 10 | `provenance` | غایب (داخل `base_invoice`) | `Required field is missing: provenance.` |

### مغایرهٔ ۳ — فیلدهای اضافی غیرمجاز در سطح ریشه (۴ مورد)

C# با `RejectUnknown(root, RootAllowed)` (خط ۱۱۱) هر فیلد غیرمجاز در سطح ریشه را رد می‌کند. `RootAllowed` شامل ۱۶ فیلد ضرور + `issued_content_sha256` است. وکتور o17-E-72 ۴ فیلد اضافی دارد:

| # | فیلد اضافی | خطای C# |
|---|---|---|
| 1 | `base_invoice` | `Unexpected field: base_invoice.` |
| 2 | `synthetic_only` | `Unexpected field: synthetic_only.` |
| 3 | `v1_2_draft_addendum` | `Unexpected field: v1_2_draft_addendum.` |
| 4 | `vector_id` | `Unexpected field: vector_id.` |

### مغایرهٔ ۴ — تکرار دو فیلد provenance (مواردی که PHP اشاره کرده)

وکتور o17-E-72 دو فیلد `image_sha256` و `template_library_id` را **دو‌بار** دارد:
- یک بار داخل `provenance` (در `base_invoice.provenance`)
- یک بار مستقیماً در سطح `base_invoice` (هم‌سطح با `provenance`)

C# این دو فیلد را **تنها داخل `provenance`** می‌طلبد (خطوط ۳۱۵–۳۲۷):
```csharp
RejectUnknown(provenance, "image_sha256", "template_library_id", "engine_version", "fields");
foreach (var name in new[] { "image_sha256", "template_library_id", "engine_version", "fields" })
{
    Require(provenance, name);
}
```

اما چون C# در اعتبارسنجی سطح ریشه شکست می‌خورد (مغایرات ۱–۳)، هرگز به اعتبارسنجی `provenance` نمی‌رسید.

## پاسخ به ادعای PHP دربارهٔ «محل دو فیلد provenance»

گزارش آزاد PHP (ریپو `kandoo-cloud-php`، شاخهٔ `o16-P-E72-review`، کامیت `3e44d51`؛ reports/E72-PHP-INDEPENDENT-VERDICT-FA.md) ادعا می‌کند گزارش C# در مورد «محل دو فیلد provenance» خطا دارد.

**پاسخ C# — رد ed و مستند:**

從 C# کد Frozen (`src/Kandoo.Core/CanonicalInvoiceConsumer.cs`):

1. **محل صحیح `image_sha256` و `template_library_id`:** داخل `provenance` (خط ۳۱۵) نه در سطح ریشه و نه در سطح `base_invoice`.
2. **محل صحیح `provenance`:** مستقیماً در سطح ریشه invoice (خط ۵۸، `RootRequired`) نه در অন্তরী `base_invoice`.

وکتور o17-E-72:
- `provenance` را داخل `base_invoice` دارد → **موقع炼错了** (باید در سطح ریشه باشد)
- `image_sha256` و `template_library_id` را هر دو داخل `provenance` و هم‌زمان در سطح `base_invoice` دارد → **تکرار غیرمجاز**

در هر دو مورد، C# مطابق کد frozen عمل می‌کند؛ ادعای PHP درباره خطای C# در تعیین محل فیلدها **رد می‌شود** با شواهد بایتی به Lines ۵۳–۵۸، ۱۱۱–۱۱۲، ۳۱۵–۳۲۷ در `CanonicalInvoiceConsumer.cs`.

## مواردی که در o17-E-72 اصلاح شده‌اند (از نسخه 1e620cc)

| مورد | نسخه قبلی (1e620cc) | نسخه o17-E-72 (1e029bd) | وضعیت C# |
|---|---|---|---|
| `quantity` در `line_items` | `quantity` (اشتباه) | `quantity_millis` ✓ | سازگار |
| `quantity_millis` در `provenance.fields.line_items` | `quantity` (اشتباه) | `quantity_millis` ✓ | سازگار |
| `display_number` برای DRAFT | `"SYN-E72-001"` (اشتباه) | `null` ✓ | سازگار |
| `provenance.fields.display_number` | `"native"` (اشتباه) | `"unavailable"` ✓ | سازگار |

با این حال، چهار دسته مغایر بالا همچنان برقرار است و وکتور o17-E-72 را **NASĀZGAR** می‌سازد.

## نتیجه نهایی

**وکتور o17-E-72 بایت‌به‌بایت با پیاده‌سازی C# سازگار نیست.** C# CanonicalInvoiceConsumer وکتور را با ۱۴ خطای `SchemaValidationFailed` (۱۰ مورد 필드 الغا‌شده + ۴ مورد فیلد اضافی در سطح ریشه) رد می‌کند.

**لینک وکتور:** https://github.com/majid-ghanbari-dost/Kandoo-Digital-Invoice/blob/1e029bdadc3f970d45556352d23b5dc7be40587d/tests/golden/e72/canonical-invoice-v1-2-draft-vector.json

**کامیت پایتون:** `1e029bdadc3f970d45556352d23b5dc7be40587d` (تگ `o17-E-72`، شاخه `arena/d0587e71-kandoo-digital-invoice`)

**عقده:** قرارداد `src/Kandoo.Core/CanonicalInvoiceConsumer.cs` (RootAllowed دست‌نخورده) تغییر نمی‌کند. وکتور باید با四 دسته مغایر بالا اصلاح شود تا از نظر C# قابل پذیرش باشد.
