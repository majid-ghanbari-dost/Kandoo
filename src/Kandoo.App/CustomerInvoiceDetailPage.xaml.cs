namespace Kandoo.App;

/// <summary>
/// Customer invoice detail (C-97: Customer-Invoice-Detail board). Recorded values
/// only; unrecorded fields become explicit data gaps (same honesty policy as the
/// PHP customer web page — remaining_balance as recorded, never computed).
/// </summary>
public partial class CustomerInvoiceDetailPage : ContentPage
{
    private readonly CustomerSessionStore _sessions = new();
    private readonly CustomerLocalConfig _config = CustomerLocalConfig.Load();
    private readonly string _storeId;
    private readonly string _invoiceId;
    private CustomerInvoiceDetail? _detail;

    public CustomerInvoiceDetailPage(string storeId, string storeName, string invoiceId, long displayNumber)
    {
        _storeId = storeId;
        _invoiceId = invoiceId;
        InitializeComponent();
        HeaderTitle.Text = CustomerInvoicesViewBuilder.CardTitle(displayNumber);
        StoreNameLabel.Text = storeName;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var session = _sessions.Load();
        if (session is not { IsLoggedIn: true })
        {
            StatusLabel.Text = "ورود مشتری لازم است.";
            return;
        }

        StatusLabel.Text = "در حال بارگذاری فاکتور…";
        try
        {
            using var client = NewClient();
            var result = await client.GetInvoiceAsync(session.SessionToken, _storeId, _invoiceId);
            if (!result.IsSuccess || result.Value is null)
            {
                StatusLabel.Text = result.Status == CustomerApiStatus.NotFound
                    ? "فاکتور یافت نشد."
                    : "در حال حاضر ارتباط با سرور ممکن نیست.";
                return;
            }

            _detail = result.Value.Invoice;
            StatusLabel.Text = string.Empty;
            Render(CustomerInvoiceDetailViewBuilder.Build(_detail));
        }
        catch (Exception)
        {
            StatusLabel.Text = "در حال حاضر ارتباط با سرور ممکن نیست.";
        }
    }

    private void Render(CustomerInvoiceDetailViewState state)
    {
        StoreNameLabel.Text = state.StoreName;
        HeaderTitle.Text = state.HeaderTitle;
        DateValue.Text = state.DateText;

        if (state.BuyerText is not null)
        {
            BuyerRow.IsVisible = true;
            BuyerValue.Text = state.BuyerText;
        }
        else
        {
            BuyerRow.IsVisible = false;
        }

        ItemsList.Clear();
        foreach (var item in state.Items)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 12 };
            var title = new Label { Style = (Style)Resources["RowLabelStyle"] };
            title.Text = item.Title;
            var amount = new Label { Style = (Style)Resources["RowValueStyle"] };
            amount.Text = item.AmountText;
            row.Add(title, 0, 0);
            row.Add(amount, 1, 0);
            ItemsList.Add(row);
        }

        if (state.Items.Count == 0)
        {
            ItemsList.Add(new Label { Style = (Style)Resources["RowLabelStyle"], Text = "—" });
        }

        PayableValue.Text = state.PayableText;

        PaymentsList.Clear();
        foreach (var payment in state.Payments)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 12 };
            var method = new Label { Style = (Style)Resources["RowLabelStyle"] };
            method.Text = payment.MethodText;
            var amount = new Label { Style = (Style)Resources["RowValueStyle"] };
            amount.Text = payment.AmountText;
            row.Add(method, 0, 0);
            row.Add(amount, 1, 0);
            PaymentsList.Add(row);
        }

        // مانده باقی‌مانده (ثبت‌شده) — only when recorded, never computed.
        if (state.BalanceText is not null)
        {
            BalanceRow.IsVisible = true;
            BalanceValue.Text = state.BalanceText;
        }
        else
        {
            BalanceRow.IsVisible = false;
        }

        if (state.HasDataGaps)
        {
            GapsCard.IsVisible = true;
            GapsTitle.Text = CustomerInvoiceDetailViewBuilder.GapSectionTitle;
            GapsList.Clear();
            foreach (var gap in state.DataGaps)
            {
                var line = new Label { Style = (Style)Resources["RowLabelStyle"] };
                line.Text = gap;
                GapsList.Add(line);
            }

            var closing = new Label { Style = (Style)Resources["RowLabelStyle"] };
            closing.Text = CustomerInvoiceDetailViewBuilder.GapClosing;
            GapsList.Add(closing);
        }
        else
        {
            GapsCard.IsVisible = false;
        }
    }

    private async void OnBackClicked(object? sender, EventArgs e) => await Navigation.PopAsync();

    private async void OnInvoicesTabTapped(object? sender, TappedEventArgs e) => await Navigation.PopAsync();

    private async void OnRolesTabTapped(object? sender, TappedEventArgs e)
    {
        var profiles = new PreferencesUserProfileStore();
        var current = profiles.Load();
        await Navigation.PushModalAsync(new RoleSelectionPage(profiles, current));
    }

    private async void OnCallStoreClicked(object? sender, EventArgs e)
    {
        // The P-31 projection records no store phone — say so instead of inventing one.
        StatusLabel.Text = "شماره‌ی تماس فروشگاه در داده صدور این فاکتور ثبت نشده است.";
        await Task.CompletedTask;
    }

    private async void OnSaveInvoiceClicked(object? sender, EventArgs e)
    {
        if (_detail is null)
        {
            return;
        }

        var state = CustomerInvoiceDetailViewBuilder.Build(_detail);
        var lines = new List<string>
        {
            "فاکتور دیجیتال کندو",
            $"فروشگاه: {state.StoreName}",
            $"شماره‌ی فاکتور: {state.HeaderTitle}",
            $"تاریخ: {state.DateText}",
        };
        if (state.BuyerText is not null)
        {
            lines.Add($"خریدار: {state.BuyerText}");
        }

        lines.Add("اقلام:");
        foreach (var item in state.Items)
        {
            lines.Add($"- {item.Title} — {item.AmountText}");
        }

        lines.Add($"قابل پرداخت: {state.PayableText}");
        foreach (var payment in state.Payments)
        {
            lines.Add($"{payment.MethodText}: {payment.AmountText}");
        }

        if (state.BalanceText is not null)
        {
            lines.Add($"{CustomerInvoiceDetailViewBuilder.RecordedBalanceLabel} {state.BalanceText}");
        }

        if (state.HasDataGaps)
        {
            lines.Add(string.Empty);
            lines.Add(CustomerInvoiceDetailViewBuilder.GapSectionTitle);
            foreach (var gap in state.DataGaps)
            {
                lines.Add($"- {gap}");
            }

            lines.Add(CustomerInvoiceDetailViewBuilder.GapClosing);
        }

        try
        {
            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Title = state.HeaderTitle,
                Text = string.Join(Environment.NewLine, lines),
            });
        }
        catch (Exception)
        {
            StatusLabel.Text = "ذخیره‌ی فاکتور انجام نشد.";
        }
    }

    private CustomerApiClient NewClient()
    {
        var device = _sessions.LoadOrCreateDevice();
        try
        {
            return new CustomerApiClient(_config.ApiBaseUrl, device.DeviceId);
        }
        catch (ArgumentException)
        {
            return new CustomerApiClient(CustomerApiClient.DefaultBaseUrl, device.DeviceId);
        }
    }
}
