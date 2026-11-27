namespace Kandoo.App;

/// <summary>
/// MAUI-side factory for the role-switch routing (C-92). The decision lives in
/// <see cref="RoleStart"/> (unit-tested); this class only materializes the pages.
/// Used by App start-up and by RoleSelectionPage.Select (the «نقش‌ها» switch).
/// </summary>
public static class RoleStartPages
{
    public static Page HomeFor(LocalUserProfile profile, IUserProfileStore profiles)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(profiles);
#if WINDOWS
        const bool isWindows = true;
#else
        const bool isWindows = false;
#endif
        return RoleStart.For(profile.Role, isWindows) switch
        {
            RoleStartKind.CustomerHome => new CustomerInvoicesPage(),
            _ => new MainPage(profile, profiles),
        };
    }

    public static NavigationPage Create(LocalUserProfile profile, IUserProfileStore profiles) =>
        new(HomeFor(profile, profiles));
}
