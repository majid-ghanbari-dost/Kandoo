namespace Kandoo.HolooDiscovery;

public static class DiscoveryScanner
{
    private static readonly HashSet<string> DatabaseExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mdb", ".accdb", ".mdf", ".fdb", ".db", ".dbf", ".sqlite"
    };

    public static DiscoveryResult Scan(
        string mode,
        IEnumerable<InstalledProgram> programs,
        IEnumerable<string> sqlInstanceNames)
    {
        var programRows = programs
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.InstallFolder, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var folders = programRows
            .Select(row => row.InstallFolder)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var databases = new List<CandidateDatabase>();
        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                MatchCasing = MatchCasing.PlatformDefault
            };
            foreach (var path in Directory.EnumerateFiles(folder, "*", options))
            {
                if (!DatabaseExtensions.Contains(Path.GetExtension(path)))
                {
                    continue;
                }

                var metadata = new FileInfo(path);
                if ((metadata.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                databases.Add(new CandidateDatabase(
                    metadata.FullName,
                    metadata.Length,
                    metadata.LastWriteTimeUtc));
            }
        }

        return new DiscoveryResult(
            mode,
            programRows,
            folders,
            databases.OrderBy(row => row.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
            sqlInstanceNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }
}
