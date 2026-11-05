using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Kandoo.HolooDiscovery;

[SupportedOSPlatform("windows")]
public static class WindowsLiveProbe
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static DiscoveryResult Discover()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Live registry discovery is Windows-only.");
        }

        var programs = ReadPrograms();
        return DiscoveryScanner.Scan("live-consent-declared", programs, ReadSqlInstanceNames());
    }

    private static IReadOnlyList<InstalledProgram> ReadPrograms()
    {
        var rows = new List<InstalledProgram>();
        ReadUninstallView(RegistryHive.LocalMachine, RegistryView.Registry64, rows);
        ReadUninstallView(RegistryHive.LocalMachine, RegistryView.Registry32, rows);
        ReadUninstallView(RegistryHive.CurrentUser, RegistryView.Default, rows);
        return rows
            .DistinctBy(row => (row.Name, row.InstallFolder))
            .ToArray();
    }

    private static void ReadUninstallView(
        RegistryHive hive,
        RegistryView view,
        ICollection<InstalledProgram> rows)
    {
        using var root = RegistryKey.OpenBaseKey(hive, view);
        using var uninstall = root.OpenSubKey(UninstallPath, writable: false);
        if (uninstall is null) return;
        foreach (var keyName in uninstall.GetSubKeyNames())
        {
            using var key = uninstall.OpenSubKey(keyName, writable: false);
            var name = key?.GetValue("DisplayName")?.ToString();
            if (!IsHolooName(name)) continue;
            var installFolder = key?.GetValue("InstallLocation")?.ToString()?.Trim() ?? string.Empty;
            if (installFolder.Length == 0)
            {
                installFolder = FolderFromDisplayIcon(key?.GetValue("DisplayIcon")?.ToString());
            }

            rows.Add(new InstalledProgram(
                name!.Trim(),
                installFolder,
                key?.GetValue("DisplayVersion")?.ToString()?.Trim() ?? string.Empty));
        }
    }

    private static IReadOnlyList<string> ReadSqlInstanceNames()
    {
        var names = new List<string>();
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using (var instanceNames = root.OpenSubKey(
                   @"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL", writable: false))
        {
            if (instanceNames is not null) names.AddRange(instanceNames.GetValueNames());
        }

        using var product = root.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft SQL Server", writable: false);
        if (product?.GetValue("InstalledInstances") is string[] installed) names.AddRange(installed);
        return names;
    }

    private static bool IsHolooName(string? name) =>
        name?.Contains("holoo", StringComparison.OrdinalIgnoreCase) == true ||
        name?.Contains("هلو", StringComparison.Ordinal) == true;

    private static string FolderFromDisplayIcon(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon)) return string.Empty;
        var path = displayIcon.Trim().Trim('"');
        var comma = path.LastIndexOf(',');
        if (comma > 1) path = path[..comma];
        try
        {
            return Directory.Exists(path)
                ? Path.GetFullPath(path)
                : Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }
    }
}
