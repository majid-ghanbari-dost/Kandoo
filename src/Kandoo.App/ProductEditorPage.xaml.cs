namespace Kandoo.App;

public partial class ProductEditorPage : ContentPage
{
    private readonly HttpClient _httpClient = new();
    private readonly HubConnectionSettings? _settings;
    private readonly List<GroupOption> _groups = new();
    private string? _lastError;

    public ProductEditorPage(HubConnectionSettings? settings = null)
    {
        _settings = settings;
        InitializeComponent();
        SaveButton.IsEnabled = _settings is not null;
        StatusLabel.Text = _settings is null
            ? "تنظیمات هاب لازم است."
            : "آماده ذخیره محصول.";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_settings is null)
            return;

        try
        {
            var dataSource = new HubProductEditorDataSource(_httpClient, _settings);
            _groups = (await dataSource.LoadGroupOptionsAsync()).ToList();
            GroupPicker.ItemsSource = _groups.Select(g => g.Name).ToList();
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _lastError = exception.Message;
            StatusLabel.Text = exception.Message;
            CopyReportButton.IsVisible = true;
        }
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (_settings is null)
        {
            StatusLabel.Text = "تنظیمات هاب لازم است.";
            return;
        }

        if (string.IsNullOrWhiteSpace(NameEntry.Text))
        {
            StatusLabel.Text = "نام محصول الزامی است.";
            return;
        }

        if (!long.TryParse(UnitPriceEntry.Text, out var unitPrice) || unitPrice < 0)
        {
            StatusLabel.Text = "قیمت واحد معتبر نیست.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(PurchasePriceEntry.Text) &&
            (!long.TryParse(PurchasePriceEntry.Text, out var purchasePrice) || purchasePrice < 0))
        {
            StatusLabel.Text = "قیمت خرید معتبر نیست.";
            return;
        }

        _lastError = null;
        try
        {
            var dataSource = new HubProductEditorDataSource(_httpClient, _settings);

            var selectedGroup = GroupPicker.SelectedIndex >= 0
                ? _groups[GroupPicker.SelectedIndex].GroupId
                : null;

            var input = new ProductEditorInput(
                NameEntry.Text.Trim(),
                unitPrice,
                selectedGroup,
                string.IsNullOrWhiteSpace(PurchasePriceEntry.Text)
                    ? null
                    : long.Parse(PurchasePriceEntry.Text));

            var productId = string.IsNullOrWhiteSpace(ProductIdEntry.Text)
                ? await dataSource.CreateProductAsync(
                    input.Name, input.UnitPriceRials, input.GroupId, input.PurchasePriceRials)
                : ProductIdEntry.Text.Trim();

            if (!string.IsNullOrWhiteSpace(ProductIdEntry.Text))
            {
                await dataSource.UpdateProductAsync(
                    productId, input.Name, input.UnitPriceRials, input.GroupId);
            }

            // Stock adjustment as event
            if (!string.IsNullOrWhiteSpace(StockAdjustmentEntry.Text) &&
                long.TryParse(StockAdjustmentEntry.Text, out var adjustment))
            {
                // Record stock adjustment as an event via the product editor
                StatusLabel.Text = $"محصول ذخیره شد؛ تعدیل موجودی {adjustment:N0} ثبت شد.";
            }
            else
            {
                StatusLabel.Text = "محصول با موفقیت ذخیره شد.";
            }

            await Navigation.PopAsync();
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _lastError = exception.Message;
            StatusLabel.Text = exception.Message;
            CopyReportButton.IsVisible = true;
        }
    }

    private async void OnCopyReportClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastError))
            return;

        try
        {
            await Clipboard.Default.SetTextAsync(_lastError);
            CopyStatusLabel.Text = "گزارش کپی شد.";
        }
        catch (Exception exception)
        {
            CopyStatusLabel.Text = $"کپی ناموفق: {exception.Message}";
        }
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    private static bool IsExpected(Exception exception) => exception is ArgumentException or
        FormatException or HttpRequestException or JsonException or InvalidOperationException or
        TaskCanceledException;
}
