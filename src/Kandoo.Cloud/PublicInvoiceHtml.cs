using System.Globalization;
using System.Net;
using System.Text;
using Kandoo.Core;

namespace Kandoo.Cloud;

public static class PublicInvoiceHtml
{
    public static string Render(PublicInvoice invoice)
    {
        var view = invoice.View;
        var rows = new StringBuilder();
        foreach (var line in invoice.LineItems)
        {
            rows.Append("<tr><td>").Append(Encode(line.ProductName))
                .Append("</td><td>").Append(line.QuantityMillis.ToString(CultureInfo.InvariantCulture))
                .Append("</td><td>").Append(line.UnitPriceRials.ToString("N0", CultureInfo.InvariantCulture))
                .Append("</td><td>").Append(line.LineTotalRials.ToString("N0", CultureInfo.InvariantCulture))
                .AppendLine("</td></tr>");
        }

        if (invoice.LineItems.Count == 0)
        {
            rows.Append("<tr><td colspan=\"4\">—</td></tr>");
        }

        var issuedAtTehran = JalaliDateConverter.ToTehran(view.IssuedAt);
        var issuedJalali = JalaliDateConverter.Format(view.IssuedAt);
        return $$"""
            <!doctype html>
            <html lang="fa" dir="rtl">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{{Encode(view.ProductName)}}</title>
              <style>
                @font-face {
                  font-family: "Vazirmatn";
                  src: url("/fonts/Vazirmatn-Regular.ttf") format("truetype");
                  font-style: normal;
                  font-weight: 400;
                  font-display: swap;
                }
                @font-face {
                  font-family: "Vazirmatn";
                  src: url("/fonts/Vazirmatn-Bold.ttf") format("truetype");
                  font-style: normal;
                  font-weight: 700;
                  font-display: swap;
                }
                :root {
                  color-scheme: light;
                  --honey: #F2A900;
                  --ink: #2B1D0E;
                  --cream: #FFF8E7;
                  --wax: #F5E9C8;
                  --green: #256B43;
                  --review: #9A3B0A;
                  --paper: #FFFFFF;
                  --muted: #6B5B47;
                  --line: #D8C9A6;
                  --font-body: "Vazirmatn", Tahoma, Arial, sans-serif;
                  --weight-regular: 400;
                  --weight-bold: 700;
                  --text-small: 0.875rem;
                  --text-body: 1rem;
                  --text-title: 1.75rem;
                  --text-total: 1.25rem;
                  --leading-body: 1.8;
                  --space-1: 0.25rem;
                  --space-2: 0.5rem;
                  --space-3: 0.75rem;
                  --space-4: 1rem;
                  --space-5: 1.5rem;
                  --space-6: 2rem;
                  --space-7: 3rem;
                  --radius: 1rem;
                  --rule: 1px;
                  --accent-rule: 4px;
                  --content-width: 56rem;
                  --print-margin: 12mm;
                }
                * { box-sizing: border-box; }
                body {
                  margin: 0;
                  background: var(--cream);
                  color: var(--ink);
                  font-family: var(--font-body);
                  font-size: var(--text-body);
                  line-height: var(--leading-body);
                }
                main {
                  max-width: var(--content-width);
                  margin: var(--space-6) auto;
                  padding: var(--space-6);
                  background: var(--paper);
                  border: var(--rule) solid var(--line);
                  border-radius: var(--radius);
                }
                header {
                  padding-bottom: var(--space-5);
                  border-bottom: var(--accent-rule) solid var(--honey);
                }
                .eyebrow {
                  margin: 0 0 var(--space-2);
                  color: var(--green);
                  font-size: var(--text-small);
                  font-weight: var(--weight-bold);
                }
                h1 {
                  margin: 0;
                  font-size: var(--text-title);
                  line-height: var(--leading-body);
                }
                .store { margin: var(--space-2) 0 0; color: var(--muted); }
                dl {
                  display: grid;
                  grid-template-columns: minmax(9rem, 0.4fr) minmax(0, 1fr);
                  gap: var(--space-3) var(--space-5);
                  margin: var(--space-5) 0;
                  padding: var(--space-4);
                  background: var(--wax);
                  border-radius: var(--radius);
                }
                dt { font-weight: var(--weight-bold); }
                dd { margin: 0; overflow-wrap: anywhere; }
                .data-gap {
                  margin: var(--space-5) 0;
                  padding: var(--space-4);
                  color: var(--review);
                  background: var(--cream);
                  border: var(--rule) dashed var(--review);
                  border-radius: var(--radius);
                }
                .data-gap h2 { margin: 0 0 var(--space-2); font-size: var(--text-body); }
                .data-gap p { margin: var(--space-1) 0; }
                .lines { overflow-x: auto; }
                table { width: 100%; border-collapse: collapse; margin-top: var(--space-5); }
                th, td {
                  border: var(--rule) solid var(--line);
                  padding: var(--space-3);
                  text-align: right;
                  vertical-align: top;
                }
                th { background: var(--wax); font-weight: var(--weight-bold); }
                td:nth-child(n+2) { white-space: nowrap; }
                .total {
                  margin: var(--space-5) 0 0;
                  padding: var(--space-4);
                  color: var(--green);
                  background: var(--cream);
                  border-right: var(--accent-rule) solid var(--honey);
                  font-size: var(--text-total);
                  font-weight: var(--weight-bold);
                }
                .legal {
                  margin-top: var(--space-5);
                  padding-top: var(--space-4);
                  color: var(--muted);
                  border-top: var(--rule) dashed var(--line);
                  font-size: var(--text-small);
                }
                @media (max-width: 640px) {
                  main { margin: 0; padding: var(--space-4); border-radius: 0; }
                  dl { grid-template-columns: minmax(0, 1fr); gap: var(--space-1); }
                  dd { margin-bottom: var(--space-3); }
                }
                @media print {
                  body { background: var(--paper); }
                  main { margin: 0; max-width: none; padding: 0; border: 0; }
                  table { page-break-inside: auto; }
                  tr { page-break-inside: avoid; }
                }
                @page { margin: var(--print-margin); }
              </style>
            </head>
            <body>
              <main>
                <header>
                  <p class="eyebrow">فاکتور دیجیتال کندو</p>
                  <h1>{{Encode(view.ProductName)}}</h1>
                  <p class="store">{{Encode(view.StoreName)}}</p>
                </header>
                <dl>
                  <dt>شماره فاکتور</dt><dd>{{view.DisplayNumber.ToString(CultureInfo.InvariantCulture)}}</dd>
                  <dt>وضعیت</dt><dd>{{Encode(view.LifecycleStatus)}}</dd>
                  <dt>تاریخ و زمان صدور (جلالی، Asia/Tehran)</dt><dd>{{issuedJalali}}، {{issuedAtTehran.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}}</dd>
                  <dt>شناسه فاکتور</dt><dd dir="ltr">{{view.DigitalInvoice.InvoiceId}}</dd>
                </dl>
                <section class="data-gap" aria-label="محدودیت‌های داده صدور">
                  <h2>محدودیت‌های داده صدور</h2>
                  <p>نام مشتری (<span dir="ltr">customer_name</span>) در داده صدور این فاکتور موجود نیست.</p>
                  <p>روش پرداخت (<span dir="ltr">payment_method</span>) به‌صورت فیلد مستقل این فاکتور در داده صدور موجود نیست.</p>
                </section>
                <div class="lines">
                  <table>
                    <thead><tr><th scope="col">کالا</th><th scope="col">تعداد (هزارم واحد)</th><th scope="col">قیمت واحد (ریال)</th><th scope="col">جمع (ریال)</th></tr></thead>
                    <tbody>{{rows}}</tbody>
                  </table>
                </div>
                <p class="total">مبلغ قابل پرداخت: {{invoice.PayableRials.ToString("N0", CultureInfo.InvariantCulture)}} ریال</p>
                <p class="legal">{{Encode(view.LegalCopy.Content)}}</p>
              </main>
            </body>
            </html>
            """;
    }

    private static string Encode(string? value) => WebUtility.HtmlEncode(value) ?? string.Empty;
}
