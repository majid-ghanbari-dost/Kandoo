using Xunit;

namespace Kandoo.HolooDiscovery.Tests;

public sealed class DiscoveryAndConsoleTests
{
    [Fact]
    public void SyntheticDemoDiscoversMetadataAndWritesExactlyOneReport()
    {
        using var sandbox = new TestSandbox();
        var root = sandbox.CreateFixture();
        var reportDirectory = Path.Combine(sandbox.Root, "reports");
        Directory.CreateDirectory(reportDirectory);
        var report = Path.Combine(reportDirectory, "selected.txt");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = ConsoleApp.Run(["demo", "--sandbox", root, "--report", report], output, error);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Single(Directory.GetFiles(reportDirectory));
        var text = File.ReadAllText(report);
        Assert.Contains("هلو نمونه", text);
        Assert.Contains("sample.mdb", text);
        Assert.Contains("size_bytes=8", text);
        Assert.Contains("HOLOOSAMPLE", text);
        Assert.Contains("magic_hex=504B0304000001000203040506070809", text);
        Assert.DoesNotContain("ignore.txt", text);
    }

    [Fact]
    public void LiveBackupWithoutConsentIsRejectedBeforeReadingOrWriting()
    {
        using var sandbox = new TestSandbox();
        var report = Path.Combine(sandbox.Root, "must-not-exist.txt");
        var missingBackup = Path.Combine(sandbox.Root, "real-look-alike.bak");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = ConsoleApp.Run(["inspect", "--file", missingBackup, "--report", report], output, error);

        Assert.Equal(2, exit);
        Assert.False(File.Exists(report));
        Assert.Contains("blocked", error.ToString());
    }

    [Fact]
    public void SyntheticInstallFolderCannotEscapeSandbox()
    {
        using var sandbox = new TestSandbox();
        var root = sandbox.CreateFixture();
        File.WriteAllText(Path.Combine(root, "programs.json"),
            "[{\"name\":\"هلو نمونه\",\"install_folder\":\"..\",\"version\":\"demo\"}]");
        var report = Path.Combine(sandbox.Root, "escape.txt");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = ConsoleApp.Run(["demo", "--sandbox", root, "--report", report], output, error);

        Assert.Equal(2, exit);
        Assert.False(File.Exists(report));
        Assert.Contains("inside the sandbox", error.ToString());
    }

    [Fact]
    public void DefaultReportIsBesideExecutable()
    {
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)),
            Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(ReportWriter.DefaultPath)!));
    }
}
