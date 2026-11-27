using System.Security.Cryptography;
using System.Text.Json;

namespace Kandoo.App;

public partial class ProductsPage : ContentPage
{
    private readonly HttpClient _httpClient = new();
    private HubConnectionSettings? _settings;
    private IReadOnlyList<ProductRow> _allRows = Array.Empty<ProductRow>();
    private ProductListFilter _activeFilter = ProductListFilter.All;
    private string? _lastError;

    public ProductsPage(HubConnectionSettings? settings = null)
    {
        _settings = HubConnectionResolver.Resolve(settings) ?? settings;
        InitializeComponent();
        StoreSidebarHelper.AttachSidebar(
            this,
            SidebarHost,
            StoreSidebarSection.Products,
            () => _settings);
        StoreSidebarHelper.ApplyRoundedCorners(SearchBorder, 10);
        StoreSidebarHelper.ApplyRoundedCorners(TableCard, 16);
        StoreSidebarHelper.ApplyRoundedCorners(EmptyState, 16);

        UpdateFilterChips();
        StatusLabel.Text = HasValidSettings(_settings)
            ? "آماده بارگذاری کالاها."
            : "تنظیمات اتصال به هاب یافت نشد؛ ابتدا راه‌اندازی یا جفت‌سازی را انجام دهید.";
        UpdateEmptyState(isEmpty: true, errorMessage: null);
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

    private async void OnLoadClicked(object? sender, EventArgs e) => await LoadFromHubAsync();

    private async void OnSearchCompleted(object? sender, EventArgs e) => await LoadFromHubAsync();

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e) => RenderFilteredRows();

    private void OnFilterAllClicked(object? sender, EventArgs e) => SetFilter(ProductListFilter.All);

    private void OnFilterIncompletePriceClicked(object? sender, EventArgs e) =>
        SetFilter(ProductListFilter.IncompletePrice);

    private void OnFilterUnconfirmedStockClicked(object? sender, EventArgs e) =>
        SetFilter(ProductListFilter.UnconfirmedStock);

    private void OnFilterLowStockClicked(object? sender, EventArgs e) =>
        SetFilter(ProductListFilter.LowStock);

    private async void OnNewProductClicked(object? sender, EventArgs e)
    {
        _settings = HubConnectionResolver.Resolve(_settings) ?? _settings;
        await Navigation.PushAsync(new ProductEditorPage(_settings));
    }

    private async void OnHolooImportClicked(object? sender, EventArgs e)
    {
        await DisplayAlert(
            "ورود از هلو",
            "اتصال به هلو فقط‌خواندنی است و هیچ تغییری در پایگاه دادهٔ هلو ثبت نمی‌کند. همگام‌سازی زندهٔ هلو خارج از دامنهٔ MVP-1 است.",
            "متوجه شدم");
    }

    private void SetFilter(ProductListFilter filter)
    {
        _activeFilter = filter;
        UpdateFilterChips();
        RenderFilteredRows();
    }

    private void UpdateFilterChips()
    {
        StyleChip(FilterAllChip, _activeFilter == ProductListFilter.All);
        StyleChip(FilterIncompletePriceChip, _activeFilter == ProductListFilter.IncompletePrice);
        StyleChip(FilterUnconfirmedStockChip, _activeFilter == ProductListFilter.UnconfirmedStock);
        StyleChip(FilterLowStockChip, _activeFilter == ProductListFilter.LowStock);
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
            StatusLabel.Text = "تنظیمات اتصال به هاب و نشست دستگاه لازم است.";
            UpdateEmptyState(
                isEmpty: true,
                errorMessage: "تنظیمات هاب یافت نشد؛ ابتدا راه‌اندازی فروشگاه یا جفت‌سازی را انجام دهید.");
            return;
        }

        LoadButton.IsEnabled = false;
        _lastError = null;
        try
        {
            var source = new HubProductDataSource(_httpClient, _settings!);
            _allRows = await source.LoadProductsAsync(groupId: null, query: null, limit: 200);
            RenderFilteredRows();
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _lastError = exception.Message;
            _allRows = Array.Empty<ProductRow>();
            ProductItems.Children.Clear();
            StatusLabel.Text = "خطا در دریافت فهرست کالاها.";
            UpdateEmptyState(isEmpty: true, errorMessage: exception.Message);
        }
        finally
        {
            LoadButton.IsEnabled = true;
        }
    }

    private void RenderFilteredRows()
    {
        var visible = ProductCatalogFilter.Apply(_allRows, _activeFilter, SearchEntry.Text);
        ProductItems.Children.Clear();

        foreach (var row in visible)
        {
            ProductItems.Add(BuildProductRowView(row));
        }

        StatusLabel.Text = _allRows.Count == visible.Count
            ? FormattableString.Invariant($"{visible.Count} کالا نمایش داده شد.")
            : FormattableString.Invariant($"{visible.Count} از {_allRows.Count} کالا نمایش داده شد.");

        UpdateEmptyState(isEmpty: visible.Count == 0, errorMessage: null);
    }

    private View BuildProductRowView(ProductRow row)
    {
        var container = new VerticalStackLayout { Spacing = 0 };
        container.Add(new BoxView
        {
            HeightRequest = 1,
            Color = StoreSidebarHelper.DividerCream
        });

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(90)),
                new ColumnDefinition(new GridLength(2, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(140)),
                new ColumnDefinition(new GridLength(100)),
                new ColumnDefinition(new GridLength(140)),
                new ColumnDefinition(new GridLength(90)),
            },
            Padding = new Thickness(16, 12),
            ColumnSpacing = 8
        };

        grid.Add(new Label
        {
            Text = row.CodeDisplay,
            FontFamily = "Vazirmatn-Bold",
            FontSize = 15,
            TextColor = StoreSidebarHelper.Ink,
            VerticalOptions = LayoutOptions.Center
        }, 0, 0);

        var nameStack = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center
        };
        nameStack.Add(new Label
        {
            Text = row.Name,
            FontFamily = "Vazirmatn-Bold",
            FontSize = 15,
            TextColor = StoreSidebarHelper.Ink
        });
        if (row.HasVariantSubtitle)
        {
            nameStack.Add(new Label
            {
                Text = row.VariantSubtitle,
                FontFamily = "Vazirmatn",
                FontSize = 12,
                TextColor = StoreSidebarHelper.MutedInk
            });
        }

        grid.Add(nameStack, 1, 0);

        grid.Add(new Label
        {
            Text = row.TablePriceDisplay,
            FontFamily = row.IsIncompletePrice ? "Vazirmatn" : "Vazirmatn-Bold",
            FontSize = 15,
            TextColor = row.IsIncompletePrice ? StoreSidebarHelper.MutedInk : StoreSidebarHelper.Green,
            VerticalOptions = LayoutOptions.Center
        }, 2, 0);

        grid.Add(new Label
        {
            Text = row.StockDisplay,
            FontFamily = "Vazirmatn",
            FontSize = 15,
            TextColor = StoreSidebarHelper.Ink,
            VerticalOptions = LayoutOptions.Center
        }, 3, 0);

        var badge = new Border
        {
            BackgroundColor = Color.FromArgb(row.StatusBadgeHex),
            StrokeThickness = 0,
            Padding = new Thickness(10, 4),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = row.StatusBadgeText,
                FontFamily = "Vazirmatn-Bold",
                FontSize = 12.5,
                TextColor = Colors.White
            }
        };
        StoreSidebarHelper.ApplyRoundedCorners(badge, 6);
        grid.Add(badge, 4, 0);

        var editButton = new Button
        {
            Text = "ویرایش",
            FontFamily = "Vazirmatn-Bold",
            FontSize = 13,
            BackgroundColor = StoreSidebarHelper.Wax,
            TextColor = StoreSidebarHelper.Ink,
            CornerRadius = 8,
            MinimumHeightRequest = 38,
            Padding = new Thickness(10, 4),
            VerticalOptions = LayoutOptions.Center
        };
        SemanticProperties.SetDescription(editButton, $"ویرایش کالای {row.Name}");
        editButton.Clicked += async (_, _) =>
        {
            await Navigation.PushAsync(new ProductEditorPage(_settings, row));
        };
        grid.Add(editButton, 5, 0);

        container.Add(grid);
        return container;
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
        EmptyStateTitleLabel.Text = hasError ? "ارتباط با هاب برقرار نشد" : "هیچ کالایی یافت نشد";
        EmptyStateTitleLabel.TextColor = hasError ? StoreSidebarHelper.Review : StoreSidebarHelper.Ink;
        EmptyStateSubtitleLabel.Text = hasError
            ? "بارگذاری کالاها با مشکل مواجه شد. می‌توانید گزارش خطا را کپی کنید یا دوباره تلاش کنید."
            : "کالایی مطابق جست‌وجو یا فیلتر انتخابی وجود ندارد. با دکمهٔ «کالای جدید» اولین کالا را ثبت کنید.";
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
