using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Kandoo.App.Tests;

/// <summary>
/// C-97 «screens vs boards» text fidelity gate (emulator-free): every structural
/// text of the customer boards must exist verbatim in the built customer screens
/// (XAML) or in the view-state copy (Logic). Sample data and [placeholders] are
/// dynamic and excluded by construction. Comparison normalizes ZWNJ and the
/// ی/ي, ک/ك letter variants plus whitespace. Layout/color fidelity is verified by
/// the board comparison on device (screenshot per screen, reported in the PR).
/// </summary>
public sealed class C97CustomerBoardTextTests
{
    private static readonly (string Board, string Screen, string[] Files)[] Map =
    {
        (
            "docs/design/Customer-Login-v2.dc.html",
            "Customer login (Customer-Login-v2)",
            new[] { "src/Kandoo.App/CustomerOtpPage.xaml" }),
        (
            "docs/design/Customer-Locked.dc.html",
            "Customer locked state (Customer-Locked)",
            new[] { "src/Kandoo.App/CustomerOtpPage.xaml" }),
        (
            "docs/design/Customer-Invoices-v2.dc.html",
            "Customer invoices (Customer-Invoices-v2)",
            new[]
            {
                "src/Kandoo.App/CustomerInvoicesPage.xaml",
                "src/Kandoo.App/Logic/CustomerViewState.cs",
                "src/Kandoo.App/Logic/PersianDigits.cs",
            }),
        (
            "docs/design/Customer-Empty.dc.html",
            "Customer empty state (Customer-Empty)",
            new[] { "src/Kandoo.App/CustomerInvoicesPage.xaml" }),
        (
            "docs/design/Customer-New-Banner.dc.html",
            "Customer new-invoice banner (Customer-New-Banner)",
            new[]
            {
                "src/Kandoo.App/CustomerInvoicesPage.xaml",
                "src/Kandoo.App/Logic/CustomerViewState.cs",
            }),
        (
            "docs/design/Customer-Invoice-Detail.dc.html",
            "Customer invoice detail (Customer-Invoice-Detail)",
            new[]
            {
                "src/Kandoo.App/CustomerInvoiceDetailPage.xaml",
                "src/Kandoo.App/Logic/CustomerViewState.cs",
            }),
    };

    [Fact]
    public void C97_BoardTexts_Are_Verbatim_In_Customer_Screens()
    {
        foreach (var (board, screen, files) in Map)
        {
            var boardPath = P31VectorReplayTests.FindRepositoryFile(board);
            var boardText = Normalize(File.ReadAllText(boardPath));
            var haystack = Normalize(string.Join(
                "\n",
                files.Select(f => File.ReadAllText(P31VectorReplayTests.FindRepositoryFile(f)))));

            foreach (var required in RequiredTexts(board))
            {
                Assert.True(
                    haystack.Contains(Normalize(required), StringComparison.Ordinal),
                    $"{screen}: board text «{required}» not found in [{string.Join(", ", files)}]");
            }

            // Sanity: the board file itself contains every required text (no typo in this table).
            foreach (var required in RequiredTexts(board))
            {
                Assert.True(
                    boardText.Contains(Normalize(required), StringComparison.Ordinal),
                    $"{screen}: required text «{required}» is not in its own board file — fix this test table.");
            }
        }
    }

    [Fact]
    public void C97_Board_Colors_Are_Used_In_Customer_Screens()
    {
        // Board palette (docs/design/README.md + boards): honey, ink, cream, wax,
        // green, brown (Review). Screens must reference the shared tokens or the
        // exact hex values from the boards.
        var screens = new[]
        {
            "src/Kandoo.App/CustomerOtpPage.xaml",
            "src/Kandoo.App/CustomerInvoicesPage.xaml",
            "src/Kandoo.App/CustomerInvoiceDetailPage.xaml",
        };
        foreach (var screen in screens)
        {
            var xaml = File.ReadAllText(P31VectorReplayTests.FindRepositoryFile(screen));
            Assert.True(
                xaml.Contains("Honey", StringComparison.Ordinal) || xaml.Contains("#F2A900", StringComparison.Ordinal),
                $"{screen}: honey token missing");
            Assert.True(
                xaml.Contains("Ink", StringComparison.Ordinal) || xaml.Contains("#2B1D0E", StringComparison.Ordinal),
                $"{screen}: ink token missing");
        }
    }

    /// <summary>
    /// Structural board copy per screen (verbatim from the board files). Sample
    /// data (store names, numbers, amounts, dates) and [placeholders] are dynamic
    /// — covered by CustomerViewStateTests instead.
    /// </summary>
    private static IEnumerable<string> RequiredTexts(string board) => board switch
    {
        "docs/design/Customer-Login-v2.dc.html" => new[]
        {
            "ورود به کندو",
            "فاکتورهای خریدتان یک‌جا",
            "شماره‌ی موبایل",
            "ارسال کد",
            "کد تأیید",
            "ورود",
            "کد به همین شماره پیامک می‌شود. اگر فاکتوری برای این شماره ثبت نشده باشد، پیامکی نمی‌رود.",
        },
        "docs/design/Customer-Locked.dc.html" => new[]
        {
            "تعداد دفعات دریافت کد تمام شد",
            "برای این شماره دو بار کد فرستاده شده است. برای ادامه، به پشتیبانی کندو در تلگرام پیام بدهید. نیازی نیست دوباره تلاش کنید.",
            "پیام به پشتیبانی (تلگرام)",
            "شماره‌ی خود را در پیام بنویسید تا دسترسی‌تان بازگردانده شود.",
        },
        "docs/design/Customer-Invoices-v2.dc.html" => new[]
        {
            "فاکتورهای من",
            "نقش: مشتری",
            "فاکتور جدید",
            "جست‌وجو در فروشگاه‌ها و فاکتورها",
            "پرداخت‌شده",
            "مانده دارد",
            "تومان",
            "فاکتورها",
            "فروشگاه‌ها",
            "نقش‌ها",
        },
        "docs/design/Customer-Empty.dc.html" => new[]
        {
            "فاکتورهای من",
            "نقش: مشتری",
            "هنوز فاکتوری ندارید",
            "وقتی از یک فروشگاه کندو خرید کنید و شماره‌ی شما را ثبت کنند، فاکتور همین‌جا ظاهر می‌شود.",
        },
        "docs/design/Customer-New-Banner.dc.html" => new[]
        {
            "فاکتورهای من",
            "نقش: مشتری",
            "فاکتور جدید",
            "جدید",
            "پرداخت‌شده",
            "فاکتورها",
            "نقش‌ها",
        },
        "docs/design/Customer-Invoice-Detail.dc.html" => new[]
        {
            "فاکتور دیجیتال کندو",
            "تاریخ",
            "خریدار",
            "اقلام",
            "قابل پرداخت",
            "پرداخت",
            "کارت‌خوان",
            "نقدی",
            "نسیه",
            "مانده",
            "تماس با فروشگاه",
            "ذخیره‌ی فاکتور",
        },
        _ => throw new ArgumentOutOfRangeException(nameof(board), board, "Unknown board."),
    };

    /// <summary>ZWNJ + letter-variant + whitespace normalization for the comparison.</summary>
    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\u200c': // ZWNJ
                    continue;
                case 'ي':
                    builder.Append('ی');
                    break;
                case 'ك':
                    builder.Append('ک');
                    break;
                default:
                    builder.Append(ch);
                    break;
            }
        }

        return Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
    }
}
