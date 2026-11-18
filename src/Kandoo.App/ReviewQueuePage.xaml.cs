using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Kandoo.App;

public partial class ReviewQueuePage : ContentPage
{
    private readonly ReviewQueueCoordinator _coordinator;
    private readonly HttpClient _httpClient = new();
    private readonly HubConnectionSettings? _settings;
    private string? _lastError;
    private ReviewQueueRow? _selectedReview;

    public ReviewQueuePage(ReviewQueueCoordinator coordinator, HubConnectionSettings? settings = null)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _settings = settings;
        InitializeComponent();
        UpdateModePresentation();
        UpdateRealSettingsVisibility();
    }

    private void UpdateModePresentation()
    {
        ModeLabel.Text = _coordinator.IsDemo
            ? "حالت نمایشی؛ بدون نیاز به تنظیمات"
            : "حالت واقعی؛ اتصال مستقیم به Hub";
        LoadButton.IsEnabled = _coordinator.IsDemo || _settings is not null;
        StatusLabel.Text = _coordinator.IsDemo
            ? "حالت نمایشی فعال است؛ برای بارگذاری روی دکمه کلیک کنید."
            : "تنظیمات Hub را وارد و صف را بارگذاری کنید.";
    }

    private void UpdateRealSettingsVisibility()
    {
        RealSettings.IsVisible = !_coordinator.IsDemo;
    }

    private async void OnFakeModeClicked(object? sender, EventArgs e)
    {
        _coordinator.UseFake();
        UpdateModePresentation();
        await LoadQueueAsync();
    }

    private async void OnRealModeClicked(object? sender, EventArgs e)
    {
        _coordinator.UseReal();
        UpdateModePresentation();
        StatusLabel.Text = "تنظیمات Hub را وارد و صف را بارگذاری کنید.";
    }

    private async void OnLoadClicked(object? sender, EventArgs e)
    {
        await LoadQueueAsync();
    }

    private async Task LoadQueueAsync()
    {
        LoadButton.IsEnabled = false;
        _lastError = null;
        ListCopyReportButton.IsVisible = false;
        ClearReviewDetail();

        try
        {
            HubConnectionSettings? settings = null;
            if (!_coordinator.IsDemo)
            {
                settings = _settings;
                if (settings is null)
                {
                    StatusLabel.Text = "تنظیمات Hub ارائه نشده است.";
                    return;
                }
            }

            var rows = await _coordinator.LoadAsync(settings);
            BindQueueItems(rows);
            StatusLabel.Text = FormattableString.Invariant($"{rows.Count} مورد بارگذاری شد.");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _lastError = exception.Message;
            StatusLabel.Text = exception.Message;
            ListCopyReportButton.IsVisible = true;
        }
        finally
        {
            LoadButton.IsEnabled = true;
        }
    }

    private void BindQueueItems(IReadOnlyList<ReviewQueueRow> rows)
    {
        QueueItems.Clear();
        EmptyState.IsVisible = rows.Count == 0;

        foreach (var row in rows)
        {
            var border = new Border
            {
                Style = Resources["CardStyle"] as Style
            };
            var layout = new VerticalStackLayout
            {
                Style = Resources["RowStackStyle"] as Style
            };
            layout.Add(new Label
            {
                Text = row.InvoiceNumber,
                Style = Resources["SectionTitleStyle"] as Style
            });
            layout.Add(new Label
            {
                Text = $"{row.JalaliDate} — {row.QueueStatus}",
                Style = Resources["BodyStyle"] as Style
            });
            layout.Add(new Label
            {
                Text = row.AmountDisplay,
                Style = Resources["AmountStyle"] as Style
            });
            var selectButton = new Button
            {
                Text = "انتخاب و بازبینی",
                Style = Resources["SecondaryButtonStyle"] as Style
            };
            selectButton.Clicked += (_, _) => OnReviewSelected(row);
            layout.Add(selectButton);
            border.Content = layout;
            QueueItems.Add(border);
        }
    }

    private void ClearReviewDetail()
    {
        ReviewDetailBorder.IsVisible = false;
        _selectedReview = null;
        AttentionCells.Clear();
        ReviewIdLabel.Text = string.Empty;
        ReviewStatusLabel.Text = string.Empty;
        ReviewAmountLabel.Text = string.Empty;
        CorrectionFieldEntry.Text = string.Empty;
        CorrectionValueEntry.Text = string.Empty;
        CustomerPhoneEntry.Text = string.Empty;
        PrintCheckBox.IsChecked = false;
        IssueStatusLabel.Text = string.Empty;
    }

    private void OnReviewSelected(ReviewQueueRow row)
    {
        _selectedReview = row;
        ReviewDetailBorder.IsVisible = true;
        ReviewIdLabel.Text = $"شناسه: {row.ReviewId}";
        ReviewStatusLabel.Text = $"وضعیت: {row.QueueStatus}";
        ReviewAmountLabel.Text = row.AmountDisplay;

        // Load attention cells for real mode
        if (!_coordinator.IsDemo && _settings is not null)
        {
            _ = LoadAttentionCellsAsync(row.ReviewId);
        }
        else
        {
            // Fake mode: show demo attention cells
            AttentionCells.Clear();
            AttentionCells.Add(new Label
            {
                Text = "customer.phone — شماره تماس مشتری نیاز به تایید دارد.",
                Style = Resources["BodyStyle"] as Style
            });
            AttentionCells.Add(new Label
            {
                Text = "summary.payable — مجموع پرداختی نیاز به بازبینی دارد.",
                Style = Resources["BodyStyle"] as Style
            });
        }
    }

    private async Task LoadAttentionCellsAsync(string reviewId)
    {
        if (_settings is null)
            return;

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_settings.BaseUrl.TrimEnd('/')}/v1/stores/{_settings.StoreId:D}/reviews/{reviewId}/attention");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.SessionToken);
            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                IssueStatusLabel.Text = $"بارگذاری لیست اصلاح ناموفق: HTTP {(int)response.StatusCode}.";
                return;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            var items = document.RootElement.GetProperty("items").EnumerateArray();

            AttentionCells.Clear();
            foreach (var cell in items)
            {
                var field = cell.GetProperty("field").GetString() ?? string.Empty;
                var rawValue = cell.GetProperty("raw_value").GetString() ?? string.Empty;
                var reason = cell.GetProperty("reason").GetString() ?? string.Empty;

                AttentionCells.Add(new Label
                {
                    Text = $"{field}: {rawValue} ({reason})",
                    Style = Resources["BodyStyle"] as Style
                });
            }
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            IssueStatusLabel.Text = exception.Message;
        }
    }

    private async void OnCorrectClicked(object? sender, EventArgs e)
    {
        if (_selectedReview is null || string.IsNullOrWhiteSpace(CorrectionFieldEntry.Text) ||
            string.IsNullOrWhiteSpace(CorrectionValueEntry.Text) || _settings is null)
        {
            IssueStatusLabel.Text = "شناسه مورد، مسیر فیلد و مقدار جدید الزامی است.";
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{_settings.BaseUrl.TrimEnd('/')}/v1/stores/{_settings.StoreId:D}/reviews/{_selectedReview.ReviewId}/corrections");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.SessionToken);

            var jsonValue = "{\"value\":" + CorrectionValueEntry.Text + "}";
            var valueElement = JsonDocument.Parse(jsonValue).RootElement.GetProperty("value");
            var body = new { field_path = CorrectionFieldEntry.Text.Trim(), value = valueElement };
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                IssueStatusLabel.Text = $"اصلاح ناموفق: HTTP {(int)response.StatusCode}.";
                return;
            }

            IssueStatusLabel.Text = "اصلاح با موفقیت ثبت شد.";
            CorrectionFieldEntry.Text = string.Empty;
            CorrectionValueEntry.Text = string.Empty;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            IssueStatusLabel.Text = exception.Message;
        }
    }

    private async void OnSetPhoneClicked(object? sender, EventArgs e)
    {
        if (_selectedReview is null || string.IsNullOrWhiteSpace(CustomerPhoneEntry.Text) || _settings is null)
        {
            IssueStatusLabel.Text = "شناسه مورد و تلفن مشتری الزامی است.";
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{_settings.BaseUrl.TrimEnd('/')}/v1/stores/{_settings.StoreId:D}/reviews/{_selectedReview.ReviewId}/customer-phone");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.SessionToken);

            var body = new { phone = CustomerPhoneEntry.Text.Trim() };
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                IssueStatusLabel.Text = $"ثبت تلفن ناموفق: HTTP {(int)response.StatusCode}.";
                return;
            }

            IssueStatusLabel.Text = "تلفن مشتری با موفقیت ثبت شد.";
            CustomerPhoneEntry.Text = string.Empty;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            IssueStatusLabel.Text = exception.Message;
        }
    }

    private async void OnIssueClicked(object? sender, EventArgs e)
    {
        if (_selectedReview is null || _settings is null)
        {
            IssueStatusLabel.Text = "شناسه مورد و تنظیمات Hub الزامی است.";
            return;
        }

        IssueStatusLabel.Text = "در حال صدور…";

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{_settings.BaseUrl.TrimEnd('/')}/v1/stores/{_settings.StoreId:D}/reviews/{_selectedReview.ReviewId}/issue");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.SessionToken);

            var body = new
            {
                service_type = "SALE",
                print = PrintCheckBox.IsChecked is true
            };
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                IssueStatusLabel.Text = $"صدور ناموفق: HTTP {(int)response.StatusCode}.";
                return;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            var root = document.RootElement;

            var status = root.GetProperty("status").GetString() ?? string.Empty;
            var invoiceId = root.TryGetProperty("invoice_id", out var idElem) && idElem.ValueKind == JsonValueKind.String
                ? idElem.GetString()
                : null;
            var displayNumber = root.TryGetProperty("display_number", out var dnElem) && dnElem.ValueKind == JsonValueKind.Number
                ? dnElem.GetInt64()
                : 0;

            // Print status handling with fallback
            var printStatus = string.Empty;
            if (root.TryGetProperty("print", out var printElem) && printElem.ValueKind == JsonValueKind.Object)
            {
                printStatus = printElem.TryGetProperty("status", out var psElem) && psElem.ValueKind == JsonValueKind.String
                    ? psElem.GetString() ?? string.Empty
                    : string.Empty;
            }

            if (PrintCheckBox.IsChecked is true)
            {
                if (string.Equals(printStatus, "PRINT_ADAPTER_DISABLED", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(printStatus, "PRINT_FAILED", StringComparison.OrdinalIgnoreCase))
                {
                    // Print fallback: save print preview to local file
                    await HandlePrintFallbackAsync(invoiceId, displayNumber);
                }
            }

            IssueStatusLabel.Text = FormattableString.Invariant(
                $"صدور انجام شد (وضعیت: {status}, شماره نمایشی: {displayNumber}).");

            // Refresh detail to show issued status
            ReviewStatusLabel.Text = $"وضعیت: {status}";
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            IssueStatusLabel.Text = exception.Message;
        }
    }

    private async Task HandlePrintFallbackAsync(string? invoiceId, long displayNumber)
    {
        try
        {
            var printInfo = FormattableString.Invariant(
                $"Print fallback — Invoice: {invoiceId}, Display number: {displayNumber}, Time: {DateTimeOffset.UtcNow:O}");

            var fallbackDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "print-fallback");
            Directory.CreateDirectory(fallbackDir);

            var fallbackPath = Path.Combine(
                fallbackDir,
                $"print-fallback-{displayNumber}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.txt");

            await File.WriteAllTextAsync(fallbackPath, printInfo);
            await Clipboard.Default.SetTextAsync(printInfo);

            IssueStatusLabel.Text = FormattableString.Invariant(
                $"چاپ ناموفق؛ پیش‌فرض چاپ ذخیره شد: {fallbackPath} (محتوای چاپ در کلیپبورد کپی شد).");
        }
        catch (Exception exception)
        {
            IssueStatusLabel.Text = FormattableString.Invariant(
                $"پیش‌فرض چاپ ناموفق: {exception.Message}");
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
            CopyStatusLabel.Text = FormattableString.Invariant($"کپی ناموفق: {exception.Message}");
        }
    }

    private async void OnListCopyReportClicked(object? sender, EventArgs e)
    {
        await OnCopyReportClicked(sender, e);
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    private static bool IsExpected(Exception exception) => exception is ArgumentException or
        FormatException or HttpRequestException or JsonException or InvalidOperationException or
        TaskCanceledException;
}
