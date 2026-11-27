namespace Kandoo.App;

public partial class RoleSelectionPage : ContentPage
{
    private readonly IUserProfileStore _profiles;
    private readonly bool _isWindows;

    public RoleSelectionPage(IUserProfileStore profiles, LocalUserProfile? current = null)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        InitializeComponent();
#if WINDOWS
        _isWindows = true;
#else
        _isWindows = false;
#endif
        CurrentRoleLabel.Text = current is null
            ? "برای شروع، نقش را انتخاب کنید؛ بعداً از تنظیمات قابل تغییر است."
            : $"نقش فعلی: {AppRolePolicy.DemoDisplayName(current.Role)}";
        PlatformNoteLabel.Text = _isWindows
            ? "در Windows فقط نقش فروشگاه ارائه می‌شود؛ حالت مشتری MVP فعلاً Android-only است."
            : "حالت مشتری روی اندروید: ورود با شماره‌ی موبایل و کد پیامکی، بعد فهرست فاکتورها.";
        CustomerButton.IsVisible = !_isWindows;
        BothButton.IsVisible = !_isWindows;
        BackButton.IsVisible = current is not null;
    }

    private async void OnBackClicked(object? sender, EventArgs e) =>
        await Navigation.PopModalAsync();

    private async void OnCrashReportClicked(object? sender, EventArgs e) =>
        await Navigation.PushModalAsync(new CrashReportPage());

    private void OnStoreClicked(object? sender, EventArgs e) => Select(AppUserRole.Store);
    private void OnCustomerClicked(object? sender, EventArgs e) => Select(AppUserRole.Customer);
    private void OnBothClicked(object? sender, EventArgs e) => Select(AppUserRole.Both);

    private void Select(AppUserRole role)
    {
        if (!AppRolePolicy.IsSupported(role, _isWindows))
        {
            throw new InvalidOperationException("This role is not supported on the current platform.");
        }

        var profile = new LocalUserProfile(role);
        _profiles.Save(profile);
        // C-92 role switch: route to the chosen role's home (customer mode lands
        // on فاکتورهای من — RoleStart is unit-tested; see RoleStartPages).
        Application.Current!.MainPage = RoleStartPages.Create(profile, _profiles);
    }
}
