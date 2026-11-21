using System.Security.Cryptography;
using System.Text.Json;

namespace Kandoo.App;

public partial class ProductsPage : ContentPage
{
    private readonly HttpClient _httpClient = new();
    private HubConnectionSettings? _settings;
    private readonly SharedTokenFile _tokenFile = new();
    private string? _lastError;

    public ProductsPage(HubConnectionSettings? settings = null)
    {
        _settings = settings;
        InitializeComponent();
        LoadButton.IsEnabled = _settings is not null;
        StatusLabel.Text = _settings is null
            ? "تنظیمات Hub ارائه نشده است؛ بارگذاری غیرفعال است."
            : "آماده بارگذاری کالا.";
        UpdateEmptyState(false, null);
    }

    private async void OnLoadClicked(object? sender, EventArgs e)
    {
        if (_settings is null)
        {
            // Try to load settings from shared token file.
            var record = _tokenFile.Load();
            if (record is not null)
            {
                _settings = new HubConnectionSettings(
                    string.Empty,
                    record.StoreId,
                    record.Token);
            }
            else
            {
                StatusLabel.Text = "تنظیمات Hub و نشست دستگاه لازم است.";
                UpdateEmptyState(true, "تنظیمات Hub ارائه نشده است؛ لطفاً ابتدا در صفحه اصلی جفت‌سازی کنید.");
                return;
            }
        }

        LoadButton.IsEnabled = false;
        _lastError = null;
        try
        {
            var source = new HubProductDataSource(_httpClient, _settings);
            var rows = await source.LoadProductsAsync(GroupIdEntry.Text);
            BindableLayout.SetItemsSource(ProductItems, rows);
            StatusLabel.Text = FormattableString.Invariant($"{rows.Count} کالا بارگذاری شد.");
            UpdateEmptyState(rows.Count == 0, null);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _lastError = exception.Message;
            BindableLayout.SetItemsSource(ProductItems, Array.Empty<ProductRow>());
            StatusLabel.Text = exception.Message;
            UpdateEmptyState(true, exception.Message);
        }
        finally
        {
            LoadButton.IsEnabled = true;
        }
    }

    private async void OnCopyReportClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastError))
            return;

        try
        {
            await Clipboard.Default.SetTextAsync(_lastError);
            CopyReportButton.Text = "کپی شد";
        }
        catch (Exception exception)
        {
            CopyReportButton.Text = $"کپی ناموفق: {exception.Message}";
        }
    }

    private void UpdateEmptyState(bool isEmpty, string? errorMessage)
    {
        EmptyState.IsVisible = isEmpty;
        ProductItems.IsVisible = !isEmpty;
        CopyReportButton.IsVisible = !string.IsNullOrWhiteSpace(errorMessage);
        EmptyStateErrorLabel.Text = errorMessage ?? string.Empty;
    }

    private static bool IsExpected(Exception exception) => exception is ArgumentException or
        FormatException or HttpRequestException or JsonException or InvalidOperationException or
        TaskCanceledException or CryptographicException;
}
