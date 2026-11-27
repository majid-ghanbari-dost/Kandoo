using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Kandoo.Capture;

public sealed record CaptureFolderBuildRequest(
    string BatchRootDirectory,
    string JobFolderName,
    string SpoolJobId,
    byte[] SplBytes,
    string SplFileName = "capture-job.spl",
    DateTimeOffset? CapturedAt = null,
    IReadOnlyList<byte[]>? ExplicitPagePngs = null,
    string? PrinterName = null,
    string? DocumentName = null,
    bool ExtractEscPosRasters = true,
    bool ExtractXpsText = true);

public sealed record CaptureFolderBuildResult(
    string FolderPath,
    string SplPath,
    string SplSha256,
    string ReportPath,
    IReadOnlyList<string> PagePngPaths,
    string? XpsTextPath,
    string? XpsText,
    CaptureIntakeInspection Inspection);

/// <summary>
/// Produces Kandoo-Digital-Invoice E-30 / E-73 compliant capture folders
/// (non-empty .spl, naturally ordered .png page(s), and UTF-8 Report.txt)
/// from raw spool captures or ESC/POS / OpenXPS payloads.
/// </summary>
public static class CaptureFolderBuilder
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static CaptureFolderBuildResult Build(CaptureFolderBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.BatchRootDirectory))
            throw new ArgumentException("Batch root directory is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.JobFolderName))
            throw new ArgumentException("Job folder name is required.", nameof(request));
        if (request.JobFolderName.Equals("Unassigned", StringComparison.OrdinalIgnoreCase) ||
            request.JobFolderName.Contains("..", StringComparison.Ordinal) ||
            request.JobFolderName.IndexOfAny(['/', '\\']) >= 0)
        {
            throw new ArgumentException("Job folder name cannot be 'Unassigned' or contain path separators.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.SpoolJobId))
            throw new ArgumentException("SpoolJobId is required.", nameof(request));
        if (request.SplBytes is null || request.SplBytes.Length == 0)
            throw new ArgumentException("SplBytes must be non-empty.", nameof(request));

        var batchRoot = Path.GetFullPath(request.BatchRootDirectory);
        var folderPath = Path.Combine(batchRoot, request.JobFolderName.Trim());
        Directory.CreateDirectory(folderPath);

        var safeSplFileName = Path.GetFileName(string.IsNullOrWhiteSpace(request.SplFileName) ? "capture-job.spl" : request.SplFileName.Trim());
        if (!safeSplFileName.EndsWith(".spl", StringComparison.OrdinalIgnoreCase))
            safeSplFileName += ".spl";

        var splPath = Path.Combine(folderPath, safeSplFileName);
        File.WriteAllBytes(splPath, request.SplBytes);
        var splSha256 = Convert.ToHexString(SHA256.HashData(request.SplBytes)).ToLowerInvariant();

        var pngPaths = new List<string>();
        if (request.ExplicitPagePngs is { Count: > 0 })
        {
            for (var i = 0; i < request.ExplicitPagePngs.Count; i++)
            {
                var pngBytes = request.ExplicitPagePngs[i];
                if (pngBytes is null || pngBytes.Length == 0)
                    continue;
                var pngPath = Path.Combine(folderPath, $"page_{i + 1:D3}.png");
                File.WriteAllBytes(pngPath, pngBytes);
                pngPaths.Add(pngPath);
            }
        }
        else if (request.ExtractEscPosRasters)
        {
            try
            {
                var pngPath = Path.Combine(folderPath, "page_001.png");
                new EscPosRasterExtractor().ExtractToPng(splPath, pngPath);
                pngPaths.Add(pngPath);
            }
            catch (InvalidDataException)
            {
                // Non-ESC/POS SPL (e.g. GDI/EMF/XPS without rendered PNG yet); inspection will report MISSING_PNG cleanly.
            }
        }

        string? xpsText = null;
        string? xpsTextPath = null;
        if (request.ExtractXpsText && LooksLikeZipArchive(request.SplBytes))
        {
            try
            {
                using var ms = new MemoryStream(request.SplBytes, writable: false);
                var extracted = new XpsTextExtractor().ExtractText(ms);
                if (!string.IsNullOrWhiteSpace(extracted))
                {
                    xpsText = extracted;
                    xpsTextPath = Path.Combine(folderPath, "xps_text.txt");
                    File.WriteAllText(xpsTextPath, xpsText + "\n", Utf8NoBom);
                }
            }
            catch (Exception)
            {
                // Ignore non-OpenXPS archives gracefully; folder intake validator stays fail-closed.
            }
        }

        var capturedAt = (request.CapturedAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var reportLines = new List<string>
        {
            $"Spool job ID: {request.SpoolJobId.Trim()}",
            $"Timestamp: {capturedAt.ToString("O", CultureInfo.InvariantCulture)}",
            $"Page count: {pngPaths.Count}",
            $"SPL SHA-256: {splSha256}",
        };
        if (!string.IsNullOrWhiteSpace(request.PrinterName))
            reportLines.Add($"Printer: {request.PrinterName.Trim()}");
        if (!string.IsNullOrWhiteSpace(request.DocumentName))
            reportLines.Add($"Document: {request.DocumentName.Trim()}");

        var reportPath = Path.Combine(folderPath, "Report.txt");
        File.WriteAllText(reportPath, string.Join("\n", reportLines) + "\n", Utf8NoBom);

        var inspection = CaptureIntakeValidator.InspectFolder(folderPath);
        return new CaptureFolderBuildResult(
            folderPath,
            splPath,
            splSha256,
            reportPath,
            pngPaths,
            xpsTextPath,
            xpsText,
            inspection);
    }

    public static async Task<CaptureFolderBuildResult> BuildFromSpoolArtifactAsync(
        CapturedSpoolArtifact artifact,
        string batchRootDirectory,
        IReadOnlyList<byte[]>? renderedPagePngs = null,
        string? jobFolderName = null,
        string? spoolJobId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        var splBytes = await File.ReadAllBytesAsync(artifact.CapturedPath, cancellationToken).ConfigureAwait(false);
        var effectiveFolder = string.IsNullOrWhiteSpace(jobFolderName)
            ? $"job_{artifact.Sha256[..Math.Min(16, artifact.Sha256.Length)]}"
            : jobFolderName.Trim();
        var effectiveJobId = string.IsNullOrWhiteSpace(spoolJobId)
            ? Path.GetFileNameWithoutExtension(artifact.SourcePath) + "-" + artifact.Sha256[..8]
            : spoolJobId.Trim();

        return Build(new CaptureFolderBuildRequest(
            batchRootDirectory,
            effectiveFolder,
            effectiveJobId,
            splBytes,
            Path.GetFileName(artifact.CapturedPath),
            artifact.CapturedAt,
            renderedPagePngs));
    }

    private static bool LooksLikeZipArchive(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 4 && bytes[0] == (byte)'P' && bytes[1] == (byte)'K' && bytes[2] == 0x03 && bytes[3] == 0x04;
}
