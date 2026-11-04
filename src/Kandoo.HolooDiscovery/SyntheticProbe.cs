using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kandoo.HolooDiscovery;

public static class SyntheticProbe
{
    public static DiscoveryResult Discover(string sandbox)
    {
        var root = Path.GetFullPath(sandbox);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException("The synthetic sandbox does not exist.");
        }

        var programs = JsonSerializer.Deserialize<List<SyntheticProgramRow>>(
            File.ReadAllText(SyntheticPath(root, "programs.json"))) ?? [];
        var sqlNames = JsonSerializer.Deserialize<List<string>>(
            File.ReadAllText(SyntheticPath(root, "sql-instances.json"))) ?? [];
        var mapped = programs.Select(row =>
        {
            if (string.IsNullOrWhiteSpace(row.Name) || string.IsNullOrWhiteSpace(row.InstallFolder))
            {
                throw new FormatException("Synthetic program rows require name and install_folder.");
            }

            var folder = Path.GetFullPath(Path.Combine(root, row.InstallFolder));
            EnsureSafeSyntheticPath(root, folder);
            return new InstalledProgram(row.Name, folder, row.Version ?? "demo");
        });
        return DiscoveryScanner.Scan("synthetic", mapped, sqlNames);
    }

    public static string BackupPath(string sandbox)
    {
        var root = Path.GetFullPath(sandbox);
        var path = Path.GetFullPath(Path.Combine(root, "backup", "sample-backup.zip"));
        EnsureSafeSyntheticPath(root, path);
        return path;
    }

    private static string SyntheticPath(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        EnsureSafeSyntheticPath(root, path);
        return path;
    }

    private static void EnsureSafeSyntheticPath(string root, string path)
    {
        if (!IsWithin(root, path))
        {
            throw new InvalidOperationException("Synthetic paths must stay inside the sandbox.");
        }

        var current = root;
        if (IsReparsePoint(current))
        {
            throw new InvalidOperationException("Synthetic sandbox must not be a symlink or reparse point.");
        }

        foreach (var part in Path.GetRelativePath(root, path).Split(
                     Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (IsReparsePoint(current))
            {
                throw new InvalidOperationException("Synthetic paths must not contain symlinks or reparse points.");
            }
        }
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool IsWithin(string root, string path)
    {
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.Ordinal);
    }

    private sealed record SyntheticProgramRow(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("install_folder")] string InstallFolder,
        [property: JsonPropertyName("version")] string? Version);
}
