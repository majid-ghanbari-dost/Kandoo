using System.Text.Json;
using Kandoo.Core;
using Xunit;

namespace Kandoo.Cloud.Tests;

public sealed class PublicInvoiceHtmlTests
{
    [Fact]
    public void C44_HtmlUsesLocalTokensFontsJalaliDateAndOnlyProjectionData()
    {
        var invoice = new PublicInvoice(
            new PublicInvoiceView
            {
                ProductName = "<b>صورتحساب</b>",
                StoreName = "فروشگاه آزمون",
                DisplayNumber = 42,
                LifecycleStatus = "ISSUED",
                IssuedAt = new DateTimeOffset(2026, 11, 1, 12, 30, 45, TimeSpan.Zero),
                DigitalInvoice = new PublicDigitalInvoice
                {
                    InvoiceId = Guid.NewGuid(),
                    IssuedPayload = JsonSerializer.SerializeToElement(new
                    {
                        customer_name = "payload-only-customer",
                        payment_method = "payload-only-method",
                        marker = "payload-only-secret"
                    })
                },
                LegalCopy = new PublicLegalCopyResource { Content = "<b>متن حقوقی آزمایشی</b>" }
            },
            new[]
            {
                new PublicInvoiceLine
                {
                    ProductName = "<i>کالا</i>",
                    QuantityMillis = 2000,
                    UnitPriceRials = 2500,
                    LineTotalRials = 5000
                }
            },
            5000);

        var html = PublicInvoiceHtml.Render(invoice);

        Assert.Contains("lang=\"fa\" dir=\"rtl\"", html, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(html, "<style>"));
        Assert.Equal(1, Occurrences(html, ":root"));
        Assert.Contains("@font-face", html, StringComparison.Ordinal);
        Assert.Contains("/fonts/Vazirmatn-Regular.ttf", html, StringComparison.Ordinal);
        Assert.Contains("/fonts/Vazirmatn-Bold.ttf", html, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", html, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", html, StringComparison.Ordinal);
        foreach (var token in new[]
                 {
                     "--honey: #F2A900", "--ink: #2B1D0E", "--cream: #FFF8E7",
                     "--wax: #F5E9C8", "--green: #256B43", "--review: #9A3B0A"
                 })
        {
            Assert.Contains(token, html, StringComparison.Ordinal);
        }

        Assert.Contains("var(--honey)", html, StringComparison.Ordinal);
        Assert.Contains(JalaliDateConverter.Format(invoice.View.IssuedAt), html, StringComparison.Ordinal);
        Assert.Contains("Asia/Tehran", html, StringComparison.Ordinal);
        Assert.Contains("customer_name", html, StringComparison.Ordinal);
        Assert.Contains("payment_method", html, StringComparison.Ordinal);
        Assert.Contains("5,000", html, StringComparison.Ordinal);
        Assert.Contains("ریال", html, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;صورتحساب&lt;/b&gt;", html, StringComparison.Ordinal);
        Assert.Contains("&lt;i&gt;کالا&lt;/i&gt;", html, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;متن حقوقی آزمایشی&lt;/b&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("payload-only-customer", html, StringComparison.Ordinal);
        Assert.DoesNotContain("payload-only-method", html, StringComparison.Ordinal);
        Assert.DoesNotContain("payload-only-secret", html, StringComparison.Ordinal);
    }

    private static int Occurrences(string value, string expected) =>
        value.Split(expected, StringSplitOptions.None).Length - 1;
}
