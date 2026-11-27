using System.Globalization;

namespace Kandoo.App;

public sealed partial class StatesPage : ContentPage
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };
    private HubConnectionSettings? _settings;

    public StatesPage(HubConnectionSettings? settings = null)
    {
        _settings = HubConnectionResolver.Resolve(settings) ?? settings;
        InitializeComponent();
        StoreSidebarHelper.AttachSidebar(
            this,
            SidebarHost,
            StoreSidebarSection.Review,
            () => _settings);

        StoreSidebarHelper.ApplyRoundedCorners(EmptyCard, 12);
        StoreSidebarHelper.ApplyRoundedCorners(HubDownCard, 12);
        StoreSidebarHelper.ApplyRoundedCorners(CrashErrorCard, 12);
    }

    private async void OnRetryClicked(object? sender, EventArgs e)
    {
        _settings = HubConnectionResolver.Resolve(_settings) ?? _settings;
        var baseUrl = _settings?.BaseUrl ?? HubConnectionResolver.LoadHubBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl) ||
            !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            ShowProbeStatus("نشانی هاب هنوز تنظیم نشده است؛ ابتدا راه‌اندازی یا جفت‌سازی را انجام دهید.", isError: true);
            return;
        }

        RetryButton.IsEnabled = false;
        RetryButton.Text = "در حال بررسی...";
        try
        {
            var healthUrl = $"{uri.AbsoluteUri.TrimEnd('/')}/health";
            using var response = await _httpClient.GetAsync(healthUrl);
            if (response.IsSuccessStatusCode)
            {
                ShowProbeStatus($"ارتباط با هاب برقرار است ({healthUrl} — HTTP {(int)response.StatusCode}).", isError: false);
            }
            else
            {
                ShowProbeStatus($"هاب پاسخ خطا برگرداند: HTTP {(int)response.StatusCode}.", isError: true);
            }
        }
        catch (Exception exception)
        {
            ShowProbeStatus($"ارتباط با هاب هنوز قطع است: {exception.Message}", isError: true);
        }
        finally
        {
            RetryButton.Text = "تلاش دوباره";
            RetryButton.IsEnabled = true;
        }
    }

    private async void OnCopyReportClicked(object? sender, EventArgs e)
    {
        try
        {
            var lastCrash = AppCrashReports.Store.LoadLastReport();
            var payload = string.IsNullOrWhiteSpace(lastCrash)
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"Kandoo diagnostic summary\nUTC: {DateTimeOffset.UtcNow:O}\nStatus: No unhandled crash recorded on this device.\n")
                : lastCrash;

            await Clipboard.Default.SetTextAsync(payload);
            CopyReportButton.Text = "کپی گزارش خطا ✓";
            ShowProbeStatus(
                string.IsNullOrWhiteSpace(lastCrash)
                    ? "هیچ خطای ثبت‌شده‌ای وجود نداشت؛ خلاصهٔ تشخیصی در حافظه موقت کپی شد."
                    : "آخرین گزارش خطا در حافظه موقت کپی شد.",
                isError: false);
        }
        catch (Exception exception)
        {
            ShowProbeStatus($"کپی گزارش ناموفق بود: {exception.Message}", isError: true);
        }
    }

    private void ShowProbeStatus(string message, bool isError)
    {
        ProbeStatusLabel.IsVisible = true;
        ProbeStatusLabel.Text = message;
        ProbeStatusLabel.TextColor = isError ? StoreSidebarHelper.Review : StoreSidebarHelper.Green;
    }
}
