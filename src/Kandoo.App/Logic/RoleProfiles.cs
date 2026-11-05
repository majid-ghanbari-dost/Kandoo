namespace Kandoo.App;

public enum AppUserRole
{
    Store,
    Customer,
    Both
}

public sealed record LocalUserProfile(AppUserRole Role);

public interface IUserProfileStore
{
    LocalUserProfile? Load();
    void Save(LocalUserProfile profile);
}

public static class AppRolePolicy
{
    public const string StorageKey = "kandoo.local-profile.role.v1";

    public static IReadOnlyList<AppUserRole> SupportedRoles(bool isWindows) =>
        isWindows
            ? [AppUserRole.Store]
            : [AppUserRole.Store, AppUserRole.Customer, AppUserRole.Both];

    public static bool IsSupported(AppUserRole role, bool isWindows) =>
        Enum.IsDefined(role) && (!isWindows || role == AppUserRole.Store);

    public static AppUserRole Normalize(AppUserRole role, bool isWindows) =>
        IsSupported(role, isWindows)
            ? role
            : isWindows ? AppUserRole.Store : throw new ArgumentOutOfRangeException(nameof(role));

    public static string Serialize(AppUserRole role) => role switch
    {
        AppUserRole.Store => "store",
        AppUserRole.Customer => "customer",
        AppUserRole.Both => "both",
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    public static bool TryParse(string? value, out AppUserRole role)
    {
        switch (value)
        {
            case "store": role = AppUserRole.Store; return true;
            case "customer": role = AppUserRole.Customer; return true;
            case "both": role = AppUserRole.Both; return true;
            default: role = default; return false;
        }
    }

    public static string DemoDisplayName(AppUserRole role) => role switch
    {
        AppUserRole.Store => "فروشگاه نمونه",
        AppUserRole.Customer => "مشتری نمونه",
        AppUserRole.Both => "فروشگاه و مشتری نمونه",
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };
}
