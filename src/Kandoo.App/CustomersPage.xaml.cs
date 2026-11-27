using System.Security.Cryptography;
using System.Text.Json;

namespace Kandoo.App;

public partial class CustomersPage : ContentPage
{
    private readonly HttpClient _httpClient = new();
    private HubConnectionSettings? _settings;
    private IReadOnlyList<CustomerRow> _allCustomers = Array.Empty<CustomerRow>();
    private CustomerRow? _selectedCustomer;
    private bool _showingSyntheticPreview;
    private string? _lastError;

    public CustomersPage(HubConnectionSettings? settings = null)
    {
        _settings = HubConnectionResolver.Resolve(settings) ?? settings;
        InitializeComponent();
        StoreSidebarHelper.AttachSidebar(
            this,
            SidebarHost,
            StoreSidebarSection.Customers,
            () => _settings);
        StoreSidebarHelper.ApplyRoundedCorners(SearchBorder, 10);
        StoreSidebarHelper.ApplyRoundedCorners(TableCard, 16);
        StoreSidebarHelper.ApplyRoundedCorners(EmptyState, 16);
        StoreSidebarHelper.ApplyRoundedCorners(DetailCard, 16);

        SizeChanged += OnPageSizeChanged;
        StatusLabel.Text = HasValidSettings(_settings)
            ? "آماده بارگذاری مشتری‌ها."
            : "تنظیمات اتصال به هاب یافت نشد؛ ابتدا راه‌اندازی یا جفت‌سازی را انجام دهید.";
        RenderDetailCard(null);
        UpdateEmptyState(isEmpty: true, errorMessage: null);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _settings = HubConnectionResolver.Resolve(_settings) ?? _settings;
        if (HasValidSettings(_settings) && !_showingSyntheticPreview)
        {
            await LoadFromHubAsync();
        }
    }

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        if (Width <= 0)
            return;

        if (Width < 860)
        {
            MasterDetailGrid.ColumnDefinitions.Clear();
            MasterDetailGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            Grid.SetColumn(MasterColumn, 0);
            Grid.SetRow(MasterColumn, 0);
            Grid.SetColumn(DetailCard, 0);
            Grid.SetRow(DetailCard, 1);
        }
        else
        {
            MasterDetailGrid.ColumnDefinitions.Clear();
            MasterDetailGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
            MasterDetailGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            Grid.SetColumn(MasterColumn, 0);
            Grid.SetRow(MasterColumn, 0);
            Grid.SetColumn(DetailCard, 1);
            Grid.SetRow(DetailCard, 0);
        }
    }

    private async void OnLoadClicked(object? sender, EventArgs e)
    {
        _showingSyntheticPreview = false;
        SyntheticBanner.IsVisible = false;
        ToggleSyntheticButton.Text = "پیش‌نمایش ساختگی";
        await LoadFromHubAsync();
    }

    private void OnToggleSyntheticClicked(object? sender, EventArgs e)
    {
        _showingSyntheticPreview = !_showingSyntheticPreview;
        if (_showingSyntheticPreview)
        {
            _allCustomers = HubCustomersDataSource.CreateSyntheticPreviewRows();
            SyntheticBanner.IsVisible = true;
            ToggleSyntheticButton.Text = "بستن پیش‌نمایش ساختگی";
            StatusLabel.Text = "پیش‌نمایش چیدمان با داده ساختگی (غیرواقعی).";
            RenderCustomerList();
        }
        else
        {
            _allCustomers = Array.Empty<CustomerRow>();
            SyntheticBanner.IsVisible = false;
            ToggleSyntheticButton.Text = "پیش‌نمایش ساختگی";
            StatusLabel.Text = "پیش‌نمایش ساختگی بسته شد.";
            RenderCustomerList();
        }
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e) => RenderCustomerList();

    private async Task LoadFromHubAsync()
    {
        _settings = HubConnectionResolver.Resolve(_settings) ?? _settings;
        if (!HasValidSettings(_settings))
        {
            ToggleSyntheticButton.IsVisible = true;
            StatusLabel.Text = "تنظیمات اتصال به هاب و نشست دستگاه لازم است.";
            UpdateEmptyState(isEmpty: true, errorMessage: null);
            return;
        }

        LoadButton.IsEnabled = false;
        _lastError = null;
        try
        {
            var source = new HubCustomersDataSource(_httpClient, _settings!);
            var directory = await source.LoadDirectoryAsync();
            _allCustomers = directory.Customers;
            ToggleSyntheticButton.IsVisible = directory.Origin == CustomerDataOrigin.HubEndpointUnavailable;
            StatusLabel.Text = directory.StatusNote ??
                FormattableString.Invariant($"{_allCustomers.Count} مشتری دریافت شد.");
            RenderCustomerList();
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _lastError = exception.Message;
            _allCustomers = Array.Empty<CustomerRow>();
            CustomerItems.Children.Clear();
            RenderDetailCard(null);
            StatusLabel.Text = "خطا در دریافت مشتری‌ها.";
            UpdateEmptyState(isEmpty: true, errorMessage: exception.Message);
        }
        finally
        {
            LoadButton.IsEnabled = true;
        }
    }

    private void RenderCustomerList()
    {
        var visible = CustomerDirectoryFilter.Apply(_allCustomers, SearchEntry.Text);
        CustomerItems.Children.Clear();

        if (_selectedCustomer is null || !visible.Any(c => c.CustomerId == _selectedCustomer.CustomerId))
        {
            _selectedCustomer = visible.FirstOrDefault();
        }

        foreach (var row in visible)
        {
            var isSelected = _selectedCustomer is not null && row.CustomerId == _selectedCustomer.CustomerId;
            CustomerItems.Add(BuildCustomerRowView(row, isSelected));
        }

        RenderDetailCard(_selectedCustomer);
        UpdateEmptyState(isEmpty: visible.Count == 0, errorMessage: null);
    }

    private View BuildCustomerRowView(CustomerRow row, bool isSelected)
    {
        var wrapper = new VerticalStackLayout { Spacing = 0 };
        wrapper.Add(new BoxView
        {
            HeightRequest = 1,
            Color = StoreSidebarHelper.DividerCream
        });

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(2, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(130)),
                new ColumnDefinition(new GridLength(130)),
                new ColumnDefinition(new GridLength(120)),
            },
            Padding = new Thickness(16, 12),
            ColumnSpacing = 8,
            BackgroundColor = isSelected ? Color.FromArgb("#FFF4D6") : Colors.White
        };

        grid.Add(new Label
        {
            Text = row.Name,
            FontFamily = "Vazirmatn-Bold",
            FontSize = 15,
            TextColor = StoreSidebarHelper.Ink,
            VerticalOptions = LayoutOptions.Center
        }, 0, 0);

        grid.Add(new Label
        {
            Text = row.Phone,
            FontFamily = "Vazirmatn",
            FontSize = 14,
            TextColor = StoreSidebarHelper.MutedInk,
            VerticalOptions = LayoutOptions.Center
        }, 1, 0);

        grid.Add(new Label
        {
            Text = row.TotalDisplay,
            FontFamily = "Vazirmatn-Bold",
            FontSize = 14,
            TextColor = StoreSidebarHelper.Green,
            VerticalOptions = LayoutOptions.Center
        }, 2, 0);

        grid.Add(new Label
        {
            Text = row.OutstandingDisplay,
            FontFamily = row.OutstandingRials > 0 ? "Vazirmatn-Bold" : "Vazirmatn",
            FontSize = 14,
            TextColor = row.OutstandingRials > 0 ? StoreSidebarHelper.Review : StoreSidebarHelper.Ink,
            VerticalOptions = LayoutOptions.Center
        }, 3, 0);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            _selectedCustomer = row;
            RenderCustomerList();
        };
        grid.GestureRecognizers.Add(tap);

        wrapper.Add(grid);
        return wrapper;
    }

    private void RenderDetailCard(CustomerRow? customer)
    {
        RecentInvoicesStack.Children.Clear();
        if (customer is null)
        {
            DetailCustomerNameLabel.Text = "مشتری انتخاب نشده";
            DetailPhoneLabel.Text = "—";
            DetailTotalLabel.Text = "۰ ریال";
            DetailOutstandingLabel.Text = "۰ ریال";
            DetailOutstandingLabel.TextColor = StoreSidebarHelper.Ink;
            RecentInvoicesStack.Add(new Label
            {
                Text = "برای مشاهدهٔ جزئیات و آخرین فاکتورها، یک مشتری را از فهرست انتخاب کنید.",
                FontFamily = "Vazirmatn",
                FontSize = 13,
                TextColor = StoreSidebarHelper.MutedInk
            });
            return;
        }

        DetailCustomerNameLabel.Text = customer.Name;
        DetailPhoneLabel.Text = customer.Phone;
        DetailTotalLabel.Text = customer.TotalDisplay;
        DetailOutstandingLabel.Text = customer.OutstandingDisplay;
        DetailOutstandingLabel.TextColor = customer.OutstandingRials > 0
            ? StoreSidebarHelper.Review
            : StoreSidebarHelper.Ink;

        if (customer.Invoices.Count == 0)
        {
            RecentInvoicesStack.Add(new Label
            {
                Text = "فاکتور ثبت‌شده‌ای برای نمایش وجود ندارد.",
                FontFamily = "Vazirmatn",
                FontSize = 13,
                TextColor = StoreSidebarHelper.MutedInk
            });
            return;
        }

        foreach (var invoice in customer.Invoices)
        {
            var invRow = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                },
                Padding = new Thickness(0, 6)
            };
            invRow.Add(new Label
            {
                Text = invoice.InvoiceNumber,
                FontFamily = "Vazirmatn",
                FontSize = 14,
                TextColor = StoreSidebarHelper.Ink
            }, 0, 0);
            invRow.Add(new Label
            {
                Text = invoice.AmountDisplay,
                FontFamily = "Vazirmatn-Bold",
                FontSize = 14,
                TextColor = StoreSidebarHelper.Ink
            }, 1, 0);
            RecentInvoicesStack.Add(invRow);
            RecentInvoicesStack.Add(new BoxView
            {
                HeightRequest = 1,
                Color = StoreSidebarHelper.DividerCream
            });
        }
    }

    private async void OnCopyReportClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastError))
            return;

        try
        {
            await Clipboard.Default.SetTextAsync(_lastError);
            CopyReportButton.Text = "گزارش کپی شد";
        }
        catch (Exception exception)
        {
            CopyReportButton.Text = $"کپی ناموفق: {exception.Message}";
        }
    }

    private void UpdateEmptyState(bool isEmpty, string? errorMessage)
    {
        EmptyState.IsVisible = isEmpty;
        TableCard.IsVisible = !isEmpty;
        var hasError = !string.IsNullOrWhiteSpace(errorMessage);
        CopyReportButton.IsVisible = hasError;
        CopyReportButton.Text = "کپی گزارش خطا";
        EmptyTitleLabel.Text = hasError ? "خطا در ارتباط با هاب" : "هیچ مشتری ثبت نشده است";
        EmptyTitleLabel.TextColor = hasError ? StoreSidebarHelper.Review : StoreSidebarHelper.Ink;
        EmptySubtitleLabel.Text = hasError
            ? "دریافت فهرست مشتری‌ها با خطا مواجه شد."
            : "با صدور فاکتور با شماره موبایل، مشتری به فهرست اضافه می‌شود.";
        EmptyStateErrorLabel.Text = errorMessage ?? string.Empty;
        EmptyStateErrorLabel.IsVisible = hasError;
    }

    private static bool HasValidSettings(HubConnectionSettings? settings) =>
        settings is not null &&
        !string.IsNullOrWhiteSpace(settings.BaseUrl) &&
        settings.StoreId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(settings.SessionToken);

    private static bool IsExpected(Exception exception) => exception is ArgumentException or
        FormatException or HttpRequestException or JsonException or InvalidOperationException or
        TaskCanceledException or CryptographicException;
}
