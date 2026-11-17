# RUN-LOCAL — Kandoo Order 15 Demo Week (ویندوز + اندروید)

این مستند تصویری از اجرا محلی برنامه кан도و برای نمایش rooted در ۷ روزه (از ۲۰۲۶-۱۱-۱۶ است) نوشته شده است. مالک روی ویندوز و/یا گوشی اندروید می‌تواند از این مراحل برای تست نهایی استفاده کند.

## پیش‌نیازها

| مورد | نسخه | نکته |
|------|-------|------|
| .NET 8 SDK | 8.0.404+ | برای ساخت و اجرا Hub و برنامه ویندوز |
| Inno Setup | 6.x | برای ساخت setup.exe (اختیاری؛ fallback zip موجود است) |
| Android SDK | API 21+ | برای ساخت APK اندروید (Galaxy J8 هدف است) |
| Git | 2.x | برای کلون ریپازیتوری |

## Schritt ۱: کلون ریپازیتوری

```bash
git clone https://github.com/majid-ghanbari-dost/Kandoo.git
cd Kandoo
git checkout arena/4bb6cd51-kandoo
git pull origin arena/4bb6cd51-kandoo
```

## Schritt ۲: ساخت و اجرای Hub (ویندوز)

Hub یک برنامه ASP.NET Core است که با SQLite روی LAN اجرا می‌شود:

```powershell
# انتشار خود‌کافي برای ویندوز
dotnet publish src/Kandoo.Hub/Kandoo.Hub.csproj `
    --configuration Release --runtime win-x64 --self-contained true `
    --output .\publish\win-x64

# اجرا
.\publish\win-x64\Kandoo.Hub.exe
```

Hub در `http://localhost:5000` (یا پورت Configure شده) اجرا می‌شود. برای حالت نمایشی (demo) فایل `appsettings.json` را ویرایش کنید:

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

## Schritt ۳: اجرای برنامه ویندوز (MAUI)

```powershell
dotnet build src/Kandoo.App/Kandoo.App.csproj `
    --configuration Release --framework net8.0-windows10.0.19041.0
```

سپس فایل exe را در `bin\Release\net8.0-windows10.0.19041.0\win10-x64\` پیدا کنید و اجرا نمایید.

## Schritt ۴: ساخت و اجرای APK اندروید

```bash
dotnet build src/Kandoo.App/Kandoo.App.csproj `
    --configuration Debug --framework net8.0-android
```

APK در `bin\Debug\net8.0-android\` ایجاد می‌شود. آن را روی Galaxy J8 (یا emulateر اندروید ۱۰+) نصب کنید.

## Schritt ۵: start-all (شروع همه سرویس‌ها)

برای شروع یک‌دفعه همه سرویس‌ها روی ویندوز:

```powershell
.\scripts\start-all.ps1
```

این اسکریپت:
۱. Hub را با حالت نمایشی اجرا می‌کند
۲. برنامه ویندوز را اجرا می‌کند (اگر ساخته شده باشد)
۳. لاگ‌ها را در `.\logs\` نگهداری می‌کند

## पर्याय: setup.exe (Inno Setup)

اگر Inno Setup نصب باشد:

```powershell
choco install innosetup -y
iscc packaging\HubPilot\KandooHub.Installer.iss
```

خروجی: `packaging\HubPilot\Output\KandooHub-Setup.exe`

اگر Inno Setup در دسترس نباشد، فایل zip جایگزین به عنوان fallback استفاده می‌شود.

## متغیرهای محیطی مهم

| متغیر | مقدار پیش‌فرض | توضیح |
|--------|--------------|-------|
| `ConnectionStrings__HubSqlite` | `Data Source=kandoo-hub.db;Pooling=False` | رشته اتصال SQLite |
| `Hub__Demo__Mode` | `false` | حالت نمایشی (داده‌های استیک کد شده) |
| `Hub__Demo__BaseUrl` | `""` | نشانی پایه هاب برای حالت نمایشی |
| `Hub__OwnerBootstrapProof` | `""` | começa初めて owner session (خالی است؛ Clerk باید تنظیم کند) |

## تگ‌های CI

| تسک | تگ | commit کامل | CI |
|------|-----|-------------|-----|
| C-84 | o15-C-84 | adaaeca098d937a988b796034b2f495239a49059 | 44074149427 |

## محدودیت‌ها

- setup.exe Immediate (بدین امضا) است؛ مالک باید امضا کند برای توزیع تولیدی.
- حالت نمایشی فقط به mensch LAN مجاز است (رنج‌های خصوصی 10/8، 172.16/12، 192.168/16 یا localhost).
- Galaxy J8 هدف نهایی است؛ تست روی emulateر اندروید ۱۰ در CI انجام شده است.