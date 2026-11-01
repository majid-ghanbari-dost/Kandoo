using System.Security.Cryptography;
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
        ClearC41Views();
        UpdateModePresentation();
        await LoadQueueAsync();
    }

    private void OnRealModeClicked(object? sender, EventArgs e)
    {
        _coordinator.UseReal();
        BindableLayout.SetItemsSource(QueueItems, Array.Empty<ReviewQueueRow>());
        ClearC41Views();
        UpdateModePresentation();
        StatusLabel.Text = "تنظیمات Hub را وارد و صف را بارگذاری کنید.";
    }

    private async void OnLoadClicked(object? sender, EventArgs e) => await LoadQueueAsync();

    private async void OnPairClicked(object? sender, EventArgs e)
    {
        PairButton.IsEnabled = false;
        PairingStatusLabel.Text = "در حال مصرف کد با Hub واقعی…";
        try
        {
            var code = PairingCodeParser.Parse(PairingCodeEntry.Text);
            var result = await new HubPairingDataSource(_httpClient).PairAsync(
                code, PairingLabelEntry.Text);
            HubBaseUrlEntry.Text = result.HubEndpoint;
            StoreIdEntry.Text = result.StoreId.ToString("D");
            SessionTokenEntry.Text = result.SessionToken;
            PairingCodeEntry.Text = string.Empty;
            PairingStatusLabel.Text = $"جفت‌سازی انجام شد؛ سطح {result.OperatorLevel ?? "ثبت نشده"}.";
            StatusLabel.Text = "جفت‌سازی انجام شد؛ اکنون صف را بارگذاری کنید.";
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            PairingStatusLabel.Text = exception.Message;
        }
        finally
        {
            PairButton.IsEnabled = true;
        }
    }

    private async void OnLoadDevicesClicked(object? sender, EventArgs e) => await LoadDevicesAsync();

    private async void OnSellerClicked(object? sender, EventArgs e) =>
        await ChangeDeviceLevelAsync(sender, DeviceLevels.Seller);

    private async void OnOperatorClicked(object? sender, EventArgs e) =>
        await ChangeDeviceLevelAsync(sender, DeviceLevels.Operator);

    private async void OnManagerClicked(object? sender, EventArgs e) =>
        await ChangeDeviceLevelAsync(sender, DeviceLevels.Manager);

    private async void OnRevokeClicked(object? sender, EventArgs e)
    {
        if (!TryDeviceId(sender, out var deviceId))
        {
            return;
        }

        var confirmed = await DisplayAlert(
            "لغو دستگاه", "دستگاه همراه لغو شود؟", "لغو", "بازگشت");
        if (!confirmed)
        {
            return;
        }

        try
        {
            var source = new HubDeviceAdministrationDataSource(_httpClient, ReadHubSettings());
            await source.RevokeAsync(deviceId);
            DevicesStatusLabel.Text = "دستگاه لغو شد.";
            await LoadDevicesAsync();
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            DevicesStatusLabel.Text = exception.Message;
        }
    }

    private void UpdateModePresentation()
    {
        var isReal = !_coordinator.IsDemo;
        DemoBanner.IsVisible = _coordinator.IsDemo;
        RealSettings.IsVisible = isReal;
        ModeLabel.Text = _coordinator.IsDemo
            ? "حالت نمایشی؛ بدون نیاز به تنظیمات"
            : "حالت واقعی؛ اتصال مستقیم به Hub";
#if ANDROID
        PairingSection.IsVisible = isReal;
        DevicesSection.IsVisible = false;
#elif WINDOWS
        PairingSection.IsVisible = false;
        DevicesSection.IsVisible = isReal;
#else
        PairingSection.IsVisible = false;
        DevicesSection.IsVisible = false;
#endif
    }

    private void ClearC41Views()
    {
        PairingCodeEntry.Text = string.Empty;
        PairingStatusLabel.Text = "کد جفت‌سازی را وارد کنید.";
        BindableLayout.SetItemsSource(DeviceItems, Array.Empty<DeviceRow>());
        DevicesStatusLabel.Text = "نشست مالک لازم است.";
    }

    private HubConnectionSettings ReadHubSettings()
    {
        if (!Guid.TryParse(StoreIdEntry.Text, out var storeId))
        {
            throw new FormatException("شناسه فروشگاه معتبر نیست.");
        }

        return new HubConnectionSettings(
            HubBaseUrlEntry.Text ?? string.Empty,
            storeId,
            SessionTokenEntry.Text ?? string.Empty);
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
                settings = ReadHubSettings();
            }

            var rows = await _coordinator.LoadAsync(settings);
            BindableLayout.SetItemsSource(QueueItems, rows);
            StatusLabel.Text = FormattableString.Invariant($"{rows.Count} مورد بارگذاری شد.");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            BindableLayout.SetItemsSource(QueueItems, Array.Empty<ReviewQueueRow>());
            StatusLabel.Text = exception.Message;
        }
        finally
        {
            LoadButton.IsEnabled = true;
        }
    }

    private async Task LoadDevicesAsync()
    {
        LoadDevicesButton.IsEnabled = false;
        DevicesStatusLabel.Text = "در حال بارگذاری دستگاه‌ها…";
        try
        {
            var source = new HubDeviceAdministrationDataSource(_httpClient, ReadHubSettings());
            var rows = await source.ListAsync();
            BindableLayout.SetItemsSource(DeviceItems, rows);
            DevicesStatusLabel.Text = FormattableString.Invariant($"{rows.Count} دستگاه بارگذاری شد.");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            BindableLayout.SetItemsSource(DeviceItems, Array.Empty<DeviceRow>());
            DevicesStatusLabel.Text = exception.Message;
        }
        finally
        {
            LoadDevicesButton.IsEnabled = true;
        }
    }

    private async Task ChangeDeviceLevelAsync(object? sender, string level)
    {
        if (!TryDeviceId(sender, out var deviceId))
        {
            return;
        }

        try
        {
            var source = new HubDeviceAdministrationDataSource(_httpClient, ReadHubSettings());
            await source.ChangeLevelAsync(deviceId, level);
            DevicesStatusLabel.Text = $"سطح دستگاه به {level} تغییر کرد.";
            await LoadDevicesAsync();
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            DevicesStatusLabel.Text = exception.Message;
        }
    }

    private static bool TryDeviceId(object? sender, out Guid deviceId)
    {
        deviceId = Guid.Empty;
        if (sender is not Button button)
        {
            return false;
        }

        if (button.CommandParameter is Guid value)
        {
            deviceId = value;
            return value != Guid.Empty;
        }

        return Guid.TryParse(button.CommandParameter?.ToString(), out deviceId) && deviceId != Guid.Empty;
    }

    private static bool IsExpected(Exception exception) => exception is ArgumentException or
        FormatException or HttpRequestException or JsonException or InvalidOperationException or
        TaskCanceledException or CryptographicException;
}
