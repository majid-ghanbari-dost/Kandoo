namespace Kandoo.App;

internal sealed class PreferencesUserProfileStore : IUserProfileStore
{
    private static readonly bool IsWindows =
#if WINDOWS
        true;
#else
        false;
#endif

    public LocalUserProfile? Load()
    {
        var stored = Preferences.Default.Get(AppRolePolicy.StorageKey, string.Empty);
        return AppRolePolicy.TryParse(stored, out var role)
            ? new LocalUserProfile(AppRolePolicy.Normalize(role, IsWindows))
            : null;
    }

    public void Save(LocalUserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var normalized = AppRolePolicy.Normalize(profile.Role, IsWindows);
        Preferences.Default.Set(AppRolePolicy.StorageKey, AppRolePolicy.Serialize(normalized));
    }
}
