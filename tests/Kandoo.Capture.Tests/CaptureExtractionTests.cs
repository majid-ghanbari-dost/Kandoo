using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Kandoo.Capture.Tests;

public sealed class CaptureExtractionTests
{
    [Fact]
    public void C19_SyntheticOpenXpsGlyphsReconstructTextAndPersianOrder()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("Documents/1/Pages/1.fpage");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write("""
                <FixedPage xmlns="http://schemas.openxps.org/oxps/v1.0">
                  <Glyphs OriginX="10" OriginY="10" UnicodeString="ید" />
                  <Glyphs OriginX="10" OriginY="30" UnicodeString="ABC" />
                </FixedPage>
                """);
        }

        stream.Position = 0;
        var text = new XpsTextExtractor().ExtractText(stream);
        Assert.Equal("دی" + Environment.NewLine + "ABC", text);
    }

    [Fact]
    public void C19_SyntheticEscPosRasterReconstructsPixelsAndWritesPng()
    {
        var data = new byte[] { 0x1D, 0x76, 0x30, 0, 1, 0, 2, 0, 0x80, 0x01 };
        var image = new EscPosRasterExtractor().Extract(data);
        Assert.Equal(8, image.Width);
        Assert.Equal(2, image.Height);
        Assert.True(image.IsBlack(0, 0));
        Assert.True(image.IsBlack(7, 1));
        Assert.False(image.IsBlack(1, 0));

        using var png = new MemoryStream();
        image.WritePng(png);
        var bytes = png.ToArray();
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes[..8]);
        Assert.Equal(new byte[] { 0, 0, 0, 8 }, bytes[16..20]);
        Assert.Equal(new byte[] { 0, 0, 0, 2 }, bytes[20..24]);
    }

    [Fact]
    public async Task C19_SyntheticSpoolFilesRequireStabilityAndProduceHashedReviewArtifacts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kandoo-c19", Guid.NewGuid().ToString("N"));
        var spool = Path.Combine(directory, "spool");
        var captures = Path.Combine(directory, "captures");
        Directory.CreateDirectory(spool);
        try
        {
            var splBytes = new byte[] { 0x1B, 0x40 };
            await File.WriteAllBytesAsync(Path.Combine(spool, "00001.SPL"), splBytes);
            await File.WriteAllBytesAsync(Path.Combine(spool, "00001.SHD"), new byte[] { 1, 2, 3 });
            var sink = new RecordingSink();
            var tracker = new SpoolFileTracker(new PrintCaptureOptions
            {
                SpoolDirectory = spool,
                CaptureDirectory = captures,
                StableScans = 2
            }, sink);

            await tracker.ScanOnceAsync();
            Assert.Empty(sink.Artifacts);
            await tracker.ScanOnceAsync();
            Assert.Equal(2, sink.Artifacts.Count);
            await tracker.ScanOnceAsync();
            Assert.Equal(2, sink.Artifacts.Count);

            var spl = sink.Artifacts.Single(artifact => artifact.Extension == ".spl");
            Assert.Equal(Convert.ToHexString(SHA256.HashData(splBytes)).ToLowerInvariant(), spl.Sha256);
            Assert.True(File.Exists(spl.CapturedPath));
            Assert.Equal(splBytes, await File.ReadAllBytesAsync(spl.CapturedPath));
            Assert.All(sink.Artifacts, artifact => Assert.Equal(CaptureDisposition.Review, artifact.Disposition));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task C19_StartupBaselineSuppressesExistingSyntheticFilesUntilTheyChange()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kandoo-c19-baseline", Guid.NewGuid().ToString("N"));
        var spool = Path.Combine(directory, "spool");
        Directory.CreateDirectory(spool);
        try
        {
            var source = Path.Combine(spool, "existing.SPL");
            await File.WriteAllBytesAsync(source, new byte[] { 1 });
            var sink = new RecordingSink();
            var tracker = new SpoolFileTracker(new PrintCaptureOptions
            {
                SpoolDirectory = spool,
                CaptureDirectory = Path.Combine(directory, "captures"),
                StableScans = 2
            }, sink);
            tracker.IgnoreExisting();
            await tracker.ScanOnceAsync();
            await tracker.ScanOnceAsync();
            Assert.Empty(sink.Artifacts);

            await File.WriteAllBytesAsync(source, new byte[] { 1, 2 });
            await tracker.ScanOnceAsync();
            Assert.Empty(sink.Artifacts);
            await tracker.ScanOnceAsync();
            Assert.Single(sink.Artifacts);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact(Skip = "BLOCKED: owner-provided real SPL capture samples are absent; no real-capture input is fabricated.")]
    public void C19_BLOCKED_RealSplCaptureCompatibility()
    {
    }

    [Fact(Skip = "BLOCKED: owner-provided real SHD capture samples are absent; no real-capture input is fabricated.")]
    public void C19_BLOCKED_RealShdCaptureCompatibility()
    {
    }

    private sealed record RecordedArtifact(CapturedSpoolArtifact Artifact, CaptureDisposition Disposition)
    {
        public string Extension => Artifact.Extension;
        public string Sha256 => Artifact.Sha256;
        public string CapturedPath => Artifact.CapturedPath;
    }

    private sealed class RecordingSink : ICapturedSpoolSink
    {
        public List<RecordedArtifact> Artifacts { get; } = new();

        public Task<CaptureDisposition> AcceptAsync(CapturedSpoolArtifact artifact, CancellationToken cancellationToken = default)
        {
            Artifacts.Add(new RecordedArtifact(artifact, CaptureDisposition.Review));
            return Task.FromResult(CaptureDisposition.Review);
        }
    }
}
