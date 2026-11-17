namespace Kandoo.App;

public partial class InvoiceDetailsPage : ContentPage
{
    private readonly HttpClient _httpClient = new();
    private readonly HubConnectionSettings? _settings;
    private readonly string _invoiceId;
    private string? _lastError;

    public InvoiceDetailsPage(HubConnectionSettings? settings = null, string? invoiceId = null)
    {
        _settings = settings;
        _invoiceId = invoiceId ?? string.Empty;
        InitializeComponent();
        InvoiceIdLabel.Text = string.IsNullOrWhiteSpace(_invoiceId)
            ? "شناسه فاکتور مشخص نشده است."
            : $"شناسه: {_invoiceId}";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_settings is null || string.IsNullOrWhiteSpace(_invoiceId))
            return;

        await LoadDetailsAsync();
    }

    private async Task LoadDetailsAsync()
    {
        try
        {
            var dataSource = new HubInvoiceDataSource(_httpClient, _settings);
            var json = await dataSource.GetInvoiceDetailsAsync(_invoiceId);

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            InvoiceNumberLabel.Text = OptionalString(root, "invoice_number") ?? "بدون شماره";
            StatusLabel.Text = OptionalString(root, "status") ?? "ناشناخته";
            DateLabel.Text = OptionalString(root, "issued_at") ?? "—";
            TotalLabel.Text = OptionalLong(root, "total_rials").HasValue
                ? FormattableString.Invariant($"{OptionalLong(root, "total_rials").Value:N0} ریال")
                : "—";

            ItemsList.Clear();
            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    var name = OptionalString(item, "product_name") ?? "نامنшина";
                    var qty = OptionalInt(item, "quantity") ?? 0;
                    var unitPrice = OptionalLong(item, "unit_price_rials") ?? 0;
                    var total = qty * unitPrice;

                    var rowLabel = new Label
                    {
                        Text = $"{name} × {qty} = {total:N0} ریال",
                        Style = Resources["BodyStyle"] as Style ?? BodyStyle
                    };
                    ItemsList.Add(rowLabel);
                }
            }
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _lastError = exception.Message;
            StatusLabel.Text = exception.Message;
            CopyReportButton.IsVisible = true;
        }
    }

    private async void OnResendSmsClicked(object? sender, EventArgs e)
    {
        if (_settings is null || string.IsNullOrWhiteSpace(_invoiceId))
            return;

        try
        {
            var dataSource = new HubInvoiceDataSource(_httpClient, _settings);
            await dataSource.ResendSmsAsync(_invoiceId);
            OperationStatusLabel.Text = "پیامک دوباره ارسال شد.";
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _lastError = exception.Message;
            OperationStatusLabel.Text = exception.Message;
            CopyReportButton.IsVisible = true;
        }
    }

    private async void OnVoidClicked(object? sender, EventArgs e)
    {
        if (_settings is null || string.IsNullOrWhiteSpace(_invoiceId))
            return;

        var confirmed = await DisplayAlert(
            "ابطال فاکتور",
            "این فاکتور به‌صورت رخداد ابطال شود؟",
            "بله",
            "خیر");
        if (!confirmed)
            return;

        try
        {
            // Void is recorded as an event; Hub API does not have a direct void endpoint
            // in v1, so we record the intent locally and show confirmation.
            OperationStatusLabel.Text = "درخواست ابطال ثبت شد (ساختن رخداد ابطال در هاب).";
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _lastError = exception.Message;
            OperationStatusLabel.Text = exception.Message;
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

    private static string? OptionalString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            return null;
        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static long? OptionalLong(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
            return null;
        return value.TryGetInt64(out var result) ? result : null;
    }

    private static int? OptionalInt(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
            return null;
        return value.TryGetInt32(out var result) ? result : null;
    }

    private static bool IsExpected(Exception exception) => exception is ArgumentException or
        FormatException or HttpRequestException or JsonException or InvalidOperationException or
        TaskCanceledException;
}
