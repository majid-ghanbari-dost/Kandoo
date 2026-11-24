using System.IO;
using Kandoo.App.Logic;
using Xunit;

namespace Kandoo.App.Tests;

public sealed class HubLocatorTests
{
    [Fact]
    public void ResolveHubExe_InstallLayout_FindsHubInParentHubFolder()
    {
        using var temp = new TempDirectory();
        temp.CreateFolder("app");
        temp.CreateFolder("hub");
        File.WriteAllText(temp.Combine("hub", "Kandoo.Hub.exe"), "dummy");

        var result = HubLocator.ResolveHubExe(temp.Combine("app"));

        Assert.True(File.Exists(result));
    }

    [Fact]
    public void ResolveHubExe_DevLayout_FindsHubInSiblingHubFolder()
    {
        using var temp = new TempDirectory();
        temp.CreateFolder("app");
        temp.CreateFolder("hub");
        File.WriteAllText(temp.Combine("hub", "Kandoo.Hub.exe"), "dummy");

        // Dev layout: hub at .\hub\ relative to app folder root
        var result = HubLocator.ResolveHubExe(temp.Root);

        Assert.True(File.Exists(result));
    }

    [Fact]
    public void ResolveHubExe_InstallLayout_PrefersParentHubOverDevHub()
    {
        using var temp = new TempDirectory();
        temp.CreateFolder("app");
        temp.CreateFolder("hub");
        File.WriteAllText(temp.Combine("hub", "Kandoo.Hub.exe"), "install-layout-hub");

        // When app is in a subfolder, ..\hub\ should win over .\hub\
        var result = HubLocator.ResolveHubExe(temp.Combine("app"));

        Assert.True(File.Exists(result));
        Assert.Contains("hub" + Path.DirectorySeparatorChar + "Kandoo.Hub.exe", result);
    }

    [Fact]
    public void ResolveHubExe_NotFound_ListsAllCheckedPaths()
    {
        using var temp = new TempDirectory();
        temp.CreateFolder("app");

        var ex = Assert.Throws<FileNotFoundException>(() =>
            HubLocator.ResolveHubExe(temp.Combine("app")));

        Assert.Contains("فایل Kandoo.Hub.exe در پوشه نصب یافت نشد", ex.Message);
        Assert.Contains("مسیرهای بررسی‌شده:", ex.Message);
        // Both install and dev paths should be listed
        var installPath = Path.GetFullPath(Path.Combine(temp.Combine("app"), "..", "hub", "Kandoo.Hub.exe"));
        var devPath = Path.GetFullPath(Path.Combine(temp.Combine("app"), "hub", "Kandoo.Hub.exe"));
        Assert.Contains(installPath, ex.Message);
        Assert.Contains(devPath, ex.Message);
    }

    private sealed class TempDirectory : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"kandoo-test-{Guid.NewGuid():N}");

        public string Root => _root;

        public TempDirectory()
        {
            Directory.CreateDirectory(_root);
        }

        public void CreateFolder(string relativePath)
        {
            Directory.CreateDirectory(Path.Combine(_root, relativePath));
        }

        public string Combine(params string[] parts) => Path.Combine(_root, Path.Combine(parts));

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        }
    }
}
