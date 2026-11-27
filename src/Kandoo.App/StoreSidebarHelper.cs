using Microsoft.Maui.Controls.Shapes;

namespace Kandoo.App;

public enum StoreSidebarSection
{
    None,
    Review,
    Invoices,
    Products,
    Customers,
    Accounting,
    Devices,
    Settings
}

/// <summary>
/// Shared Windows v2 chrome helper for C-WIN-B store screens: builds the 220px right-hand
/// dark-ink navigation rail shown on the Windows boards (<c>*.dc.html</c>), hides it on
/// Android / narrow viewports, and applies 16px rounded corners in C# to avoid XamlC XFC0009.
/// </summary>
internal static class StoreSidebarHelper
{
    public static readonly Color Honey = Color.FromArgb("#F2A900");
    public static readonly Color Ink = Color.FromArgb("#2B1D0E");
    public static readonly Color Cream = Color.FromArgb("#FFF8E7");
    public static readonly Color Wax = Color.FromArgb("#F5E9C8");
    public static readonly Color Green = Color.FromArgb("#256B43");
    public static readonly Color Review = Color.FromArgb("#9A3B0A");
    public static readonly Color MutedInk = Color.FromArgb("#5A4630");
    public static readonly Color UnconfirmedBrown = Color.FromArgb("#6B5A3E");
    public static readonly Color BorderTan = Color.FromArgb("#C9B88E");
    public static readonly Color DividerCream = Color.FromArgb("#F0E4C2");
    public static readonly Color CardWhite = Color.FromArgb("#FFFFFF");

    public static Func<HubConnectionSettings?, Page>? AccountingPageFactory { get; set; }

    public static void ApplyRoundedCorners(Border border, double cornerRadius = 16)
    {
        ArgumentNullException.ThrowIfNull(border);
        border.StrokeShape = new RoundRectangle
        {
            CornerRadius = new CornerRadius(cornerRadius)
        };
    }

    public static View AttachSidebar(
        ContentPage hostPage,
        VerticalStackLayout sidebarContainer,
        StoreSidebarSection activeSection,
        Func<HubConnectionSettings?> settingsAccessor)
    {
        ArgumentNullException.ThrowIfNull(hostPage);
        ArgumentNullException.ThrowIfNull(sidebarContainer);
        ArgumentNullException.ThrowIfNull(settingsAccessor);

        sidebarContainer.Children.Clear();
        sidebarContainer.BackgroundColor = Ink;
        sidebarContainer.WidthRequest = 220;
        sidebarContainer.Padding = new Thickness(16, 24);
        sidebarContainer.Spacing = 8;

        sidebarContainer.Add(new Label
        {
            Text = "کندو",
            FontFamily = "Vazirmatn-Bold",
            FontSize = 24,
            TextColor = Cream,
            Margin = new Thickness(4, 0, 4, 16)
        });

        var items = new (StoreSidebarSection Section, string Label)[]
        {
            (StoreSidebarSection.Review, "صف بازبینی"),
            (StoreSidebarSection.Invoices, "فاکتورها"),
            (StoreSidebarSection.Products, "کالاها"),
            (StoreSidebarSection.Customers, "مشتری‌ها"),
            (StoreSidebarSection.Accounting, "حسابداری"),
            (StoreSidebarSection.Devices, "دستگاه‌ها و دسترسی"),
            (StoreSidebarSection.Settings, "تنظیمات"),
        };

        foreach (var (section, label) in items)
        {
            var isActive = section == activeSection;
            var navButton = new Button
            {
                Text = label,
                FontFamily = isActive ? "Vazirmatn-Bold" : "Vazirmatn",
                FontSize = 15,
                BackgroundColor = isActive ? Honey : Colors.Transparent,
                TextColor = isActive ? Ink : Cream,
                CornerRadius = 8,
                MinimumHeightRequest = 44,
                Padding = new Thickness(12, 8),
                HorizontalOptions = LayoutOptions.Fill
            };
            SemanticProperties.SetDescription(navButton, $"رفتن به {label}");
            navButton.Clicked += async (_, _) =>
            {
                if (section == activeSection)
                {
                    return;
                }

                await NavigateAsync(hostPage, section, settingsAccessor());
            };
            sidebarContainer.Add(navButton);
        }

#if ANDROID
        sidebarContainer.IsVisible = false;
#else
        sidebarContainer.IsVisible = hostPage.Width <= 0 || hostPage.Width >= 760;
        hostPage.SizeChanged += (_, _) =>
        {
            if (hostPage.Width > 0)
            {
                sidebarContainer.IsVisible = hostPage.Width >= 760;
            }
        };
#endif

        return sidebarContainer;
    }

    private static async Task NavigateAsync(
        ContentPage current,
        StoreSidebarSection target,
        HubConnectionSettings? rawSettings)
    {
        var settings = HubConnectionResolver.Resolve(rawSettings) ?? rawSettings;
        Page? destination = target switch
        {
            StoreSidebarSection.Review => new ReviewQueuePage(
                new ReviewQueueCoordinator(
                    new FakeReviewQueueDataSource(),
                    s => new HubReviewQueueDataSource(new HttpClient(), s)),
                settings),
            StoreSidebarSection.Invoices => new InvoicesListPage(settings),
            StoreSidebarSection.Products => new ProductsPage(settings),
            StoreSidebarSection.Customers => new CustomersPage(settings),
            StoreSidebarSection.Accounting => AccountingPageFactory?.Invoke(settings) ?? new AccountingPage(settings),
            StoreSidebarSection.Devices => new DevicesPage(),
            StoreSidebarSection.Settings => new SettingsPage(settings),
            _ => null
        };

        if (destination is null)
        {
            await current.DisplayAlert("حسابداری", "صفحهٔ حسابداری مدیریتی در حال آماده‌سازی است.", "متوجه شدم");
            return;
        }

        await current.Navigation.PushAsync(destination);
    }
}
