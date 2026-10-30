namespace Kandoo.Capture;

public enum CaptureDisposition
{
    Review
}

/// <summary>A raw capture only. It is not a Canonical Invoice or native domain effect.</summary>
public sealed record CapturedSpoolArtifact(
    string SourcePath,
    string CapturedPath,
    string Extension,
    long Size,
    string Sha256,
    DateTimeOffset CapturedAt);

public interface ICapturedSpoolSink
{
    Task<CaptureDisposition> AcceptAsync(CapturedSpoolArtifact artifact, CancellationToken cancellationToken = default);
}

public sealed class PrintCaptureOptions
{
    public bool Enabled { get; set; }
    public string SpoolDirectory { get; set; } = @"C:\Windows\System32\spool\PRINTERS";
    public string CaptureDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "captures");
    public int PollIntervalMilliseconds { get; set; } = 250;
    public int StableScans { get; set; } = 2;
    public int MaxCaptureBytes { get; set; } = 16 * 1024 * 1024;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SpoolDirectory) || string.IsNullOrWhiteSpace(CaptureDirectory) ||
            PollIntervalMilliseconds <= 0 || StableScans < 2 || MaxCaptureBytes <= 0)
        {
            throw new ArgumentException("Capture directories, a positive poll interval/size, and at least two stable scans are required.");
        }
    }
}
