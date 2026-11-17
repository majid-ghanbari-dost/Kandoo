# RUN-LOCAL — Kandoo Order 15 Demo Week (ویندوز + اندروید)

این مستند برای اجرای محلی برنامه کاندو در هفتهٔ نمایشی (۷ روزه از ۲۰۲۶-۱۱-۱۶) نوشته شده است. مالک روی ویندوز و/یا گوشی اندروید می‌تواند از این مراحل برای تست نهایی استفاده کند.

## پیش‌نیازها

.NET 8 SDK نسخه 8.0.404+
Inno Setup نسخه 6.x
Android SDK نسخه API 21+
Git نسخه 2.x

## گام ۱: کلون ریپازیتوری

```bash
git clone https://github.com/majid-ghanbari-dost/Kandoo.git
cd Kandoo
git checkout arena/4bb6cd51-kandoo
git pull origin arena/4bb6cd51-kandoo
```

## گام ۲: ساخت و اجرای Hub (ویندوز)

Hub یک برنامه ASP.NET Core است که با SQLite روی LAN اجرا می‌شود:

```powershell
dotnet publish src/Kandoo.Hub/Kandoo.Hub.csproj `
    --configuration Release --runtime win-x64 --self-contained true `
    --output .\publish\win-x64
.\publish\win-x64\Kandoo.Hub.exe
```

Hub در http://localhost:5000 اجرا می‌شود. برای حالت نمایشی (demo) فایل appsettings.json را ویرایش کنید:

```json
{
  "Hub": {
    "Demo": {
      "Mode": true,
      "BaseUrl": "http://localhost:5000"
    }
  }
}
```

## گام ۳: اجرای برنامه ویندوز (MAUI)

```powershell
dotnet build src/Kandoo.App/Kandoo.App.csproj `
    --configuration Release --framework net8.0-windows10.0.19041.0
```

سپس فایل exe را در bin\Release\net8.0-windows10.0.19041.0\win10-x64\ پیدا کنید و اجرا نمایید.

## گام ۴: ساخت و اجرای APK اندروید

```bash
dotnet build src/Kandoo.App/Kandoo.App.csproj `
    --configuration Debug --framework net8.0-android
```

APK در bin\Debug\net8.0-android\ ایجاد می‌شود. آن را روی Galaxy J8 (یا شبیه‌ساز اندروید ۱۰+) نصب کنید.

## گام ۵: start-all (شروع همه سرویس‌ها)

برای شروع یک‌دفعه همه سرویس‌ها روی ویندوز:

```powershell
.\scripts\start-all.ps1
```

این اسکریپت:
۱. Hub را با حالت نمایشی اجرا می‌کند
۲. برنامه ویندوز را اجرا می‌کند (اگر ساخته شده باشد)
۳. لاگ‌ها را در .\logs\ نگهداری می‌کند

## پنج گام نمایشی مالک (Owner Demo)

مالک باید این پنج گام را روی دستگاه واقعی انجام دهد:

### گام۱ — اجرای نسخهٔ ویندوز

۱. فایل KandooHub-Setup.exe را اجرا کنید.
۲. برنامه هاب روی http://localhost:5000 اجرا می‌شود.
۳. تایید کنید صفحه اصلی هاب لود شده است.

### گام۲ — مسیر موبایل

۱. APK کاندو را روی گوشی اندروید (Galaxy J8 یا جدیدتر) نصب کنید.
۲. برنامه را باز کنید و آدرس هاب را وارد کنید.
۳. تایید کنید ارتباط با هاب برقرار شده است.

### گام۳ — دریافت و اعتبارسنجی OTP

۱. در برنامه موبایل، شماره تماس را وارد کنید.
۲. کد OTP از طریق فایل outbox دریافت شود.
۳. کد OTP را در برنامه وارد کنید و اعتبارسنجی را تایید کنید.

### گام۴ — ورود به مسیر مشاهدهٔ فاکتور

۱. از منوی اصلی، گزینه «مشاهدهٔ فاکتور» را انتخاب کنید.
۲. فهرستی از فاکتورهای صادر شده نمایش داده می‌شود.
۳. یک فاکتور را انتخاب کنید و صفحه جزئیات را باز کنید.

### گام۵ — مشاهدهٔ فاکتور واقعی و تطبیق با دادهٔ ثبت‌شده

۱. جزئیات فاکتور (شماره، تاریخ، مبالغ، مشتری) را روی صفحه موبایل مشاهده کنید.
۲. همان داده‌ها را در پنل هاب (ویندوز) بررسی کنید.
۳. تطبیق کنید که داده‌های موبایل با داده‌های ثبت‌شده در هاب مطابقت دارد.

## گام اختیاری: اندازه‌سازی شروع سرد (Cold Start)

این گام اختیاری است. مالک می‌تواند آن را انجام دهد یا آگاهانه رها کند:

```powershell
adb shell am start -W com.kandoo.app
```

خروجی TotalTime را در فایل cold-start-time.txt ذخیره کنید.

## ساخت setup.exe (Inno Setup)

اگر Inno Setup نصب باشد:

```powershell
choco install innosetup -y
iscc packaging\HubPilot\KandooHub.Installer.iss
```

خروجی: packaging\HubPilot\Output\KandooHub-Setup.exe

اگر Inno Setup در دسترس نباشد، فایل zip جایگزین به عنوان fallback استفاده می‌شود.

## متغیرهای محیطی مهم

ConnectionStrings__HubSqlite = Data Source=kandoo-hub.db;Pooling=False
Hub__Demo__Mode = false
Hub__Demo__BaseUrl = ""
Hub__OwnerBootstrapProof = ""

## تگ‌های CI

C-84: تگ o15-C-84، commit adaaeca098d937a988b796034b2f495239a49059، CI 44074149427

## محدودیت‌ها

- setup.exe فوری است؛ مالک باید امضا کند برای توزیع تولیدی.
- حالت نمایشی فقط به شبکه‌های محلی مجاز است (رنج‌های خصوصی 10/8، 172.16/12، 192.168/16 یا localhost).
- Galaxy J8 هدف نهایی است؛ تست روی شبیه‌ساز اندروید ۱۰ در CI انجام شده است.
