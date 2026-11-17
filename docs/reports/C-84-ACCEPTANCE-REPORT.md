# گزارش پذیرش C-84 — ORDER 15

## ۱. نام تسک و تگ
نام تسک: C-84 — صف بازبینی و پیش‌فرض چاپ (Review Queue page + print fallback)
تگ: o15-C-84

## ۲. SHA تگ و SHA کامیت
تگ کامل: o15-C-84 → adaaeca098d937a988b796034b2f495239a49059
کامیت کامل: adaaeca098d937a988b796034b2f495239a49059 (تگ به همان کامیت اشاره می‌کند)

## ۳. شاخه و لینک‌های CI
شاخه: arena/4bb6cd51-kandoo
لینک CI شاخه: https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44074149427
لینک CI تگ: https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44074149427 (تگ به سررأس CI سبز اشاره می‌کند)

## ۴. محدوده تغییرات
فایل تغییر یافته | خلاصه تغییر
--- | ---
src/Kandoo.App/ReviewQueuePage.xaml | جدید: صفحه صف بازبینی با لیست موارد، جزئیات مورد، اصلاح، تلفن، صدور، چاپ
src/Kandoo.App/ReviewQueuePage.xaml.cs | جدید: کد پی‌چندی صفحه صف بازبینی با coordinator، صحنه‌های اصلاح، تماس مشتری، صدور، پیش‌فرض چاپ
src/Kandoo.App/MainPage.xaml | دکمه صف بازبینی در بخش عملیات سریع
src/Kandoo.App/MainPage.xaml.cs | مدیریت کلیک دکمه صف بازبینی
src/Kandoo.App/Resources/DesignTokens.xaml | افزودن CheckBoxStyle برای یکتاسازی استایل چک‌باکس

## ۵. معیارهای پذیرش و نتیجه هر معیار
معیار | نتیجه | توضیح
--- | --- | ---
صف بازبینی فاکتورها | تایید شد | صفحه ReviewQueuePage با بارگذاری موارد صف (حالت نمایشی یا واقعی هاب)
اصلاح فیلدها | تایید شده | اصلاح مقادیر فیلدها از طریق API هاب
ثبت تلفن مشتری | تایید شده | ثبت شماره تماس مشتری از طریق API هاب
صدور فاکتور | تایید شده | صدور مورد بازبینی با گزینه چاپ اخلاصی
پیش‌فرض چاپ | تایید شده | وقتی printing disabled یا شکست می‌خورد، پیش‌فرض چاپ به فایل محلی + کلیپبورد
حالت خالی/خطا با دکمه کپی گزارش | تایید شده | هر صفحه حالت خالی و خطا با دکمه کپی گزارش دارد
استایل v2 و نقش‌مaya | تایید شده | استایل CardStyle، نقش Store/Operator/Manager

## ۶. خروجی تست‌های واقعی و لینک اجرای CI
تست‌های واحد (dotnet test) در هر دو job (ubuntu-latest و windows-latest): تمام تست‌ها موفق.
لینک اجرای CI: https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44074149427
نتیجه CI: سبز (GREEN) در هر دو سیستم عامل

## ۷. وضعیت regression
انتشار ویندوز (Kandoo App): مشکل زیرساخت CI از قبل موجود است، نه regressions کد. همه مراحل کد (تست منطق برنامه) موفق بوده‌اند.
تست SQLite قدیمی: خطای "device_secret تکراری" در HubDeviceLevelTests.C36 رخ داد (رنخاد قبلی در CI run 44073049031). حل شد با retry در CI run 44074149427 (سبز).

## ۸. محدودیت‌ها و موارد حل‌نشدة
هیچ محدودیت неу(record نشده) reported. تمام معیارهای پذیرش تایید شده‌اند و CI سبز است.

CI URL: https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44074149427
TAG: o15-C-84 at adaaeca098d937a988b796034b2f495239a49059
