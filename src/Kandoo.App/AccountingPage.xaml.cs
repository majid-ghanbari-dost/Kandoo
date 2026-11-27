using System.Security.Cryptography;
using System.Text.Json;

namespace Kandoo.App;

public sealed partial class AccountingPage : ContentPage
{
    private enum AccountingTab
    {
        Overview,
        SalesReport,
        Receivables,
        InventoryValue
    }

    private readonly HttpClient _httpClient = new();
    private HubConnectionSettings? _settings;
    private AccountingTab _activeTab = AccountingTab.Overview;
    private AccountingPeriodKind _activePeriod = AccountingPeriodKind.Today;
    private AccountingSummaryView? _summary;
    private AccountingStatsView? _stats;
    private IReadOnlyList<ProductRow> _products = Array.Empty<ProductRow>();
    private IReadOnlyList<CustomerRow> _customers = Array.Empty<CustomerRow>();

    public AccountingPage(HubConnectionSettings? settings = null)
    {
        _settings = HubConnectionResolver.Resolve(settings) ?? settings;
        InitializeComponent();
        StoreSidebarHelper.AttachSidebar(
            this,
            SidebarHost,
            StoreSidebarSection.Accounting,
            () => _settings);

        ApplyRoundedChrome();
        UpdateTabVisibility();
        UpdatePeriodChips();
        StatusLabel.Text = HasValidSettings(_settings)
            ? "آماده بارگذاری حسابداری مدیریتی از هاب."
            : "تنظیمات اتصال به هاب یافت نشد؛ ابتدا راه‌اندازی یا جفت‌سازی را انجام دهید.";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _settings = HubConnectionResolver.Resolve(_settings) ?? _settings;
        if (HasValidSettings(_settings))
        {
            await LoadFromHubAsync();
        }
    }

    private void ApplyRoundedChrome()
    {
        Border[] cards =
        {
            SalesKpiCard, ProfitKpiCard, ReceivablesKpiCard, StockValueKpiCard,
            PaymentBreakdownCard, UnpricedProfitBanner, ProViewBadgeBorder, ProLedgerTableCard,
            SrTotalSalesCard, SrInvoiceCountCard, SrAverageInvoiceCard, SrMethodsTableCard,
            RecTotalCard, RecTableCard, InvTotalValueCard, InvUnpricedCountCard, InvGroupTableCard
        };
        foreach (var border in cards)
        {
            StoreSidebarHelper.ApplyRoundedCorners(border, 12);
        }
    }

    private async void OnRefreshClicked(object? sender, EventArgs e) => await LoadFromHubAsync();

    private void OnProViewToggled(object? sender, CheckedChangedEventArgs e) => UpdateTabVisibility();

    private void OnTabOverviewClicked(object? sender, EventArgs e) => SelectTab(AccountingTab.Overview);

    private void OnTabSalesReportClicked(object? sender, EventArgs e) => SelectTab(AccountingTab.SalesReport);

    private void OnTabReceivablesClicked(object? sender, EventArgs e) => SelectTab(AccountingTab.Receivables);

    private void OnTabInventoryValueClicked(object? sender, EventArgs e) => SelectTab(AccountingTab.InventoryValue);

    private void OnPeriodTodayClicked(object? sender, EventArgs e) => SelectPeriod(AccountingPeriodKind.Today);

    private void OnPeriodWeekClicked(object? sender, EventArgs e) => SelectPeriod(AccountingPeriodKind.Week);

    private void OnPeriodMonthClicked(object? sender, EventArgs e) => SelectPeriod(AccountingPeriodKind.Month);

    private async void OnOpenProductsClicked(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new ProductsPage(_settings));
    }

    private async void OnOpenInvoicesClicked(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new InvoicesListPage(_settings));
    }

    private async void OnNewManualEntryClicked(object? sender, EventArgs e)
    {
        await DisplayAlert(
            "سند دستی",
            "ثبت سند دستی در هاب (C-94 v0) هنوز مسیر نوشتن ندارد؛ در نسخهٔ فعلی دفتر روزنامه فقط‌خواندنی است و از فاکتورهای صادرشده استخراج می‌شود.",
            "متوجه شدم");
    }

    private void SelectTab(AccountingTab tab)
    {
        _activeTab = tab;
        if (ProViewCheckBox.IsChecked)
        {
            ProViewCheckBox.IsChecked = false;
        }

        UpdateTabVisibility();
    }

    private void SelectPeriod(AccountingPeriodKind period)
    {
        _activePeriod = period;
        UpdatePeriodChips();
        RenderData();
    }

    private void UpdateTabVisibility()
    {
        var isPro = ProViewCheckBox.IsChecked;
        SubViewChipsRow.IsVisible = !isPro;

        if (isPro)
        {
            PageTitleLabel.Text = "دفتر روزنامه (نمای حرفه‌ای)";
            PeriodChipsRow.IsVisible = false;
            OverviewSection.IsVisible = false;
            SalesReportSection.IsVisible = false;
            ReceivablesSection.IsVisible = false;
            InventoryValueSection.IsVisible = false;
            ProViewSection.IsVisible = true;
            return;
        }

        ProViewSection.IsVisible = false;
        OverviewSection.IsVisible = _activeTab == AccountingTab.Overview;
        SalesReportSection.IsVisible = _activeTab == AccountingTab.SalesReport;
        ReceivablesSection.IsVisible = _activeTab == AccountingTab.Receivables;
        InventoryValueSection.IsVisible = _activeTab == AccountingTab.InventoryValue;
        PeriodChipsRow.IsVisible = _activeTab is AccountingTab.Overview or AccountingTab.SalesReport;

        PageTitleLabel.Text = _activeTab switch
        {
            AccountingTab.SalesReport => "گزارش فروش",
            AccountingTab.Receivables => "طلب از مشتری‌ها",
            AccountingTab.InventoryValue => "ارزش موجودی انبار",
            _ => "حسابداری"
        };

        StyleChip(TabOverviewChip, _activeTab == AccountingTab.Overview);
        StyleChip(TabSalesReportChip, _activeTab == AccountingTab.SalesReport);
        StyleChip(TabReceivablesChip, _activeTab == AccountingTab.Receivables);
        StyleChip(TabInventoryValueChip, _activeTab == AccountingTab.InventoryValue);
    }

    private void UpdatePeriodChips()
    {
        StyleChip(PeriodTodayChip, _activePeriod == AccountingPeriodKind.Today);
        StyleChip(PeriodWeekChip, _activePeriod == AccountingPeriodKind.Week);
        StyleChip(PeriodMonthChip, _activePeriod == AccountingPeriodKind.Month);
    }

    private static void StyleChip(Button chip, bool active)
    {
        chip.BackgroundColor = active ? StoreSidebarHelper.Honey : StoreSidebarHelper.Wax;
        chip.FontFamily = active ? "Vazirmatn-Bold" : "Vazirmatn";
    }

    private async Task LoadFromHubAsync()
    {
        _settings = HubConnectionResolver.Resolve(_settings) ?? _settings;
        if (!HasValidSettings(_settings))
        {
            StatusLabel.Text = "تنظیمات اتصال به هاب و توکن نشست لازم است.";
            StatusLabel.TextColor = StoreSidebarHelper.Review;
            return;
        }

        RefreshButton.IsEnabled = false;
        try
        {
            var accountingSource = new HubAccountingDataSource(_httpClient, _settings!);
            _summary = await accountingSource.LoadSummaryAsync();
            _stats = await accountingSource.LoadStatsAsync();

            try
            {
                var productSource = new HubProductDataSource(_httpClient, _settings!);
                _products = await productSource.LoadProductsAsync(groupId: null, query: null, limit: 200);
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                _products = Array.Empty<ProductRow>();
            }

            try
            {
                var customerSource = new HubCustomersDataSource(_httpClient, _settings!);
                var directory = await customerSource.LoadDirectoryAsync();
                _customers = directory.Customers.Where(c => c.OutstandingRials > 0).ToArray();
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                _customers = Array.Empty<CustomerRow>();
            }

            StatusLabel.TextColor = StoreSidebarHelper.Green;
            StatusLabel.Text = "گزارش حسابداری مدیریتی از رویدادهای قطعی هاب بارگذاری شد.";
            RenderData();
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            StatusLabel.TextColor = StoreSidebarHelper.Review;
            StatusLabel.Text = $"خطا در دریافت داده‌های حسابداری: {exception.Message}";
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private void RenderData()
    {
        if (_summary is null)
        {
            return;
        }

        var periodName = _activePeriod switch
        {
            AccountingPeriodKind.Week => "این هفته",
            AccountingPeriodKind.Month => "این ماه",
            _ => "امروز"
        };

        var periodStats = _stats?.ForPeriod(_activePeriod);
        SalesKpiTitleLabel.Text = $"فروش {periodName}";
        SalesKpiSubLabel.Text = $"جمع همه‌ی فاکتورهای {periodName}";
        SalesKpiValueLabel.Text = periodStats?.SalesDisplay ?? FormattableString.Invariant($"{_summary.ProfitSalesRials:N0} ریال");
        ProfitKpiValueLabel.Text = _summary.ProfitDisplay;
        ReceivablesKpiValueLabel.Text = _summary.ReceivablesDisplay;
        ReceivablesKpiSubLabel.Text = _summary.ReceivableInvoiceCount > 0
            ? FormattableString.Invariant($"{_summary.ReceivableInvoiceCount:N0} فاکتور دارای مانده")
            : "فاکتورهای پرداخت‌نشده";
        StockValueKpiValueLabel.Text = _summary.StockValueDisplay;

        PaymentBreakdownTitleLabel.Text = $"فروش {periodName} به تفکیک روش پرداخت";
        if (periodStats is not null)
        {
            OverviewCardReaderLabel.Text = periodStats.CardReader.AmountDisplay;
            OverviewCardToCardLabel.Text = periodStats.CardToCard.AmountDisplay;
            OverviewCashLabel.Text = periodStats.Cash.AmountDisplay;
            OverviewCreditOrUnknownLabel.Text = periodStats.Unknown.AmountDisplay;
        }
        else
        {
            OverviewCardReaderLabel.Text = _summary.CardReader.AmountDisplay;
            OverviewCardToCardLabel.Text = _summary.CardToCard.AmountDisplay;
            OverviewCashLabel.Text = _summary.Cash.AmountDisplay;
            OverviewCreditOrUnknownLabel.Text = _summary.Credit.AmountDisplay;
        }

        UnpricedProfitLabel.Text =
            FormattableString.Invariant($"سود فقط برای کالاهایی حساب می‌شود که قیمت خرید دارند. {_summary.UnpricedProductsCount:N0} کالا قیمت خرید ندارد و در سود نیامده است.");

        RenderProLedger(_summary);
        RenderSalesReport(periodStats, _summary);
        RenderReceivables(_summary);
        RenderInventoryValue(_summary);
    }

    private void RenderProLedger(AccountingSummaryView summary)
    {
        ProLedgerRowsStack.Children.Clear();
        ProNoteLabel.Text = $"{summary.ProfessionalNote} سندهای خودکار قابل ویرایش نیستند؛ اصلاح فقط با سند برگشت انجام می‌شود.";

        if (summary.LedgerRows.Count == 0)
        {
            ProLedgerRowsStack.Add(BuildEmptyTableMessage("هنوز سندی در دفتر روزنامه ثبت نشده است."));
            return;
        }

        foreach (var row in summary.LedgerRows)
        {
            ProLedgerRowsStack.Add(new BoxView { HeightRequest = 1, Color = StoreSidebarHelper.DividerCream });
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(70)),
                    new ColumnDefinition(new GridLength(2, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(150)),
                    new ColumnDefinition(new GridLength(150)),
                    new ColumnDefinition(new GridLength(90)),
                },
                Padding = new Thickness(16, 12),
                ColumnSpacing = 8
            };
            grid.Add(new Label { Text = row.SequenceNumber.ToString(), FontFamily = "Vazirmatn-Bold", FontSize = 14, TextColor = StoreSidebarHelper.Ink, VerticalOptions = LayoutOptions.Center }, 0, 0);
            grid.Add(new Label { Text = row.Account, FontFamily = "Vazirmatn", FontSize = 14, TextColor = StoreSidebarHelper.Ink, VerticalOptions = LayoutOptions.Center }, 1, 0);
            grid.Add(new Label { Text = row.DebitDisplay, FontFamily = "Vazirmatn-Bold", FontSize = 14, TextColor = StoreSidebarHelper.Ink, VerticalOptions = LayoutOptions.Center }, 2, 0);
            grid.Add(new Label { Text = row.CreditDisplay, FontFamily = "Vazirmatn-Bold", FontSize = 14, TextColor = StoreSidebarHelper.Ink, VerticalOptions = LayoutOptions.Center }, 3, 0);

            var badge = new Border
            {
                BackgroundColor = Color.FromArgb(row.SourceBadgeHex),
                StrokeThickness = 0,
                Padding = new Thickness(10, 4),
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions = LayoutOptions.Center,
                Content = new Label
                {
                    Text = row.SourceBadgeText,
                    FontFamily = "Vazirmatn-Bold",
                    FontSize = 12,
                    TextColor = Colors.White
                }
            };
            StoreSidebarHelper.ApplyRoundedCorners(badge, 6);
            grid.Add(badge, 4, 0);
            ProLedgerRowsStack.Add(grid);
        }
    }

    private void RenderSalesReport(AccountingPeriodStats? periodStats, AccountingSummaryView summary)
    {
        SrTotalSalesValueLabel.Text = periodStats?.SalesDisplay ?? FormattableString.Invariant($"{summary.ProfitSalesRials:N0} ریال");
        SrInvoiceCountValueLabel.Text = periodStats?.InvoiceCountDisplay ?? "—";
        SrAverageInvoiceValueLabel.Text = periodStats?.AverageInvoiceDisplay ?? "—";

        SrMethodsRowsStack.Children.Clear();
        var methodRows = periodStats is not null
            ? new (string Name, string Amount)[]
            {
                ("کارت‌خوان", periodStats.CardReader.AmountDisplay),
                ("کارت‌به‌کارت", periodStats.CardToCard.AmountDisplay),
                ("نقدی", periodStats.Cash.AmountDisplay),
                ("نسیه (کل دوره)", summary.Credit.AmountDisplay),
            }
            : new (string Name, string Amount)[]
            {
                ("کارت‌خوان", summary.CardReader.AmountDisplay),
                ("کارت‌به‌کارت", summary.CardToCard.AmountDisplay),
                ("نقدی", summary.Cash.AmountDisplay),
                ("نسیه", summary.Credit.AmountDisplay),
            };

        foreach (var (name, amount) in methodRows)
        {
            SrMethodsRowsStack.Add(new BoxView { HeightRequest = 1, Color = StoreSidebarHelper.DividerCream });
            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(new GridLength(180))
                },
                Padding = new Thickness(16, 12)
            };
            row.Add(new Label { Text = name, FontFamily = "Vazirmatn", FontSize = 15, TextColor = StoreSidebarHelper.Ink }, 0, 0);
            row.Add(new Label { Text = amount, FontFamily = "Vazirmatn-Bold", FontSize = 15, TextColor = StoreSidebarHelper.Ink }, 1, 0);
            SrMethodsRowsStack.Add(row);
        }
    }

    private void RenderReceivables(AccountingSummaryView summary)
    {
        RecTotalValueLabel.Text = summary.ReceivablesDisplay;
        RecSubNoteLabel.Text = summary.UnrecordedReceivableInvoices > 0
            ? FormattableString.Invariant($"{summary.ReceivableInvoiceCount:N0} فاکتور دارای طلب ثبت‌شده · {summary.UnrecordedReceivableInvoices:N0} فاکتور بدون ماندهٔ ثبت‌شده")
            : FormattableString.Invariant($"{summary.ReceivableInvoiceCount:N0} فاکتور دارای طلب");

        RecRowsStack.Children.Clear();
        if (_customers.Count == 0)
        {
            RecRowsStack.Add(BuildEmptyTableMessage(
                summary.ReceivableOutstandingRials > 0
                    ? "جمع طلب در هاب ثبت شده است؛ مسیر فهرست تفکیکی بدهکاران هنوز در هاب فعال نیست."
                    : "طلب باز از مشتری‌ها وجود ندارد."));
            return;
        }

        foreach (var cust in _customers)
        {
            RecRowsStack.Add(new BoxView { HeightRequest = 1, Color = StoreSidebarHelper.DividerCream });
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(2, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(140)),
                    new ColumnDefinition(new GridLength(140)),
                    new ColumnDefinition(new GridLength(120)),
                },
                Padding = new Thickness(16, 12)
            };
            grid.Add(new Label { Text = cust.Name, FontFamily = "Vazirmatn-Bold", FontSize = 14, TextColor = StoreSidebarHelper.Ink, VerticalOptions = LayoutOptions.Center }, 0, 0);
            grid.Add(new Label { Text = cust.Phone, FontFamily = "Vazirmatn", FontSize = 14, TextColor = StoreSidebarHelper.MutedInk, VerticalOptions = LayoutOptions.Center }, 1, 0);
            grid.Add(new Label { Text = cust.OutstandingDisplay, FontFamily = "Vazirmatn-Bold", FontSize = 14, TextColor = StoreSidebarHelper.Review, VerticalOptions = LayoutOptions.Center }, 2, 0);

            var settleBtn = new Button
            {
                Text = "ثبت پرداخت",
                FontFamily = "Vazirmatn",
                FontSize = 13,
                BackgroundColor = StoreSidebarHelper.Wax,
                TextColor = StoreSidebarHelper.Ink,
                CornerRadius = 16,
                MinimumHeightRequest = 36,
                Padding = new Thickness(12, 4)
            };
            settleBtn.Clicked += async (_, _) =>
            {
                await DisplayAlert("ثبت پرداخت", "ثبت تسویهٔ نسیه روی فاکتور در هاب هنوز مسیر اختصاصی ندارد.", "متوجه شدم");
            };
            grid.Add(settleBtn, 3, 0);
            RecRowsStack.Add(grid);
        }
    }

    private void RenderInventoryValue(AccountingSummaryView summary)
    {
        InvTotalValueLabel.Text = summary.StockValueDisplay;
        InvUnpricedCountLabel.Text = FormattableString.Invariant($"{summary.UnpricedProductsCount:N0}");

        InvGroupRowsStack.Children.Clear();
        var byGroup = HubAccountingDataSource.SummarizeStockByGroup(_products);
        if (byGroup.Count == 0)
        {
            InvGroupRowsStack.Add(BuildEmptyTableMessage("کالای دارای قیمت خرید و موجودی تأییدشده یافت نشد."));
            return;
        }

        foreach (var group in byGroup)
        {
            InvGroupRowsStack.Add(new BoxView { HeightRequest = 1, Color = StoreSidebarHelper.DividerCream });
            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(2, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(120)),
                    new ColumnDefinition(new GridLength(160)),
                },
                Padding = new Thickness(16, 12)
            };
            row.Add(new Label { Text = group.GroupName, FontFamily = "Vazirmatn-Bold", FontSize = 14, TextColor = StoreSidebarHelper.Ink }, 0, 0);
            row.Add(new Label { Text = FormattableString.Invariant($"{group.ProductCount:N0}"), FontFamily = "Vazirmatn", FontSize = 14, TextColor = StoreSidebarHelper.Ink }, 1, 0);
            row.Add(new Label { Text = group.ValueDisplay, FontFamily = "Vazirmatn-Bold", FontSize = 14, TextColor = StoreSidebarHelper.Green }, 2, 0);
            InvGroupRowsStack.Add(row);
        }
    }

    private static View BuildEmptyTableMessage(string message) => new Label
    {
        Text = message,
        FontFamily = "Vazirmatn",
        FontSize = 14,
        TextColor = StoreSidebarHelper.MutedInk,
        Padding = new Thickness(16, 18),
        HorizontalTextAlignment = TextAlignment.Center
    };

    private static bool HasValidSettings(HubConnectionSettings? settings) =>
        settings is not null &&
        !string.IsNullOrWhiteSpace(settings.BaseUrl) &&
        settings.StoreId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(settings.SessionToken);

    private static bool IsExpected(Exception exception) => exception is ArgumentException or
        FormatException or HttpRequestException or JsonException or InvalidOperationException or
        TaskCanceledException or CryptographicException;
}
