using System.Text.Json;

namespace Kandoo.App;

public partial class ReturnsPage : ContentPage
{
    private readonly HttpClient _httpClient = new();
    private readonly HubConnectionSettings? _settings;
    private string? _lastError;

    public ReturnsPage(HubConnectionSettings? settings = null)
    {
        _settings = settings;
        InitializeComponent();
        LoadButton.IsEnabled = _settings is not null;
        StatusLabel.Text = _settings is null
            ? "تنظیمات هاب فراهم نشده است."
            : "آماده بارگذاری برگشت‌ها.";
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
            // C-90: Returns screen — placeholder until backend returns API is available.
            // The Hub replication queue already handles retry/backoff for offline scenarios
            // (see HubCloudReplicationClient with exponential backoff).
            StatusLabel.Text = "صف برگشت‌ها در حال حاضر از سمت هاب پشتیبانی نمی‌شود.";
            ReturnsList.Clear();
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
