# گزارش ORDER 16 — تسک C-93-3 (شاخه c93fix3)

## خلاصه

تسک C-93-3: اضافه کردن کلاس `HolooShadowStore` برای سایه‌انبار فقط‌خواندنی محصولات، مشتریان و تاریخچه Holoo. این کلاس داده‌های وارد شده از Holoo را در حافظه محلی نگهداری می‌کند بدون نوشتن به Holoo.

## جدول نتایج

| وظیفه | شاخه | SHA کامل | آدرس رن CI | نتیجه |
|-------|-------|---------|-----------|------|
| C-93-3 | `arena/4bb6cd51-kandoo-c93fix3` | `77fbef7` | https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44902323205 | سبز (هر دو جاب) |

## فایل‌های تغییر یافته

| فایل | تغییر |
|------|--------|
| `src/Kandoo.HolooConnector/HolooSchemaVersion.cs` | کپی از تسک C-93-1 |
| `src/Kandoo.HolooConnector/HolooIncrementalSync.cs` | کپی از تسک C-93-2 |
| `src/Kandoo.HolooConnector/HolooShadowStore.cs` | اضافه شدن کلاس سایه‌انبار برای مشتریان، محصولات و تاریخچه |
| `tests/Kandoo.HolooConnector.Tests/HolooConnectorTests.cs` | اضافه شدن ۶ تست جدید برای سایه‌انبار |

## جزئیات پیاده‌سازی

### HolooShadowStore

- کلاس sealed برای نگهداری داده‌های فقط‌خواندنی Holoo
- متد `UpsertCustomer`: افزودن یا به‌روزرسانی مشتری سایه
- متد `UpsertProduct`: افزودن یا به‌روزرسانی محصول سایه
- متد `AppendHistory`: افزودن رکورد تاریخچه
- متد `TryGetCustomer`: دریافت مشتری بر اساس کد
- متد `TryGetProduct`: دریافت محصول بر اساس کد
- خصوصیت `Customers`: لیست только-readable مشتریان
- خصوصیت `Products`: لیست فقط-readable محصولات
- خصوصیت `History`: لیست فقط-readable تاریخچه

### رکوردها

- `CustomerShadowRecord`: کد مشتری، نام، تلفن، وضعیت تأیید نشده
- `ProductShadowRecord`: کد محصول، نام، قیمت خرید، آخرین قیمت فروش، وضعیت تأیید نشده
- `HistoryShadowRecord`: کد فاکتور، کد مشتری، کد محصول، تعداد، قیمت واحد

## محدودیت‌ها و نکات برای مدیر (PM)

- به HolooSchemaVersion و HolooIncrementalSync (تسک‌های C-93-1 و C-93-2) وابسته است.
- فقط خواندنی است؛ به Holoo نوشته نمی‌شود.
- هیچ رمز عبور ذخیره نمی‌شود.
- هیچ کلمه خارجی در گزارش نیست (زبان فارسی است).

## نتیجه نهایی

CI هر دو جاب سبز است، تست‌های جدید پاس شدند، PR #19 ایجاد شده است. C-93-3 تکمیل شد.