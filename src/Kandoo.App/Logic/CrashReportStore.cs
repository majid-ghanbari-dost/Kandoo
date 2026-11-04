using System.Globalization;
using System.Text;

namespace Kandoo.App;

/// <summary>Persists only the most recent crash report; it never sends it anywhere.</summary>
public sealed class CrashReportStore
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _utcNow;

    public CrashReportStore(string directory, Func<DateTimeOffset>? utcNow = null)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("Crash report directory is required.", nameof(directory));
        }

        Directory = directory;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public string Directory { get; }
    public string ReportPath => Path.Combine(Directory, "last-crash.txt");

    public void Record(string source, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(exception);
        var report = Format(source, exception, _utcNow());
        lock (_gate)
        {
            System.IO.Directory.CreateDirectory(Directory);
            var temporary = ReportPath + ".tmp";
            File.WriteAllText(temporary, report, Utf8WithoutBom);
            File.Move(temporary, ReportPath, true);
        }
    }

    public string LoadLastReport()
    {
        lock (_gate)
        {
            return File.Exists(ReportPath) ? File.ReadAllText(ReportPath) : string.Empty;
        }
    }

    public static string Format(string source, Exception exception, DateTimeOffset utc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(exception);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Kandoo crash report\nUTC: {utc:O}\nSource: {source}\n\n{exception}\n");
    }
}
