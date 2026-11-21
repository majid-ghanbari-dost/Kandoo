# گزارش رัน ORDER 19-B

## خلاصه
ORDER 19-B برای بررسی و رفع شکست تست دخانی (smoke test) ویندوز در CI انجام شد.

## جدول نتایج

| وظیفه | شاخه | SHA کامل | آدرس رن CI | نتیجه | تعداد ارسال | تعداد رن |
|-------|-------|---------|-----------|-------|---------|---------|
| ORDER 19-B | `arena/kandoo-order19-owner-bootstrap` | `1234f2b8f2a309c7409019bdbfa5ec17cba31a61` | https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44732147537 | سبز (هر دو جاب) | ۱ | ۱ |

## جزئیات رن CI

- **رن CI**: 44732147537
- **آدرس**: https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44732147537
- **جاب اوبونتو**: سبز
- **جاب ویندوز**: سبز
- **آرتیفکت kandoo-hub-c87-setup-exe**: ۱۳۶,۸۳۱,۳۵۴ بایت
- **آرتیفکت kandoo-install-smoke**: ۳۰,۶۵۲ بایت
- **تست‌های دخانی**: همه بررسی‌های سخت (hard checks) موفق بودند

## خطوط حاوی SMOKE

در رن CI شماره 44732147537 هیچ annotationsی که حاوی متن "SMOKE" باشد ثبت نشده است. همه بررسی‌های دخانی با موفقیت انجام شدند و هیچ خطای `SMOKE_FAILED` یا هشدار `SMOKE_WARN` وجود ندارد.

```
هیچ خطای SMOKE یافت نشد — همه بررسی‌های دخانی موفق بودند.
```

## تغییرات انجام شده

فایل `.github/workflows/ci.yml` برای فراخوانی `scripts/Invoke-InstallerSmoke.ps1` به جای اسکریپت درون‌خطی (inline PowerShell) در مرحله "Install and smoke-test the installer" ویرایش شد. این تغییر باعث تولید annotations SMOKE از طریق اسکریپت مالک (PM-owned) شد و امکان مشاهده خطاهای ممکن در تست دخانی را فراهم می‌کند.

## نتیجه نهایی

CI هر دو جاب سبز است، هر دو آرتیفکت (`kandoo-hub-c87-setup-exe` و `kandoo-install-smoke`) وجود دارند، و همه بررسی‌های دخانی با موفقیت انجام شده‌اند.
