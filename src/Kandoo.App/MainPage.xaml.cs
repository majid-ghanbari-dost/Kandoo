using System.Text.Json;

namespace Kandoo.App;

public partial class MainPage : ContentPage
{
    private readonly HttpClient _httpClient = new();
    private readonly ReviewQueueCoordinator _coordinator;
    private bool _initialLoadComplete;

    public MainPage()
    {
        InitializeComponent();
        _coordinator = new ReviewQueueCoordinator(
            new FakeReviewQueueDataSource(),
            settings => new HubReviewQueueDataSource(_httpClient, settings));
        UpdateModePresentation();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_initialLoadComplete)
        {
            return;
        }

        _initialLoadComplete = true;
        await LoadQueueAsync();
    }

    private async void OnFakeModeClicked(object? sender, EventArgs e)
    {
        _coordinator.UseFake();
        BindableLayout.SetItemsSource(QueueItems, Array.Empty<ReviewQueueRow>());
        UpdateModePresentation();
        await LoadQueueAsync();
    }

    private void OnRealModeClicked(object? sender, EventArgs e)
    {
        _coordinator.UseReal();
        BindableLayout.SetItemsSource(QueueItems, Array.Empty<ReviewQueueRow>());
        UpdateModePresentation();
        StatusLabel.Text = "تنظیمات Hub را وارد و صف را بارگذاری کنید.";
    }

    private async void OnLoadClicked(object? sender, EventArgs e) => await LoadQueueAsync();

    private void UpdateModePresentation()
    {
        DemoBanner.IsVisible = _coordinator.IsDemo;
        RealSettings.IsVisible = !_coordinator.IsDemo;
        ModeLabel.Text = _coordinator.IsDemo
            ? "حالت نمایشی؛ بدون نیاز به تنظیمات"
            : "حالت واقعی؛ اتصال مستقیم به Hub";
    }

    private async Task LoadQueueAsync()
    {
        LoadButton.IsEnabled = false;
        StatusLabel.Text = "در حال بارگذاری…";
        try
        {
            HubConnectionSettings? settings = null;
            if (_coordinator.Mode == ReviewDataMode.Real)
            {
                if (!Guid.TryParse(StoreIdEntry.Text, out var storeId))
                {
                    throw new FormatException("شناسه فروشگاه معتبر نیست.");
                }

                settings = new HubConnectionSettings(
                    HubBaseUrlEntry.Text ?? string.Empty,
                    storeId,
                    SessionTokenEntry.Text ?? string.Empty);
            }

            var rows = await _coordinator.LoadAsync(settings);
            BindableLayout.SetItemsSource(QueueItems, rows);
            StatusLabel.Text = FormattableString.Invariant($"{rows.Count} مورد بارگذاری شد.");
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or
            HttpRequestException or JsonException or InvalidOperationException or TaskCanceledException)
        {
            BindableLayout.SetItemsSource(QueueItems, Array.Empty<ReviewQueueRow>());
            StatusLabel.Text = exception.Message;
        }
        finally
        {
            LoadButton.IsEnabled = true;
        }
    }
}
