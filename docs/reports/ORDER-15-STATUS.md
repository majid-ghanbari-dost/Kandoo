# گزارش وضعیت ORDER 15 — C# Lane و PHP Lane

## توضیحات کلی

این گزارش وضعیت پیگیری تسک‌های ORDER 15 را برای دو لاین C# و PHP ارائه می‌دهد. هر ردیف شامل: نام تسک، شاخه، کامیت کامل، لینک اجراهای CI و نتیجه است. هر فیلدی که از مخازن قابل recuperar نیست، مقدار BLOCKED-UNKNOWN دارد.

## لاین C# (ORDER 15 / ORDER 19-B)

| تسک | شاخه | کامیت کامل | لینک CI شاخه | نتیجه |
|------|--------|-----------|--------------|-------|
| C-80 | `arena/4bb6cd51-kandoo` | `46247639504c78dbf817ffb660ae453b9ec2ce23` | https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44027647544 | سبز (GREEN) |
| C-80b | `arena/4bb6cd51-kandoo` | `b2d8a0d267fd618bf16a1fc173c46544338d3558` | https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44039697223 | سبز (GREEN) |
| C-81 | `arena/4bb6cd51-kandoo` | `9c70dba693bbf19429b104e9a675703fd5a3b417` | https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44044338090 | سبز (GREEN) |
| C-81b | `arena/4bb6cd51-kandoo` | `35e5f7f6945a06d59ba9bc9c095df17983d0a3f7` | https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44050255755 | سبز (GREEN) |
| C-82 | `arena/4bb6cd51-kandoo` | `64cf70fa3e3ce08c39379815d546fa399bfd5c6a` | https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44058734355 | سبز (GREEN) |
| C-83 | `arena/4bb6cd51-kandoo` | `6903377f1a2e5832e63153df4306b2ee548b6f9c` | https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44066606436 | سبز (GREEN) |
| C-84 | `arena/4bb6cd51-kandoo` | `adaaeca098d937a988b796034b2f495239a49059` | https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44074149427 | سبز (GREEN) |
| C-85 | `arena/4bb6cd51-kandoo` | `aef2f6e99f8c5b532c51043c2bc2fe4cf7d12480` | https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44091485971 | سبز (GREEN) |
| C-86 | — | BLOCKED-UNKNOWN | BLOCKED-UNKNOWN | BLOCKED-UNKNOWN (تسک استرتچ، انجام نشده) |
| C-87 | `arena/4bb6cd51-kandoo` | `3e6f730204a9f8b4de8b651981284be1475c1731` | https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/44300282154 | سبز (GREEN) |

## لاین PHP (ORDER 15 P-tasks)

شاخه‌های مخزن `majid-ghanbari-dost/kandoo-cloud-php` در دسترس نیست (پاسخ 403 از API گیت‌هاب). کلیه فیلدهای لاین PHP مقدار BLOCKED-UNKNOWN دارند.

| تسک | شاخه | کامیت کامل | لینک CI | نتیجه |
|------|--------|-----------|----------|-------|
| P-30 | BLOCKED-UNKNOWN | BLOCKED-UNKNOWN | BLOCKED-UNKNOWN | BLOCKED-UNKNOWN |
| P-31 | BLOCKED-UNKNOWN | BLOCKED-UNKNOWN | BLOCKED-UNKNOWN | BLOCKED-UNKNOWN |
| P-32 | BLOCKED-UNKNOWN | BLOCKED-UNKNOWN | BLOCKED-UNKNOWN | BLOCKED-UNKNOWN |
| P-33 | BLOCKED-UNKNOWN | BLOCKED-UNKNOWN | BLOCKED-UNKNOWN | BLOCKED-UNKNOWN |
| P-34 | BLOCKED-UNKNOWN | BLOCKED-UNKNOWN | BLOCKED-UNKNOWN | BLOCKED-UNKNOWN |
| P-35 | BLOCKED-UNKNOWN | BLOCKED-UNKNOWN | BLOCKED-UNKNOWN | BLOCKED-UNKNOWN |

## Notes

- لاین C#: تمام تسک‌های C-80 تا C-87 به غیر از C-86 (استرتچ) تکمیل شده و CI سبز است.
- لاین PHP: مخزن قابل دسترسی نیست، تمام مقادیر BLOCKED-UNKNOWN.
- تسک C-86 (حالت مشتری اندروید) استرتچ است و انجام نشده است.
