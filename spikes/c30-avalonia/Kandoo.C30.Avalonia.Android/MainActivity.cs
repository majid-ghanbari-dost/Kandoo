using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace Kandoo.C30.Avalonia.Android;

[Activity(Label = "Kandoo C30 Avalonia Spike", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize)]
public class MainActivity : AvaloniaMainActivity<App>
{
}
