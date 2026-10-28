using System.Globalization;
using System.Net;
using System.Text;

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
                .Append("</td><td>").Append(line.UnitPriceRials.ToString(CultureInfo.InvariantCulture))
                .Append("</td><td>").Append(line.LineTotalRials.ToString(CultureInfo.InvariantCulture))
                .AppendLine("</td></tr>");
        }

        if (invoice.LineItems.Count == 0)
        {
            rows.Append("<tr><td colspan=\"4\">—</td></tr>");
        }

        return $$"""
            <!doctype html>
            <html lang="fa" dir="rtl">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{{Encode(view.ProductName)}}</title>
              <style>
                :root { color-scheme: light; font-family: Tahoma, Arial, sans-serif; }
                body { margin: 0; background: #f4f6f8; color: #17202a; }
                main { max-width: 820px; margin: 2rem auto; padding: 2rem; background: white; }
                h1 { font-size: 1.5rem; }
                dl { display: grid; grid-template-columns: 10rem 1fr; gap: .5rem 1rem; }
                dt { font-weight: bold; }
                dd { margin: 0; }
                table { width: 100%; border-collapse: collapse; margin-top: 1.5rem; }
                th, td { border: 1px solid #ccd3da; padding: .55rem; text-align: right; }
                th { background: #edf1f5; }
                .total { font-size: 1.2rem; font-weight: bold; margin-top: 1rem; }
                .placeholder { border: 1px dashed #8a939e; padding: .75rem; margin-top: 1.5rem; }
                @media print {
                  body { background: white; }
                  main { margin: 0; max-width: none; padding: 0; }
                  .no-print { display: none; }
                  table { page-break-inside: auto; }
                  tr { page-break-inside: avoid; }
                }
                @page { margin: 12mm; }
              </style>
            </head>
            <body>
              <main>
                <h1>{{Encode(view.ProductName)}}</h1>
                <p class="no-print">فاکتور دیجیتال کندو</p>
                <dl>
                  <dt>فروشگاه</dt><dd>{{Encode(view.StoreName)}}</dd>
                  <dt>شماره فاکتور</dt><dd>{{view.DisplayNumber.ToString(CultureInfo.InvariantCulture)}}</dd>
                  <dt>وضعیت</dt><dd>{{Encode(view.LifecycleStatus)}}</dd>
                  <dt>زمان صدور</dt><dd>{{Encode(view.IssuedAt.ToString("O", CultureInfo.InvariantCulture))}}</dd>
                  <dt>شناسه فاکتور</dt><dd>{{view.DigitalInvoice.InvoiceId}}</dd>
                </dl>
                <table>
                  <thead><tr><th>کالا</th><th>تعداد (هزارم واحد)</th><th>قیمت واحد (ریال)</th><th>جمع (ریال)</th></tr></thead>
                  <tbody>{{rows}}</tbody>
                </table>
                <p class="total">مبلغ قابل پرداخت: {{invoice.PayableRials.ToString(CultureInfo.InvariantCulture)}} ریال</p>
                <p class="placeholder">{{Encode(view.LegalCopy.Content)}}</p>
              </main>
            </body>
            </html>
            """;
    }

    private static string Encode(string? value) => WebUtility.HtmlEncode(value) ?? string.Empty;
}
