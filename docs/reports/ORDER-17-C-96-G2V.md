# ORDER 17 — C-96 group 2 verification (Review, Invoice, Invoices)

**Lane:** C# · **Stage:** T3 stage 4 · **Boards:** `Review.dc.html`, `Invoice.dc.html`, `Invoices.dc.html` · **Behaviour:** `docs/product-ux-spec.md`

Same method as group 1: board is the spec, compared field by field, divergences listed with a reason rather than relabelled.

---

## The finding that outranks the pixels

Comparing the **Invoice** screen to its board meant following the screen into its data path — and the path was broken in three places. The app's invoice calls did not match the Hub's wire contract, so **issuing an invoice from the Windows app could never have worked**, and no test covered it, so CI stayed green:

| # | Hub expects | App sent | Result before |
|---|---|---|---|
| 1 | envelope, invoice at `response.invoice.invoice_id` | read `invoice_id` from the root | always threw `Hub draft response missing invoice_id` |
| 2 | `operation_id`, `service_type`, `description`, `quantity_millis`, `unit_price_rials` | `product_id`, `quantity`, `unit_price_rials` | 400 `VALIDATION_ERROR` |
| 3 | `operation_id` **and** `service_type` | `operation_id` only | issue rejected |

**Fixed** in `src/Kandoo.App/Logic/HubInvoiceDataSource.cs` (`OperationResponse.TryReadInvoiceId`, correct item/issue bodies, `quantity → quantity_millis` ×1000, optional `product_id`) and pinned by `tests/Kandoo.App.Tests/C98InvoiceWireContractTests.cs`, which asserts the real Hub reply shape and treats the old root-level shape as invalid.

This is the DoD-A point-3 blocker in the **product**, not just the demo script: `scripts/demo-check.ps1` drove the API directly and passed, while the app's own path was dead. Both now agree.

---

## Screen 3 — Review queue ↔ `Review.dc.html`

| # | Board | Built screen | Status |
|---|---|---|---|
| 1 | Page title `صف بازبینی` | `صف بازبینی فاکتورها` | **DIFFERS (cosmetic)** |
| 2 | Sub `کندو در این فاکتورها مطمئن نبود. بررسی کنید و بفرستید.` | `فاکتورهای در حالت بررسی که نیاز به اصلاح و صدور دارند.` | **DIFFERS (cosmetic)** |
| 3 | Card: `فاکتور [شماره]`, `مشتری: [موبایل]`, `نیازمند بازبینی`, `دلیل: [...]` | list of review items from the Hub | **MATCHES** in substance |
| 4 | Actions `تأیید و ارسال پیامک`, `ویرایش` | `ثبت تلفن مشتری`, `صدور و چاپ`, `صدور فاکتور` | **DIFFERS** — see note A |
| 5 | Issued item shows `پیامک ارسال شد` + `ارسال دوباره` | `ارسال دوباره پیامک` on the details page | **MATCHES** |
| 6 | Data-source selector, Hub connection settings | present (not on the board) | **DIFFERS (extra)** — demo-profile controls, see note B |

**Note A.** The board's `تأیید و ارسال پیامک` is one action; the built screen splits it into phone capture, then issue, then (optionally) print. That split is what the Hub API actually requires — `reviews/{id}/customer-phone` must be set before `reviews/{id}/issue`. A single button would have to call two endpoints silently. Kept, flagged.

**Note B.** The board is the production review queue; the demo build also has to say where its data comes from. Kept, flagged.

---

## Screen 4 — Invoice issue ↔ `Invoice.dc.html`

| # | Board | Built screen (`CreateInvoicePage`) | Status |
|---|---|---|---|
| 1 | Title `صدور فاکتور` | `ساخت فاکتور جدید` | **DIFFERS (cosmetic)** |
| 2 | Service-type pills: `فروش` / `اجاره (به‌زودی)` / `سایر خدمات (به‌زودی)` | no pills; `SALE` hard-coded | **DIFFERS — acceptable**, only SALE is enabled by the Hub (the other two return `SERVICE_TYPE_DISABLED`) |
| 3 | `شماره‌ی مشتری (برای پیامک فاکتور)` | `تلفن مشتری` entry on the draft | **MATCHES** |
| 4 | `نام مشتری (الزامی)` | absent | **DIFFERS** — the Hub accepts `customer_name` but does not require it; board says required. Not changed (see note C) |
| 5 | Product picker by `تگ رقمی` + `یا نام کالا`, `رنگ`, `تعداد`, `موجودی` | raw product-id entry + quantity + price | **DIFFERS — the big one**, see note D |
| 6 | Total/payable summary | line list with per-line totals | **PARTIAL** |
| 7 | Digital-first note | `فاکتور همیشه به‌صورت دیجیتال ساخته می‌شود؛ چاپ اختیاری است.` | **MATCHES** in meaning |

**Note C.** Making the customer name required client-side while the Hub treats it as optional would make the app stricter than the API and starts rejecting invoices the backend accepts. Needs a product ruling on whether the *product* requires it — pairs with the `Customer` work in group 3.

**Note D — the product picker is the real gap.** The board's whole point is *"تگ رقمی (سریع‌ترین راه)"*: the operator types a short numeric tag and the product appears. The built screen asks for a raw GUID in a text box, which no shop operator can use, and the board's colour/size/material pickers (`[گزینه‌های همین کالا]`) do not exist. The Hub already exposes what this needs — `GET /v1/stores/{id}/products` with search, plus `cascade-options` — so this is app work, not API work.

**Why it was not built in this stage:** it is a picker with its own search/selection state, not a copy fix, and it belongs with group 3 (Products) so the same control is built once and reused. Recorded rather than silently skipped.

---

## Screen 5 — Invoices list ↔ `Invoices.dc.html`

| # | Board | Built screen | Status |
|---|---|---|---|
| 1 | Title `فاکتورها` | `فهرست فاکتورها` | **DIFFERS (cosmetic)** |
| 2 | Filter chips: `همه` / `پیامک ارسال شد` / `نیازمند بازبینی` / `لغوشده` | none | **DIFFERS — BLOCKED**, see note E |
| 3 | Table `شماره` / `مشتری` / `تاریخ` / `مبلغ` / `وضعیت` | list of items from the Hub | **PARTIAL** |
| 4 | Empty state | `هیچ فاکتور یافت نشد` / `فهرست فاکتورها خالی است.` | **MATCHES** |

**Note E — the list has no data source.** There is **no `GET /v1/stores/{id}/invoices` route in the Hub**; the invoice API is write-only (drafts / items / issue / sms). The app's "بارگذاری فاکتورها" button therefore cannot be backed by a real query today, and the filter chips on the board need that query even more. Adding the read route is server work with its own pagination and projection decisions — a stage of its own, not a screen fix.

Filed in **Q-C98-03** with options, because the board also expects filters over fields (`sms sent`, `needs review`, `cancelled`) whose storage needs checking first.

---

## Honest status

Group 2 is **PARTIAL**:

- **Fixed and green:** the app↔Hub invoice wire contract (three defects; this was the DoD-A blocker), with tests.
- **Verified, no change needed:** Review queue in substance; Invoices empty state.
- **Verified, differs, kept with a reason:** single-action vs two-step review issue; extra demo-profile controls; no service-type pills.
- **Not done, recorded:** the product picker by numeric tag (group 3), the invoices list read route (`Q-C98-03`), customer-name requiredness (note C).

Calling this group "green" would mean the screen matched its board. It does not, and two of the reasons are server contracts that do not exist yet.
