using System.Text.Json;

namespace Kandoo.App;

public partial class InvoicesListPage : ContentPage
{
    private readonly HttpClient _httpClient = new();
    private readonly HubConnectionSettings? _settings;
    private string? _lastError;

    public InvoicesListPage(HubConnectionSettings? settings = null)
    {
        _settings = settings;
        InitializeComponent();
        LoadButton.IsEnabled = _settings is not null;
        StatusLabel.Text = _settings is null
            ? "تنظیمات هاب فراهم نشده است."
            : "آماده بارگذاری فاکتورها.";
    }

    private async void OnLoadClicked(object? sender, EventArgs e)
    {
        if (_settings is null)
        {
            StatusLabel.Text = "تنظیمات هاب فراهم نشده است.";
            return;
        }

        LoadButton.IsEnabled = false;
        _lastError = null;
        try
        {
            // C-91: Invoices list page — placeholder until backend invoices list API
            // is available in the Hub. The Hub API currently supports draft creation,
            // item add, and issue, but not invoices listing.
            StatusLabel.Text = "فهرست فاکتورها در حال حاضر از سمت هاب پشتیبانی نمی‌شود.";
            InvoicesList.Clear();
            EmptyState.IsVisible = true;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _lastError = exception.Message;
            StatusLabel.Text = exception.Message;
        }
        finally
        {
            LoadButton.IsEnabled = true;
        }
    }

    private static bool IsExpected(Exception exception) => exception is ArgumentException or
        FormatException or HttpRequestException or JsonException or InvalidOperationException or
        TaskCanceledException;
}
