# بازبینی ماشهٔ E-72 (تگ `o17-E-72`) — تطبیق وکتور Canonical Invoice v1.2 با مصرف‌کنندهٔ C#

**نسخهٔ ۲ (اصلاح‌شده)** — این گزارش جانشین نسخهٔ اول است. سه خطای واقعی نسخهٔ اول در بخش «اصلاحیه» فهرست شده است.

| مورد | مقدار |
|---|---|
| وکتور | `tests/golden/e72/canonical-invoice-v1-2-draft-vector.json` |
| ریپوی وکتور | `majid-ghanbari-dost/Kandoo-Digital-Invoice` |
| کامیت | `1e029bdadc3f970d45556352d23b5dc7be40587d` (تگ `o17-E-72`) |
| شاخه | `arena/d0587e71-kandoo-digital-invoice` |
| حجم فایل | ۱۵۱۸ بایت |
| MD5 | `1dc1a55dd9814e9434c1abeadd349163` |
| قرارداد منجمد | `src/Kandoo.Core/CanonicalInvoiceConsumer.cs` (`RootAllowed` دست‌نخورده) |
| **رأی** | **ناسازگار (INCOMPATIBLE)** |

---

## ۱. رأی صریح

**وکتور `o17-E-72` با مصرف‌کنندهٔ C# سازگار نیست.**

C# در نخستین گام اعتبارسنجی سطح ریشه، وکتور را رد می‌کند:

```
SchemaValidationFailed | Unexpected field: base_invoice.
```

این نخستین و تنها خطایی است که C# تولید می‌کند، زیرا اعتبارسنج در `CanonicalInvoiceConsumer.cs` **fail-fast** است (نخستین خطا با `throw` بالا می‌رود و اعتبارسنجی متوقف می‌شود). C# هرگز فهرستی از خطاها برنمی‌گرداند و فقط **یک** کد خطا (`ErrorCode`) می‌دهد.

---

## ۲. مغایرت‌ها (سه دسته)

### مغایرت ۱ — پوشش `base_invoice` در سطح ریشه

کلیدهای سطح ریشهٔ وکتور:

```
base_invoice, synthetic_only, v1_2_draft_addendum, vector_id
```

پیکرهٔ فاکتور به‌طور کامل داخل کلید `base_invoice` قرار گرفته است، در حالی که C# می‌خواهد فیلدهای فاکتور مستقیماً در سطح ریشه باشند.

**شاهد کد:** `CanonicalInvoiceConsumer.cs`

| خط | کد | نقش |
|---|---|---|
| ۱۰۹ | `private static void ValidateRoot(JsonElement root)` | ورودی، ریشهٔ سند است |
| ۱۱۱ | `RejectUnknown(root, RootAllowed);` | نخستین گام؛ پیش از هر `Require` |
| ۵۳–۵۸ | `RootRequired` (۱۶ فیلد) | فیلدهای ضرور سطح ریشه |

چون `RejectUnknown` در خط ۱۱۱ **پیش از** حلقهٔ `Require` (خط ۱۱۲) اجرا می‌شود، نخستین خطا `Unexpected field: base_invoice.` است.

### مغایرت ۲ — غیبت هر ۱۶ فیلد ضرور در سطح ریشه

`RootRequired` در خطوط ۵۳–۵۸ شامل **۱۶** فیلد است. **هیچ‌یک** از این ۱۶ فیلد در سطح ریشهٔ وکتور وجود ندارد؛ همه داخل `base_invoice` هستند.

| # | فیلد | در سطح ریشهٔ `o17-E-72` |
|---|---|---|
| ۱ | `schema_version` | غایب |
| ۲ | `mode` | غایب |
| ۳ | `invoice_id` | غایب |
| ۴ | `display_number` | غایب |
| ۵ | `operation_id` | غایب |
| ۶ | `device_id` | غایب |
| ۷ | `public_token` | غایب |
| ۸ | `store_id` | غایب |
| ۹ | `customer` | غایب |
| ۱۰ | `state` | غایب |
| ۱۱ | `state_history` | غایب |
| ۱۲ | `line_items` | غایب |
| ۱۳ | `summary` | غایب |
| ۱۴ | `attention_cells` | غایب |
| ۱۵ | `header` | غایب |
| ۱۶ | `provenance` | غایب |

### مغایرت ۳ — چهار فیلد غیرمجاز در سطح ریشه

`RootAllowed` (خط ۶۰) برابر است با `RootRequired` به‌اضافهٔ `issued_content_sha256`. وکتور چهار فیلد دارد که در این فهرست نیستند:

| # | فیلد | خطای C# |
|---|---|---|
| ۱ | `base_invoice` | `Unexpected field: base_invoice.` ← نخستین خطا |
| ۲ | `synthetic_only` | `Unexpected field: synthetic_only.` |
| ۳ | `v1_2_draft_addendum` | `Unexpected field: v1_2_draft_addendum.` |
| ۴ | `vector_id` | `Unexpected field: vector_id.` |

**نکتهٔ قراردادی مهم:** `v1_2_draft_addendum` محتوایی دارد که در Canonical Invoice v0 هیچ جایگاهی ندارد: `currency_unit`، `invoice_discount`، `payments`، `walk_in_customer`، `schema_version: "1.2-draft"`. این‌ها افزوده‌های v1.2 هستند و صرفاً با برداشتن پوشش `base_invoice` حل نمی‌شوند؛ نیازمند تصمیم قراردادی جداگانه‌اند.

---

## ۳. پاسخ صریح به ادعای PHP دربارهٔ محل دو فیلد `provenance`

### رأی: **تأیید می‌شود — ادعای PHP درست است و گزارش پیشین C# در این بند خطا داشت.**

گزارش پیشین C# ادعا کرده بود `image_sha256` و `template_library_id` **دو بار** ظاهر شده‌اند: یک بار داخل `provenance` و یک بار در سطح `base_invoice`. **این ادعا نادرست بود.** شواهد بایتی:

| سنجه | نتیجه |
|---|---|
| تعداد رخداد واژهٔ `image_sha256` در کل فایل ۱۵۱۸ بایتی | **۱** |
| آفست بایتی آن | **۹۵۵** |
| تعداد رخداد واژهٔ `template_library_id` در کل فایل | **۱** |
| آفست بایتی آن | **۹۷۵** |

هر دو رخداد **تنها** درون شیء `provenance` قرار دارند. سطح `base_invoice` هیچ‌یک از این دو فیلد را ندارد. کلیدهای سطح `base_invoice` به‌طور دقیق:

```
attention_cells, customer, device_id, display_number, header, invoice_id,
line_items, mode, operation_id, provenance, public_token, schema_version,
state, state_history, store_id, summary
```

پس:
- **تکرار (duplication) وجود ندارد.** ادعای پیشین C# باطل است.
- **محل این دو فیلد درست است:** داخل `provenance`، که همان جایی است که C# می‌خواهد (خط ۳۱۵: `RejectUnknown(provenance, "image_sha256", "template_library_id", "engine_version", "fields")`).

### اما یک تمایز حیاتی که رأی را تغییر نمی‌دهد

محل *درست* این دو فیلد **درون** `provenance` بودن است — و این برقرار است. مشکل در **محل خودِ `provenance`** است: `provenance` باید در سطح ریشهٔ فاکتور باشد (`RootRequired` خط ۵۸؛ مصرف در خط ۱۴۰: `ValidateProvenance(Require(root, "provenance"))`)، ولی در وکتور داخل `base_invoice` نشسته است.

بنابراین:
- **ادعای PHP دربارهٔ نبود تکرار: تأیید می‌شود.**
- **نتیجه‌گیری PHP مبنی بر خطای C# در تعیین محل فیلدها: رد می‌شود.** C# دقیقاً همان جایی را می‌خواهد که قرارداد منجمد می‌گوید (درون `provenance`، و `provenance` در ریشه). وکتور `o17-E-72` `provenance` را در جای غلط گذاشته است.

به بیان کوتاه: PHP ایراد را به گزارش C# درست گرفته، اما این ایراد رأی «ناسازگار» را باطل نمی‌کند.

---

## ۴. آنچه در `o17-E-72` نسبت به `1e620cc` اصلاح شد

| مورد | نسخهٔ پیشین (`1e620cc`) | `o17-E-72` (`1e029bd`) | وضعیت |
|---|---|---|---|
| `quantity` → `quantity_millis` در `line_items` | `quantity` | `quantity_millis` | **اصلاح شد** |
| `quantity` → `quantity_millis` در `provenance.fields.line_items` | `quantity` | `quantity_millis` | **اصلاح شد** |
| `display_number` برای حالت DRAFT | `"SYN-E72-001"` | `null` | **اصلاح شد** |
| `provenance.fields.display_number` | `"native"` | `"unavailable"` | **اصلاح شد** |

این چهار مورد که در بازبینی پیشین گزارش شده بود، همگی درست اصلاح شده‌اند.

---

## ۵. آزمون تعیین‌کننده: اگر پوشش برداشته شود

برای سنجش اینکه آیا پیکرهٔ فاکتور **درون** `base_invoice` از نظر معنایی سالم است، اعتبارسنج C# به‌صورت گام‌به‌گام بازتولید شد و سپس `base_invoice` به سطح ریشه ترفیع داده شد.

**نتیجه:** پیکرهٔ ترفیع‌یافته **پذیرفته می‌شود**.

| گام اعتبارسنجی | نتیجه |
|---|---|
| `RejectUnknown(root, RootAllowed)` | گذشت |
| همهٔ ۱۶ فیلد `RootRequired` | موجود |
| `schema_version == "0"` | گذشت |
| `mode in ("A","B")` | گذشت (`B`) |
| `public_token` — base64url ≥ ۲۲ نویسه | گذشت (۲۹ نویسه) |
| `state` | گذشت (`DRAFT`) |
| `ValidateProvenance` | گذشت |
| `ValidateLifecycle` (پیش از ISSUE) | گذشت (`display_number: null` + `"unavailable"`) |
| `ValidateCustomer` | گذشت (`claimed`) |
| `ValidateStateHistory` | گذشت (آرایهٔ خالی) |
| `ValidateLineItems` | گذشت (`quantity_millis: 1000` عدد صحیح) |
| `ValidateSummary` | گذشت |
| `ValidateAttentionCells` | گذشت (آرایهٔ خالی) |
| `ValidateHeader` | گذشت |

**این یعنی مسیر اصلاح روشن است:** برداشتن پوشش `base_invoice` و انتقال فیلدهای فاکتور به سطح ریشه، وکتور را برای C# قابل‌پذیرش می‌کند. تنها بخشی که با این کار حل نمی‌شود، محتوای `v1_2_draft_addendum` است که در v0 جایگاهی ندارد و نیازمند تصمیم قراردادی است.

---

## ۶. اصلاحیه بر گزارش پیشین C#

نسخهٔ اول گزارش C# (کامیت `26d8806`، تگ `o17-E-72-REVIEW`) سه خطای واقعی داشت:

| # | ادعای نادرست پیشین | واقعیت |
|---|---|---|
| ۱ | «۱۰ فیلد ضرور ریشه غایب است» | **۱۶** فیلد غایب است (تمام `RootRequired`) |
| ۲ | «۱۴ خطای `SchemaValidationFailed`» | C# **fail-fast** است و فقط **۱** خطا برمی‌گرداند: `Unexpected field: base_invoice.` |
| ۳ | «`image_sha256`/`template_library_id` دو بار تکرار شده‌اند» | هر واژه **یک بار** در کل فایل ظاهر می‌شود، هر دو درون `provenance` — **تکرار وجود ندارد** |

خطای سوم همان موردی است که PHP به‌درستی شناسایی کرد. رأی نهایی (ناسازگار) با این اصلاح تغییر نمی‌کند، اما دلایل آن محدود به سه دستهٔ بخش ۲ است، نه چهار دسته، و شمار خطاها یک است، نه ۱۴.

---

## ۷. نتیجهٔ نهایی

**وکتور `o17-E-72` سازگار نیست (INCOMPATIBLE).**

نخستین خطای C#: `SchemaValidationFailed | Unexpected field: base_invoice.`

دلایل (سه دسته):
1. پوشش `base_invoice` در ریشه به‌جای فیلدهای تخت.
2. غیبت هر ۱۶ فیلد ضرور `RootRequired` در سطح ریشه.
3. چهار فیلد غیرمجاز در سطح ریشه؛ که یکی از آن‌ها (`v1_2_draft_addendum`) افزودهٔ قراردادی v1.2 است و در v0 جایگاه ندارد.

**قرارداد تغییر نمی‌کند.** `src/Kandoo.Core/CanonicalInvoiceConsumer.cs` دست‌نخورده می‌ماند. اقدام لازم در لِین پایتون است: تخت‌سازی سطح ریشه و تصمیم صریح دربارهٔ `v1_2_draft_addendum`.

**دسترسی به ریپوی PHP:** ریپوی `kandoo-cloud-php` برای اعتبارنامه‌های این لِین با خطای ۴۰۳ (Resource not accessible by integration) مسدود است؛ لذا گزارش `docs/reports/E72-PHP-INDEPENDENT-VERDICT-FA.md` در کامیت `3e44d51` به‌طور مستقیم خوانده نشد. پاسخ بالا بر پایهٔ ادعای منتقل‌شده در درخواست بازبینی و راستی‌آزمایی بایتی خودِ وکتور نوشته شده است. برای بازبینی کامل گزارش PHP، اعطای دسترسی خواندن لازم است.
