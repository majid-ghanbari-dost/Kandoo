namespace Kandoo.App;

public partial class SettingsPage : ContentPage
{
    private readonly HttpClient _httpClient = new();
    private readonly HubConnectionSettings? _settings;
    private string? _lastError;

    public SettingsPage(HubConnectionSettings? settings = null)
    {
        _settings = settings;
        InitializeComponent();
        StatusLabel.Text = _settings is null
            ? "تنظیمات هاب فراهم نشده است."
            : "آماده بررسی تنظیمات.";
    }

    private async void OnRefreshStatusClicked(object? sender, EventArgs e)
    {
        if (_settings is null)
        {
            StatusLabel.Text = "تنظیمات هاب لازم است.";
            return;
        }

        try
        {
            var statusSource = new HubStatusDataSource(_httpClient, _settings);
            var status = await statusSource.GetStatusAsync();
            var stats = await statusSource.GetStatsAsync();

            StatusValueLabel.Text = $"وضعیت: {status.Status}";
            StatsValueLabel.Text = $"فاکتورها: {stats.InvoiceCount}، فروش: {stats.SalesDisplay}";
            StatusLabel.Text = "وضعیت هاب با موفقیت بارگذاری شد.";
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _lastError = exception.Message;
            StatusLabel.Text = exception.Message;
            CopyReportButton.IsVisible = true;
        }
    }

    private async void OnShowOutboxClicked(object? sender, EventArgs e)
    {
        // Demo SMS outbox is local; read from the shared demo SMS provider path if available.
        var outboxPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "outbox",
            "demo-sms.log");

        if (File.Exists(outboxPath))
        {
            var lines = File.ReadAllLines(outboxPath);
            OutboxValueLabel.Text = lines.Length > 0
                ? $"تعداد پیامک در outbox: {lines.Length}"
                : "outbox خالی است.";
        }
        else
        {
            OutboxValueLabel.Text = "فایل outbox یافت نشد (هیچ پیامک نمایشی ارسال نشده است).";
        }
    }

    private async void OnBackupClicked(object? sender, EventArgs e)
    {
        // Local SQLite backup is best-effort; show a status message.
        try
        {
            var dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "kandoo-hub.db");
            if (File.Exists(dbPath))
            {
                var backupPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    $"kandoo-hub-backup-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.db");
                File.Copy(dbPath, backupPath, overwrite: true);
                BackupValueLabel.Text = $"پشتیبان گرفته شد: {backupPath}";
            }
            else
            {
                BackupValueLabel.Text = "فایل دیتابیس محلی یافت نشد.";
            }
        }
        catch (Exception exception)
        {
                BackupValueLabel.Text = $"پشتیبان‌گیری ناموفق: {exception.Message}";
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
        TaskCanceledException or IOException or UnauthorizedAccessException;
}
