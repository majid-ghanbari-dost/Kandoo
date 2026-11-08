namespace Kandoo.App;

public partial class ProductsPage : ContentPage
{
    private readonly HttpClient _httpClient = new();
    private readonly HubConnectionSettings? _settings;

    public ProductsPage(HubConnectionSettings? settings = null)
    {
        _settings = settings;
        InitializeComponent();
        LoadButton.IsEnabled = _settings is not null;
        StatusLabel.Text = _settings is null
            ? "تنظیمات Hub ارائه نشده است؛ بارگذاری غیرفعال است."
            : "آماده بارگذاری کالا.";
    }

    private async void OnLoadClicked(object? sender, EventArgs e)
    {
        if (_settings is null)
        {
            return;
        }

        LoadButton.IsEnabled = false;
        try
        {
            var source = new HubProductDataSource(_httpClient, _settings);
            var rows = await source.LoadProductsAsync(GroupIdEntry.Text);
            BindableLayout.SetItemsSource(ProductItems, rows);
            StatusLabel.Text = FormattableString.Invariant($"{rows.Count} کالا بارگذاری شد.");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            BindableLayout.SetItemsSource(ProductItems, Array.Empty<ProductRow>());
            StatusLabel.Text = exception.Message;
        }
        finally
        {
            LoadButton.IsEnabled = true;
        }
    }

    private static bool IsExpected(Exception exception) => exception is ArgumentException or
        FormatException or HttpRequestException or JsonException or InvalidOperationException or
        TaskCanceledException or CryptographicException;
}
