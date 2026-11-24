using System.Text;
using System.Text.Json;

namespace Kandoo.App;

public partial class CustomerInvoicesPage : ContentPage
{
    private readonly HttpClient _httpClient = new();
    private string? _accessToken;
    private string? _lastError;

    public CustomerInvoicesPage()
    {
        InitializeComponent();
        LoadAccessToken();
    }

    private void LoadAccessToken()
    {
        try
        {
            var tokenFile = new SharedTokenFile();
            var record = tokenFile.Load();
            _accessToken = record?.Token;
        }
        catch
        {
            _accessToken = null;
        }
    }

    private async void OnLoadClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_accessToken))
        {
            StatusLabel.Text = "ورود مشتری لازم است. ابتدا وارد سیستم شوید.";
            return;
        }

        LoadButton.IsEnabled = false;
        StatusLabel.Text = "در حال بارگذاری فاکتور‌ها…";
        _lastError = null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.kandoo.cloud/v1/customer/invoices");
            request.Headers.Add("Authorization", $"Bearer {_accessToken}");

            using var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                StatusLabel.Text = $"خطا: {(int)response.StatusCode}";
                return;
            }

            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            var itemsProp = root.TryGetProperty("items", out var items) ? items : default;

            InvoicesList.Clear();
            if (itemsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in itemsProp.EnumerateArray())
                {
                    var storeName = item.TryGetProperty("store_name", out var sn) ? sn.GetString() : "نامشخص";
                    var displayNumber = item.TryGetProperty("display_number", out var dn) ? dn.GetInt64() : 0;
                    var status = item.TryGetProperty("lifecycle_status", out var ls) ? ls.GetString() : "نامشخص";

                    var border = new Border { Style = Resources["CardStyle"] as Style };
                    var layout = new VerticalStackLayout { Style = Resources["RowStackStyle"] as Style };
                    layout.Add(new Label { Text = storeName, Style = Resources["SectionTitleStyle"] as Style });
                    layout.Add(new Label { Text = $"شماره نمایشی: {displayNumber} — وضعیت: {status}", Style = Resources["BodyStyle"] as Style });
                    border.Content = layout;
                    InvoicesList.Add(border);
                }
            }

            EmptyState.IsVisible = InvoicesList.Count == 0;
            StatusLabel.Text = "فاکتورهای بارگذاری شد.";
        }
        catch (Exception exception)
        {
            StatusLabel.Text = $"خطا در ارتباط: {exception.Message}";
        }
        finally
        {
            LoadButton.IsEnabled = true;
        }
    }
}
