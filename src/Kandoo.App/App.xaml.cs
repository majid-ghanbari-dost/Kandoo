using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;

namespace Kandoo.App;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        IUserProfileStore profiles = new PreferencesUserProfileStore();
        var profile = profiles.Load();
        Page startPage = profile is null
            ? new SetupWizardPage(profiles)
            : new MainPage(profile, profiles);
        MainPage = new NavigationPage(startPage);
    }

    // M-005 item 3a: keep window chrome (minimize/close) on the RIGHT on Windows
    // while page content stays RTL (Persian). PageStyle already sets
    // FlowDirection=RightToLeft on ContentPage; that must not leak to the
    // window title bar. Override CreateWindow to force LTR chrome only.
    protected override Window CreateWindow(IActivationState activationState)
    {
        var window = base.CreateWindow(activationState);
        window.FlowDirection = FlowDirection.LeftToRight;
        return window;
    }
}
