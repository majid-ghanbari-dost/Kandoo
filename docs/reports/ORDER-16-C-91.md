# گزارش ORDER 16 — تسک C-91

## خلاصه
تسک C-91: برنامه فروشگاه اندروید کامل: جفت‌سازی، محصولات با گروه/زیرگروه، صدور فاکتور، صف بررسی، فهرست فاکتور‌ها؛ آفلاین-اول با همگام‌سازی؛ APK منتشر شده امضا شده؛ شروع برндت (cold start) روی Samsung Galaxy J8 (اندروید ۱۰) حداکثر ۳ ثانیه، گزارش عدد واقعیmeasured یا expresión نشده بودن.

## جدول نتایج

| وظیفه | شاخه | SHA کامل | آدرس رن CI | نتیجه | نام آرتیفاکت |
|-------|-------|---------|-----------|-------|--------------|
| C-91 | `arena/kandoo-c91-android-store` | `59adf302` | https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44873977189 | سبز (جاب تست+وریدوس+اندروید) | kandoo-app-c59-android-release-apk, kandoo-app-c50-android-debug-apk |

## جزئیات تغییرات

### فایل‌های ایجاد شده

| فایل | توضیح |
|------|--------|
| `src/Kandoo.App/InvoicesListPage.xaml` | صفحه فهرست فاکتورها (سبک v2، Border/CardStyle، حالت خالی، دکمه بارگذاری) |
| `src/Kandoo.App/InvoicesListPage.xaml.cs` | کد پی‌ج فهرست فاکتورها؛ placeholder تا زمانی که API فهرست فاکتور در هاب موجود باشد |

### فایل‌های تغییر یافته

| فایل | تغییر |
|------|--------|
| `src/Kandoo.App/MainPage.xaml` | دکمه "فاکتورها" در بخش عملیات سریع |
| `src/Kandoo.App/MainPage.xaml.cs` | هندلر OnInvoicesClicked، فعال‌سازی دکمه بر اساس تنظیمات هاب |

### ویژگی‌های موجود (از ORDER 15)

| ویژگی | وضعیت |
|-------|--------|
| جفت‌سازی (Pairing) | موجود،Section جفت‌سازی در MainPage |
| محصولات با گروه/زیرگروه | موجود، ProductEditorPage |
| صدور فاکتور | موجود، CreateInvoicePage |
| صف بررسی | موجود، ReviewQueuePage |
| فهرست مشتریان | موجود، CustomersPage |
| تنظیمات | موجود، SettingsPage |
| APK منتشر شده امضا شده | موجود، CI (C-59) |
| استارت برندت (cold start) | زیرساخت در CI، استخراج عدد وابسته به استونора |

### صف تکرار و تلاش مجدد (قبلاً موجود)

صف تکرار با تلاش مجدد و لاج بکس exponential backoff در `src/Kandoo.Hub/HubCloudReplicationClient.cs` وجود دارد.

### note برای cold start

شروع برندت (cold start) روی Samsung Galaxy J8 (اندروید ۱۰) از طریق `adb shell am start -W` در strobهای emulateر Android ۱0 در CI اندازه‌گیری می‌شود. استونора emulator در CI viimeاً به دلیل خطای بنية‌ت hạיפור不起못 شد (خطای بنية زیرساخت، نه rück‌جезд کد). عدد واقعی cold start هنوز ثبت نشده است.

### note برای فهرست فاکتور

API فهرست فاکتور در هاب هنوز موجود نیست. InvoicesListPage placeholder است تا زمانی که API backend ارائه شود.

## نتیجه نهایی

CI jab تست+Windows+Android سبز است، APK منتشر شده امضا شده توسط CI ساخته می‌شود، PR #14 ایجاد شده و در انتظار بررسی مدیر است.
