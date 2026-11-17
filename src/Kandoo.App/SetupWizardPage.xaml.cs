namespace Kandoo.App;

public partial class SetupWizardPage : ContentPage
{
    private readonly IUserProfileStore _profiles;
    private readonly SharedTokenFile _tokenFile = new();
    private AppUserRole _selectedRole = AppUserRole.Store;
    private string? _lastError;

    public SetupWizardPage(IUserProfileStore profiles)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        InitializeComponent();
        UpdateStepVisibility();
        StatusLabel.Text = "مرحله ۱: نقش را انتخاب کنید.";
    }

    private void OnStoreClicked(object? sender, EventArgs e)
    {
        _selectedRole = AppUserRole.Store;
        RoleNoteLabel.Text = "نقش فروشگاه انتخاب شد (فقط در Windows قابل اجرا).";
        UpdateStepVisibility();
    }

    private void OnCustomerClicked(object? sender, EventArgs e)
    {
        _selectedRole = AppUserRole.Customer;
        RoleNoteLabel.Text = "نقش مشتری انتخاب شد (فقط در Android).";
        UpdateStepVisibility();
    }

    private void OnBothClicked(object? sender, EventArgs e)
    {
        _selectedRole = AppUserRole.Both;
        RoleNoteLabel.Text = "نقش هر دو انتخاب شد (فقط در Android).";
        UpdateStepVisibility();
    }

    private void UpdateStepVisibility()
    {
#if WINDOWS
        CustomerButton.IsVisible = false;
        BothButton.IsVisible = false;
#endif
        Step1Panel.IsVisible = true;
        Step2Panel.IsVisible = false;
        Step3Panel.IsVisible = false;

        Step1Label.TextColor = Colors.Black;
        Step2Label.TextColor = Colors.Gray;
        Step3Label.TextColor = Colors.Gray;
    }

    private async void OnFinishClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(StoreIdEntry.Text) ||
            !Guid.TryParse(StoreIdEntry.Text, out var storeId))
        {
            StatusLabel.Text = "شناسه فروشگاه معتبر نیست.";
            CopyReportButton.IsVisible = false;
            return;
        }

        var settings = new HubConnectionSettings(
            HubBaseUrlEntry.Text ?? string.Empty,
            storeId,
            SessionTokenEntry.Text ?? string.Empty);

        try
        {
            // Validate base URL format
            _ = HubApiUtilities.ValidatedBaseUrl(settings);

            // Save profile
            var profile = new LocalUserProfile(_selectedRole);
            _profiles.Save(profile);

            // Save token to shared file
            _tokenFile.Save(new DeviceTokenRecord(settings.SessionToken, settings.StoreId));

            // Navigate to MainPage
            Application.Current!.MainPage = new NavigationPage(new MainPage(profile, _profiles));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _lastError = exception.Message;
            StatusLabel.Text = exception.Message;
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

    private static bool IsExpected(Exception exception) => exception is ArgumentException or
        FormatException or InvalidOperationException;
}
