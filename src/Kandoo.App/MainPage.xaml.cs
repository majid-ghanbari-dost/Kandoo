using System.Security.Cryptography;
using System.Text.Json;

namespace Kandoo.App;

public partial class MainPage : ContentPage
{
    private readonly HttpClient _httpClient = new();
    private readonly ReviewQueueCoordinator _coordinator;
    private readonly IUserProfileStore _profiles;
    private readonly LocalUserProfile _profile;
    private readonly SharedTokenFile _tokenFile = new();
    private bool _initialLoadComplete;
    private string? _lastError;

    public MainPage(LocalUserProfile profile, IUserProfileStore profiles)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        InitializeComponent();
        _coordinator = new ReviewQueueCoordinator(
            new FakeReviewQueueDataSource(),
            settings => new HubReviewQueueDataSource(_httpClient, settings));
        UpdateModePresentation();
        UpdateRolePresentation();
        UpdateActionButtonState();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_profile.Role == AppUserRole.Customer)
        {
            return;
        }

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

    private async void OnRoleSettingsClicked(object? sender, EventArgs e) =>
        await Navigation.PushModalAsync(new RoleSelectionPage(_profiles, _profile));

    private async void OnCrashReportClicked(object? sender, EventArgs e) =>
        await Navigation.PushModalAsync(new CrashReportPage());

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

            // Persist the device/session token to the shared token file so all
            // Stage-A pages can access it without re-entering credentials.
            try
            {
                var storeId = Guid.Parse(StoreIdEntry.Text);
                _tokenFile.Save(new DeviceTokenRecord(SessionTokenEntry.Text, storeId));
            }
            catch (Exception)
            {
                // Token persistence is best-effort; pairing still succeeded.
            }
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

    private void UpdateRolePresentation()
    {
        RoleLabel.Text = $"نقش فعلی: {AppRolePolicy.DemoDisplayName(_profile.Role)}";
        StoreWorkspace.IsVisible = _profile.Role is AppUserRole.Store or AppUserRole.Both;
        CustomerPanel.IsVisible = _profile.Role is AppUserRole.Customer or AppUserRole.Both;
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

    private HubConnectionSettings? ReadHubSettings()
    {
        if (!string.IsNullOrWhiteSpace(StoreIdEntry.Text) &&
            Guid.TryParse(StoreIdEntry.Text, out var storeId))
        {
            return new HubConnectionSettings(
                HubBaseUrlEntry.Text ?? string.Empty,
                storeId,
                SessionTokenEntry.Text ?? string.Empty);
        }

        // Fall back to shared token file for devices already paired.
        var record = _tokenFile.Load();
        if (record is not null)
        {
            StatusLabel.Text = "نشست دستگاه از فایل توکن مشترک بارگذاری شد.";
            return new HubConnectionSettings(string.Empty, record.StoreId, record.Token);
        }

        return null;
    }

    private async Task LoadQueueAsync()
    {
        LoadButton.IsEnabled = false;
        StatusLabel.Text = "در حال بارگذاری…";
        _lastError = null;
        try
        {
            HubConnectionSettings? settings = null;
            if (_coordinator.Mode == ReviewDataMode.Real)
            {
                settings = ReadHubSettings();
                if (settings is not null)
                {
                    // Persist to shared token file for other Stage-A pages.
                    try
                    {
                        _tokenFile.Save(new DeviceTokenRecord(settings.SessionToken, settings.StoreId));
                    }
                    catch (Exception)
                    {
                        // Best-effort persistence.
                    }
                }
            }

            var rows = await _coordinator.LoadAsync(settings);
            BindableLayout.SetItemsSource(QueueItems, rows);
            StatusLabel.Text = FormattableString.Invariant($"{rows.Count} مورد بارگذاری شد.");
            CopyReportButton.IsVisible = false;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _lastError = exception.Message;
            BindableLayout.SetItemsSource(QueueItems, Array.Empty<ReviewQueueRow>());
            StatusLabel.Text = exception.Message;
            CopyReportButton.IsVisible = true;
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
            CopyStatusLabel.Text = "گزارش کپی شد.";
        }
        catch (Exception exception)
        {
            CopyStatusLabel.Text = $"کپی ناموفق: {exception.Message}";
        }
    }

    private async void OnProductsClicked(object? sender, EventArgs e)
    {
        try
        {
            var settings = ReadHubSettings();
            await Navigation.PushAsync(new ProductsPage(settings));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            StatusLabel.Text = exception.Message;
        }
    }

    private async void OnReviewQueueClicked(object? sender, EventArgs e)
    {
        try
        {
            var settings = ReadHubSettings();
            await Navigation.PushAsync(new ReviewQueuePage(_coordinator, settings));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            StatusLabel.Text = exception.Message;
        }
    }

    private async void OnCreateInvoiceClicked(object? sender, EventArgs e)
    {
        try
        {
            var settings = ReadHubSettings();
            if (settings is null)
            {
                StatusLabel.Text = "تنظیمات هاب لازم است.";
                return;
            }
            await Navigation.PushAsync(new CreateInvoicePage(settings));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            StatusLabel.Text = exception.Message;
        }
    }

    private async void OnProductEditorClicked(object? sender, EventArgs e)
    {
        try
        {
            var settings = ReadHubSettings();
            if (settings is null)
            {
                StatusLabel.Text = "تنظیمات هاب لازم است.";
                return;
            }
            await Navigation.PushAsync(new ProductEditorPage(settings));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            StatusLabel.Text = exception.Message;
        }
    }

    private async void OnCustomersClicked(object? sender, EventArgs e)
    {
        try
        {
            var settings = ReadHubSettings();
            if (settings is null)
            {
                StatusLabel.Text = "تنظیمات هاب لازم است.";
                return;
            }
            await Navigation.PushAsync(new CustomersPage(settings));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            StatusLabel.Text = exception.Message;
        }
    }

    private async void OnSettingsClicked(object? sender, EventArgs e)
    {
        try
        {
            var settings = ReadHubSettings();
            await Navigation.PushAsync(new SettingsPage(settings));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            StatusLabel.Text = exception.Message;
        }
    }

    private async void OnReturnsClicked(object? sender, EventArgs e)
    {
        try
        {
            var settings = ReadHubSettings();
            await Navigation.PushAsync(new ReturnsPage(settings));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            StatusLabel.Text = exception.Message;
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

    private void UpdateActionButtonState()
    {
        var hasSettings = ReadHubSettings() is not null;
        CreateInvoiceButton.IsEnabled = hasSettings;
        ProductEditorButton.IsEnabled = hasSettings;
        CustomersButton.IsEnabled = hasSettings;
        ReturnsButton.IsEnabled = hasSettings;
        SettingsButton.IsEnabled = true; // Settings page works without Hub settings for local backup
    }

    private static bool IsExpected(Exception exception) => exception is ArgumentException or
        FormatException or HttpRequestException or JsonException or InvalidOperationException or
        TaskCanceledException or CryptographicException;
}
