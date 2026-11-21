using System.Text.Json;

namespace Kandoo.Hub;

public static class ExtractionBridgeContract
{
    public const string ContractId = "e30.extraction-bridge.v1";
    public const string SourceCommit = "29d842178cd39d3f54f14f3716c44c585911467b";
    public const string SchemaId = "urn:kandoo:extraction-bridge:capture-folder-result:1";
    public const string SchemaSha256 = "41964e49807d52eca80860d4c0ac88af7f04141679a6336391e7da9561eb94c0";
    public const string CaptureFolderGoldenSha256 = "336f33fab4997da2963d72c0a07d3589cf0baeb3d61f418042f596748640219e";
    public const int CaptureFolderGoldenBytes = 14_075;
    public static readonly TimeSpan MaximumTimeout = TimeSpan.FromSeconds(180);
}

public enum CaptureFolderBridgeDisposition
{
    Completed,
    Review,
    Blocked,
    Error
}

public sealed record ExtractorBridgeOptions
{
    public string ExecutablePath { get; set; } = string.Empty;
    public bool PythonModuleMode { get; set; }
    public string WorkingDirectory { get; set; } = string.Empty;
    public TimeSpan Timeout { get; set; } = ExtractionBridgeContract.MaximumTimeout;
    public Dictionary<string, string> Environment { get; set; } = new(StringComparer.Ordinal);
}

public sealed record CaptureFolderExtractionRequest(
    string CaptureFolder,
    string? E05Bank = null,
    string? CapturedAt = null,
    string? Templates = null,
    string? DriftBaseline = null);

public sealed record ExtractorProcessInvocation(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment);

public sealed record ExtractorProcessRun(
    int? ExitCode,
    byte[] StandardOutput,
    byte[] StandardError,
    bool TimedOut = false);

public interface IExtractorProcessRunner
{
    Task<ExtractorProcessRun> RunAsync(
        ExtractorProcessInvocation invocation,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

public sealed record CaptureFolderExtractionResult(
    CaptureFolderBridgeDisposition Disposition,
    string Code,
    int? ExitCode,
    JsonElement? Document,
    byte[] StandardOutput,
    byte[] StandardError,
    string? Detail = null);
