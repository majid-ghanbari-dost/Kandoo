namespace Kandoo.App;

/// <summary>Where the app should land for a chosen role (role switch, C-92).</summary>
public enum RoleStartKind
{
    /// <summary>Store workspace (review queue, invoices, products, pairing) — MainPage.</summary>
    StoreWorkspace,

    /// <summary>Customer mode home (فاکتورهای من, login gate) — CustomerInvoicesPage.</summary>
    CustomerHome,
}

/// <summary>
/// Role-switch routing (ORDER 16 / C-92, UX spec §1-§6): one app, several roles under
/// one verified mobile number; switching happens from the «نقش‌ها» tab / «تنظیمات نقش».
/// Windows is store-only; the customer role is Android-only for MVP-1.
/// </summary>
public static class RoleStart
{
    public static RoleStartKind For(AppUserRole role, bool isWindows) => role switch
    {
        AppUserRole.Customer when !isWindows => RoleStartKind.CustomerHome,
        AppUserRole.Store => RoleStartKind.StoreWorkspace,
        AppUserRole.Both => RoleStartKind.StoreWorkspace,
        // Windows customer mode is after MVP (product-ux-spec §1); Normalize() keeps
        // the selected role valid per platform before we ever get here.
        AppUserRole.Customer => RoleStartKind.StoreWorkspace,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role."),
    };

    /// <summary>«نقش: مشتری» — the top-of-page role label of the customer boards.</summary>
    public static string RoleLabel(AppUserRole role) => role switch
    {
        AppUserRole.Customer => "نقش: مشتری",
        AppUserRole.Store => "نقش: فروشگاه",
        AppUserRole.Both => "نقش: هر دو",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role."),
    };
}
