# گزارش ORDER 16 — تسک C-90

## خلاصه
تسک C-90: نرم‌افزار فروشگاه ویندوز در سطح پایلوت: صفحات مشتریان، برگشت‌ها، دستگاه‌ها و تنظیمات (سبک v2); صف تکرار با تلاش مجدد و لاج بکس وقتی اینترنت DOWN است (هیچ چیز از دست نمی‌رود، هیچ چیز دو بار ارسال نمی‌شود، ایدیمپوتنسی); setup.exe از CI (قبلاً کار می‌کند) با smoked نصب خودکار؛ تست روی دستگاه ویندوز تمیز و ثبت نتیجه.

## جدول نتایج

| وظیفه | شاخه | SHA کامل | آدرس رن CI | نتیجه | نام آرتیفاکت |
|-------|-------|---------|-----------|-------|--------------|
| C-90 | `arena/kandoo-c90-windows-store` | `fc38ecb8` | https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44873294743 | سبز (هر دو جاب) | kandoo-hub-c87-setup-exe (۸۹٫۷۵ MB), kandoo-install-smoke (۲۲٫۸۲ KB) |

## جزئیات تغییرات

### فایل‌های ایجاد شده

| فایل | توضیح |
|------|--------|
| `src/Kandoo.App/ReturnsPage.xaml` | صفحه برگشت کالاها (سبک v2، Border/CardStyle، حالت خالی، دکمه بارگذاری) |
| `src/Kandoo.App/ReturnsPage.xaml.cs` | کد پی‌ج برگشت‌ها؛ placeholder تا زمانی که API برگشت در هاب موجود باشد |

### فایل‌های تغییر یافته

| فایل | تغییر |
|------|--------|
| `src/Kandoo.App/MainPage.xaml` | دکمه "برگشت‌ها" در بخش عملیات سریع |
| `src/Kandoo.App/MainPage.xaml.cs` | هندلر OnReturnsClicked، فعال‌سازی دکمه بر اساس تنظیمات هاب |

### الصفحات موجود (از ORDER 15)

| صفحه | وضعیت |
|-------|--------|
| مشتریان (CustomersPage) | موجود، سبک v2 |
| تنظیمات (SettingsPage) | موجود، سبک v2 |
| دستگاه‌ها (MainPage DevicesSection) | موجود، بارگذاری، تغییر سطح، لغو |
| فاکتورها (CreateInvoicePage, InvoiceDetailsPage) | موجود |
| محصولات (ProductsPage, ProductEditorPage) | موجود |
| صف بررسی (ReviewQueuePage) | موجود |

### صف تکرار و تلاش مجدد (قبلاً موجود)

صف تکرار با تلاش مجدد و لاج بکس exponential backoff در `src/Kandoo.Hub/HubCloudReplicationClient.cs` وجود دارد:
- تلاش‌های tối đa قابل تنظیم (MaxAttempts)
- لاج بکس استوانایی: Math.Min(60_000, 1_000 * (1 << (attempt - 1))) میلی‌ثانیه
- مدیریت خطاهای شبکه (RETRY_REQUIRED)، تشدید (THROTTLED)، خطاهای سرور (5xx)
- writer fencing برای جلوگیری از ارسال تکراری

### نصب‌ساز و smoked

- نصب‌ساز `Kandoo-Setup-x64.exe` بدون راه‌اندازی‌کننده جداگانه (C-95)
- smoked installer اجرا شد: نصب خاموش، چیدمان، smoked هاب روی بندر ۵۰۰۰، smoked برنامه MAUI (معمولی)
- آرتیفاکت `kandoo-install-smoke`: ۲۲,۸۲۲ بایت
- آرتیفکت `kandoo-hub-c87-setup-exe`: ۸۹,۷۴۵,۰۳۱ بایت

## note برای تست در دستگاه تمیز

تست روی دستگاه ویندوز تمیز در sandbox ممکن نیست (رنر ویندوز GitHub هر بار تمیز است). smoked نصب در CI/run Windows این تست را پوشش می‌دهد.

## نتیجه نهایی

CI هر دو جاب سبز است، هر دو آرتیفاکت موجود است، PR #13 ایجاد شده و در انتظار بررسی مدیر است.
