using System.Diagnostics;

namespace Kandoo.App;

internal static class AppCrashReports
{
    private static readonly Lazy<CrashReportStore> LazyStore =
        new(() => new CrashReportStore(FileSystem.AppDataDirectory));
    private static int _registered;

    internal static CrashReportStore Store => LazyStore.Value;

    internal static void RegisterGlobalHandlers()
    {
        if (Interlocked.Exchange(ref _registered, 1) != 0)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var exception = args.ExceptionObject as Exception ?? new InvalidOperationException(
                $"Non-Exception crash object: {args.ExceptionObject?.GetType().FullName ?? "null"}");
            Record("AppDomain", exception);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Record("TaskScheduler", args.Exception);
            args.SetObserved();
        };
    }

    internal static void Record(string source, Exception exception)
    {
        try
        {
            Store.Record(source, exception);
        }
        catch (Exception persistenceFailure)
        {
            Debug.WriteLine($"KANDOO_CRASH_PERSIST_FAILED {persistenceFailure}");
        }

        try
        {
            Debug.WriteLine($"KANDOO_CRASH source={source} {exception}");
#if ANDROID
            Android.Util.Log.Error("KandooCrash", $"source={source}\n{exception}");
#endif
        }
        catch
        {
            // A crash handler must never create a second unhandled exception.
        }
    }
}
