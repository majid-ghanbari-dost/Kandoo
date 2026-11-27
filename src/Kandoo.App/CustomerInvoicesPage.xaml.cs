using Microsoft.Maui.Controls.Shapes;

namespace Kandoo.App;

/// <summary>
/// Customer «فاکتورهای من» (C-97: Customer-Invoices-v2 + Customer-Empty +
/// Customer-New-Banner). Invoices grouped per store with badges, search and the
/// new-invoice banner; bottom nav with the role switch («نقش‌ها»). Talks to the
/// PHP Cloud customer API through CustomerApiClient (P-31 accepted contract).
/// </summary>
public partial class CustomerInvoicesPage : ContentPage
{
    private readonly CustomerSessionStore _sessions = new();
    private readonly CustomerSeenInvoicesStore _seen = new();
    private readonly CustomerLocalConfig _config = CustomerLocalConfig.Load();
    private CustomerSessionRecord? _session;
    private CustomerInvoicesResponse? _response;
    private bool _loginGateShown;

    public CustomerInvoicesPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _session = _sessions.Load();
        if (_session is not { IsLoggedIn: true })
        {
            if (!_loginGateShown)
            {
                _loginGateShown = true;
                await Navigation.PushModalAsync(new CustomerOtpPage());
            }

            return;
        }

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_session is not { IsLoggedIn: true })
        {
            return;
        }

        StatusLabel.Text = "در حال بارگذاری فاکتورها…";
        try
        {
            using var client = NewClient();
            var result = await client.ListInvoicesAsync(_session!.SessionToken);
            if (result.HttpStatus == 401 || result.Status == CustomerApiStatus.Unauthenticated)
            {
                // Session revoked/expired — back to login with the remembered device.
                _sessions.ClearSession();
                _session = null;
                StatusLabel.Text = "نشست منقضی شده است؛ دوباره وارد شوید.";
                if (!_loginGateShown)
                {
                    _loginGateShown = true;
                    await Navigation.PushModalAsync(new CustomerOtpPage());
                }

                return;
            }

            if (!result.IsSuccess || result.Value is null)
            {
                StatusLabel.Text = "در حال حاضر ارتباط با سرور ممکن نیست.";
                return;
            }

            _response = result.Value;
            StatusLabel.Text = string.Empty;
            Render();
        }
        catch (Exception)
        {
            StatusLabel.Text = "در حال حاضر ارتباط با سرور ممکن نیست.";
        }
    }

    private void Render()
    {
        var state = CustomerInvoicesViewBuilder.Build(_response, SearchEntry.Text, _seen.Load());

        // Customer-New-Banner
        if (state.NewInvoiceBanner is not null)
        {
            BannerStoreName.Text = $" از {state.NewInvoiceBanner.StoreName}";
            NewInvoiceBanner.BindingContext = state.NewInvoiceBanner;
            NewInvoiceBanner.IsVisible = true;
        }
        else
        {
            NewInvoiceBanner.IsVisible = false;
        }

        // Cards grouped per store
        InvoicesList.Clear();
        foreach (var store in state.Stores)
        {
            var header = new Label { Style = (Style)Resources["StoreHeaderStyle"] };
            header.Text = store.StoreName;
            InvoicesList.Add(header);

            foreach (var invoice in store.Invoices)
            {
                InvoicesList.Add(BuildCard(invoice));
            }
        }

        // Customer-Empty state
        EmptyState.IsVisible = state.IsEmpty;
    }

    private Border BuildCard(CustomerInvoiceCard invoice)
    {
        var card = new Border
        {
            BackgroundColor = Colors.White,
            Padding = new Thickness(16),
            Stroke = Colors.Transparent,
        };
        card.StrokeShape = new RoundRectangle { CornerRadius = 16 };
        var tap = new TapGestureRecognizer();
        var captured = invoice;
        tap.Tapped += async (sender, args) => await OpenInvoiceAsync(captured);
        card.GestureRecognizers.Add(tap);

        var body = new VerticalStackLayout { Spacing = 10 };

        // Row 1: «فاکتور ۱۴۰۲۵» + badge chip
        var top = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        var title = new Label { Style = (Style)Resources["CardTitleStyle"] };
        title.Text = CustomerInvoicesViewBuilder.CardTitle(invoice.DisplayNumber);
        top.Add(title, 0, 0);
        if (invoice.BadgeText is not null)
        {
            var chip = new Border
            {
                Padding = new Thickness(11, 4),
                Stroke = Colors.Transparent,
                BackgroundColor = invoice.Badge switch
                {
                    CustomerBadgeKind.New => (Color)Application.Current!.Resources["Ink"],
                    CustomerBadgeKind.Paid => (Color)Application.Current!.Resources["Green"],
                    _ => (Color)Application.Current!.Resources["Review"],
                },
            };
            chip.StrokeShape = new RoundRectangle { CornerRadius = 999 };
            chip.Content = new Label
            {
                Text = invoice.BadgeText,
                TextColor = Colors.White,
                FontFamily = (string)Application.Current!.Resources["FontFamilyBold"],
                FontSize = 12.5,
            };
            top.Add(chip, 1, 0);
        }

        body.Add(top);

        // Row 2: date
        if (invoice.DateText is not null)
        {
            var date = new Label { Style = (Style)Resources["CardDateStyle"] };
            date.Text = invoice.DateText;
            body.Add(date);
        }

        // Row 3: amount + unit (board: «۱۲۰٬۰۰۰» big + «تومان» caption)
        if (invoice.AmountText is not null)
        {
            var splitAt = invoice.AmountText.LastIndexOf(' ');
            var amountRow = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) } };
            var amount = new Label { Style = (Style)Resources["BigAmountStyle"] };
            amount.Text = splitAt > 0 ? invoice.AmountText[..splitAt] : invoice.AmountText;
            var unit = new Label { Style = (Style)Resources["CardDateStyle"] };
            unit.Text = splitAt > 0 ? invoice.AmountText[(splitAt + 1)..] : string.Empty;
            unit.VerticalOptions = LayoutOptions.End;
            amountRow.Add(amount, 0, 0);
            amountRow.Add(unit, 1, 0);
            amountRow.ColumnSpacing = 8;
            body.Add(amountRow);
        }

        card.Content = body;
        return card;
    }

    private async Task OpenInvoiceAsync(CustomerInvoiceCard invoice)
    {
        _seen.MarkSeen(invoice.InvoiceId);
        await Navigation.PushAsync(new CustomerInvoiceDetailPage(
            invoice.StoreId,
            invoice.StoreName,
            invoice.InvoiceId,
            invoice.DisplayNumber));
        Render();
    }

    private async void OnBannerTapped(object? sender, TappedEventArgs e)
    {
        if (NewInvoiceBanner.BindingContext is CustomerInvoiceCard card)
        {
            await OpenInvoiceAsync(card);
        }
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => Render();

    private async void OnRolesTabTapped(object? sender, TappedEventArgs e)
    {
        // Role switch (C-92): the «نقش‌ها» tab opens role selection; the app then
        // routes to the chosen role's home (RoleStart / RoleSelectionPage.Select).
        var profiles = new PreferencesUserProfileStore();
        var current = profiles.Load();
        await Navigation.PushModalAsync(new RoleSelectionPage(profiles, current));
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
