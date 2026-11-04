namespace Kandoo.App;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        IUserProfileStore profiles = new PreferencesUserProfileStore();
        var profile = profiles.Load();
        Page startPage = profile is null
            ? new RoleSelectionPage(profiles)
            : new MainPage(profile, profiles);
        MainPage = startPage;
    }
}
