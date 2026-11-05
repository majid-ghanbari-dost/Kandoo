using System.Globalization;
using System.Text;

namespace Kandoo.HolooDiscovery;

public static class ReportWriter
{
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "kandoo-c52-report.txt");

    public static string Render(DiscoveryResult? discovery, BackupInspection? backup)
    {
        var text = new StringBuilder();
        text.AppendLine("Kandoo C-52 read-only Holoo discovery report");
        text.AppendLine("No SQL connections, queries, network access, or database-content reads were performed.");
        if (discovery is not null)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Mode: {discovery.Mode}");
            text.AppendLine("Installed programs:");
            foreach (var program in discovery.Programs)
            {
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"- name={program.Name} | version={program.Version} | install_folder={program.InstallFolder}");
            }

            text.AppendLine("Install folders:");
            foreach (var folder in discovery.InstallFolders)
            {
                text.AppendLine($"- {folder}");
            }

            text.AppendLine("Candidate database files (metadata only):");
            foreach (var file in discovery.CandidateDatabases)
            {
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"- path={file.Path} | size_bytes={file.SizeBytes} | last_write_utc={file.LastWriteUtc:O}");
            }

            text.AppendLine("Local SQL Server instance names only:");
            foreach (var name in discovery.SqlInstanceNames)
            {
                text.AppendLine($"- {name}");
            }
        }

        if (backup is not null)
        {
            text.AppendLine("User-selected backup inspection:");
            text.AppendLine($"- path={backup.Path}");
            text.AppendLine(CultureInfo.InvariantCulture, $"- size_bytes={backup.SizeBytes}");
            text.AppendLine($"- magic_hex={backup.MagicHex}");
            text.AppendLine($"- compression_guess={backup.CompressionGuess}");
            text.AppendLine($"- encryption_guess={backup.EncryptionGuess}");
            text.AppendLine("Guesses use at most the first 16 bytes and are not proof of format or encryption.");
        }

        return text.ToString();
    }

    public static void Write(string path, DiscoveryResult? discovery, BackupInspection? backup)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(Path.GetDirectoryName(fullPath)))
        {
            throw new DirectoryNotFoundException("The selected report directory must already exist.");
        }

        File.WriteAllText(fullPath, Render(discovery, backup), new UTF8Encoding(false));
    }
}
