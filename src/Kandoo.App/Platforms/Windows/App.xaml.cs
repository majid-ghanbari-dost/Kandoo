namespace Kandoo.App.WinUI;

public partial class App : MauiWinUIApplication
{
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) => AppCrashReports.Record("WinUI", args.Exception);
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
