using Avalonia;

namespace Kandoo.C30.Avalonia.Desktop;

internal static class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--probe")
        {
            return await HubProbe.RunAsync(args[1], args[2]);
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
