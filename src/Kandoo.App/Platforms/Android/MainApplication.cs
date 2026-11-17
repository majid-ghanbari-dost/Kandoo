using Android.App;
using Android.Runtime;

namespace Kandoo.App;

[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership) : base(handle, ownership)
    {
        AndroidEnvironment.UnhandledExceptionRaiser += (_, args) =>
            AppCrashReports.Record("AndroidRuntime", args.Exception);
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
