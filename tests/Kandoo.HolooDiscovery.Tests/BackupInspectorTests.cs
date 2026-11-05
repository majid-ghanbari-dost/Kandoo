using Xunit;

namespace Kandoo.HolooDiscovery.Tests;

public sealed class BackupInspectorTests
{
    [Fact]
    public void ReadMagicNeverReadsMoreThanSixteenBytes()
    {
        using var stream = new CountingStream(new byte[4096]);
        var magic = BackupInspector.ReadMagic(stream);
        Assert.Equal(16, magic.Length);
        Assert.Equal(16, stream.BytesRead);
    }

    [Fact]
    public void ZipGuessUsesOnlyHeaderAndReportsEncryptionAsGuess()
    {
        var (compression, encryption) = BackupInspector.Guess(
            [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x01, 0x00, 0xAA, 0xBB, 0xCC, 0xDD, 0x11, 0x22, 0x33, 0x44]);
        Assert.Equal("likely-zip", compression);
        Assert.Equal("possible-zip-flag", encryption);
    }

    [Fact]
    public void InspectReportsSizeAndAtMostSixteenMagicBytes()
    {
        using var sandbox = new TestSandbox();
        var root = sandbox.CreateFixture();
        var result = BackupInspector.Inspect(SyntheticProbe.BackupPath(root));
        Assert.Equal(16, result.SizeBytes);
        Assert.Equal(32, result.MagicHex.Length);
        Assert.Equal("likely-zip", result.CompressionGuess);
        Assert.Equal("possible-zip-flag", result.EncryptionGuess);
    }

    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int BytesRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }
    }
}
