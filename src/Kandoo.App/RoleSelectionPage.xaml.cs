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
            : "حالت مشتری فقط انتخاب پروفایل است؛ فهرست فاکتورها در این مرحله اضافه نشده است.";
        CustomerButton.IsVisible = !_isWindows;
        BothButton.IsVisible = !_isWindows;
        BackButton.IsVisible = current is not null;
        SizeChanged += (_, _) => LogLayout("size");
        LogLayout("ctor");
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LogLayout("appearing");
    }

    private void LogLayout(string stage)
    {
        if (Content is not ScrollView { Content: Layout layout })
        {
            System.Diagnostics.Debug.WriteLine($"KANDOO_C58_LAYOUT stage={stage} content=missing");
            return;
        }
        System.Diagnostics.Debug.WriteLine(
            $"KANDOO_C58_LAYOUT stage={stage} children={layout.Children.Count} page={Width:F0}x{Height:F0} grid={layout.Width:F0}x{layout.Height:F0}");
        foreach (var child in layout.Children.OfType<VisualElement>())
        {
            System.Diagnostics.Debug.WriteLine(
                $"KANDOO_C58_CHILD type={child.GetType().Name} visible={child.IsVisible} size={child.Width:F0}x{child.Height:F0}");
        }
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
        Application.Current!.MainPage = new NavigationPage(new MainPage(profile, _profiles));
    }
}
