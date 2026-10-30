using System.Security.Cryptography;
using System.Text.Json;
using Kandoo.Capture;
using Xunit;

namespace Kandoo.Hub.Tests;

public sealed class PrintCaptureWiringTests
{
    [Fact]
    public async Task C19_HubSinkPersistsReviewOnlyManifestWithoutNativeEffects()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kandoo-c19-hub", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var bytes = new byte[] { 0x1B, 0x40 };
            var capture = Path.Combine(directory, "synthetic.spl");
            await File.WriteAllBytesAsync(capture, bytes);
            var artifact = new CapturedSpoolArtifact(
                "synthetic-source.spl",
                capture,
                ".spl",
                bytes.LongLength,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                new DateTimeOffset(2026, 10, 29, 11, 0, 0, TimeSpan.Zero));
            var sink = new HubCaptureSink();

            Assert.Equal(CaptureDisposition.Review, await sink.AcceptAsync(artifact));
            Assert.Equal(CaptureDisposition.Review, await sink.AcceptAsync(artifact));
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(capture + ".review.json"));
            Assert.Equal("REVIEW", manifest.RootElement.GetProperty("status").GetString());
            Assert.Equal("NOT_CANONICALIZED", manifest.RootElement.GetProperty("canonical_invoice_status").GetString());
            Assert.False(manifest.RootElement.GetProperty("native_effects").GetBoolean());
            Assert.Equal(artifact.Sha256, manifest.RootElement.GetProperty("capture_sha256").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
