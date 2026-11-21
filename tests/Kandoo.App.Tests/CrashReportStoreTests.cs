using Kandoo.App;
using Xunit;

namespace Kandoo.App.Tests;

public sealed class CrashReportStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "kandoo-c50-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void RecordPersistsUtf8ReportAndLoadReturnsIt()
    {
        var store = new CrashReportStore(
            _directory,
            () => new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
        store.Record("AppDomain", new InvalidOperationException("خطای نمونه"));

        var report = store.LoadLastReport();
        Assert.Contains("2026-11-03T12:00:00.0000000+00:00", report);
        Assert.Contains("Source: AppDomain", report);
        Assert.Contains("خطای نمونه", report);
        Assert.True(File.Exists(store.ReportPath));
        Assert.False(File.Exists(store.ReportPath + ".tmp"));
    }

    [Fact]
    public void RecordReplacesOnlyLastReportAndMissingReportIsEmpty()
    {
        var store = new CrashReportStore(_directory);
        Assert.Equal(string.Empty, store.LoadLastReport());
        store.Record("TaskScheduler", new InvalidOperationException("first"));
        store.Record("AndroidRuntime", new InvalidOperationException("second"));

        var report = store.LoadLastReport();
        Assert.DoesNotContain("first", report);
        Assert.Contains("second", report);
        Assert.Contains("Source: AndroidRuntime", report);
        Assert.Single(System.IO.Directory.GetFiles(_directory, "last-crash.txt"));
    }

    public void Dispose()
    {
        if (System.IO.Directory.Exists(_directory))
        {
            System.IO.Directory.Delete(_directory, true);
        }
    }
}
