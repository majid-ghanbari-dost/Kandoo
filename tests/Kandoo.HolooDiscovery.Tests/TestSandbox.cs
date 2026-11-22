using System.Text.Json;

namespace Kandoo.HolooDiscovery.Tests;

internal sealed class TestSandbox : IDisposable
{
    public TestSandbox()
    {
        Root = Path.Combine(Path.GetTempPath(), "kandoo-c52-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string CreateFixture()
    {
        var install = Path.Combine(Root, "installed", "HolooSample");
        var data = Path.Combine(install, "data");
        var backup = Path.Combine(Root, "backup");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(backup);
        File.WriteAllText(Path.Combine(Root, "programs.json"), JsonSerializer.Serialize(new[]
        {
            new { name = "هلو نمونه", install_folder = "installed/HolooSample", version = "0.0-demo" }
        }));
        File.WriteAllText(Path.Combine(Root, "sql-instances.json"), JsonSerializer.Serialize(new[] { "HOLOOSAMPLE" }));
        File.WriteAllBytes(Path.Combine(data, "sample.mdb"), [0x4E, 0x4F, 0x54, 0x2D, 0x41, 0x2D, 0x44, 0x42]);
        File.WriteAllBytes(Path.Combine(data, "ignore.txt"), [0x01]);
        var magic = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x01, 0x00, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09 };
        File.WriteAllBytes(Path.Combine(backup, "sample-backup.zip"), magic);
        return Root;
    }

    public void Dispose()
    {
        if (Directory.Exists(Root)) Directory.Delete(Root, true);
    }
}
