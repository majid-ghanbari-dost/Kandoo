using System.Security.Cryptography;
using System.Text.Json;
using Kandoo.Capture;

namespace Kandoo.Hub;

/// <summary>Persists raw-capture REVIEW manifests; no invoice/customer/sale is inferred.</summary>
public sealed class HubCaptureSink : ICapturedSpoolSink
{
    public async Task<CaptureDisposition> AcceptAsync(
        CapturedSpoolArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(artifact.CapturedPath) || artifact.Sha256.Length != 64)
        {
            throw new InvalidOperationException("CAPTURE_ARTIFACT_MISSING");
        }

        var bytes = await File.ReadAllBytesAsync(artifact.CapturedPath, cancellationToken);
        var actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (bytes.LongLength != artifact.Size || !string.Equals(actualHash, artifact.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("CAPTURE_ARTIFACT_HASH_MISMATCH");
        }

        var manifest = JsonSerializer.Serialize(new
        {
            schema_version = "0",
            status = "REVIEW",
            capture_sha256 = artifact.Sha256,
            capture_file = Path.GetFileName(artifact.CapturedPath),
            source_kind = artifact.Extension,
            size = artifact.Size,
            captured_at = artifact.CapturedAt,
            canonical_invoice_status = "NOT_CANONICALIZED",
            native_effects = false
        }, HubJson.Options);
        var path = artifact.CapturedPath + ".review.json";
        if (File.Exists(path))
        {
            using var existing = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
            var root = existing.RootElement;
            if (root.GetProperty("status").GetString() == "REVIEW" &&
                root.GetProperty("canonical_invoice_status").GetString() == "NOT_CANONICALIZED" &&
                !root.GetProperty("native_effects").GetBoolean() &&
                root.GetProperty("capture_sha256").GetString() == artifact.Sha256 &&
                root.GetProperty("capture_file").GetString() == Path.GetFileName(artifact.CapturedPath))
            {
                return CaptureDisposition.Review;
            }

            throw new InvalidOperationException("CAPTURE_REVIEW_CONFLICT");
        }

        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, manifest, cancellationToken);
            File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        return CaptureDisposition.Review;
    }
}

public sealed class PrintCaptureHostedService : BackgroundService
{
    private readonly PrintCaptureOptions _options;
    private readonly ICapturedSpoolSink _sink;

    public PrintCaptureHostedService(PrintCaptureOptions options, ICapturedSpoolSink sink)
    {
        _options = options;
        _sink = sink;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await new PrintSpoolMonitor(_options, _sink).RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal Host shutdown.
        }
    }
}
