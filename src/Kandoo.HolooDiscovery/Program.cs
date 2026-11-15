using Kandoo.HolooDiscovery;

return ConsoleApp.Run(args, Console.Out, Console.Error);

namespace Kandoo.HolooDiscovery
{
    public static class ConsoleApp
    {
        public static int Run(string[] args, TextWriter output, TextWriter error)
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            {
                WriteHelp(output);
                return 0;
            }

            try
            {
                var options = ParseOptions(args[1..]);
                var report = options.TryGetValue("report", out var selectedReport)
                    ? selectedReport
                    : ReportWriter.DefaultPath;
                DiscoveryResult? discovery = null;
                BackupInspection? backup = null;
                switch (args[0])
                {
                    case "demo":
                        RequireOnly(options, "sandbox", "report");
                        var sandbox = Required(options, "sandbox");
                        discovery = SyntheticProbe.Discover(sandbox);
                        backup = BackupInspector.Inspect(SyntheticProbe.BackupPath(sandbox));
                        break;
                    case "discover":
                        RequireLiveConsent(options);
                        RequireOnly(options, "live", "manager-consent-granted", "report", "backup");
                        discovery = DiscoverLive();
                        if (options.TryGetValue("backup", out var discoveredBackup))
                        {
                            backup = BackupInspector.Inspect(discoveredBackup);
                        }
                        break;
                    case "inspect":
                        RequireLiveConsent(options);
                        RequireOnly(options, "live", "manager-consent-granted", "report", "file");
                        backup = BackupInspector.Inspect(Required(options, "file"));
                        break;
                    default:
                        throw new ArgumentException($"Unknown command: {args[0]}");
                }

                ReportWriter.Write(report, discovery, backup);
                output.WriteLine($"C52 PASS mode={discovery?.Mode ?? "backup-inspection"} report={Path.GetFullPath(report)}");
                if (backup is not null)
                {
                    output.WriteLine($"magic_hex={backup.MagicHex} compression_guess={backup.CompressionGuess} encryption_guess={backup.EncryptionGuess}");
                }

                return 0;
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or
                UnauthorizedAccessException or InvalidOperationException or NotSupportedException or
                System.Security.SecurityException or System.Text.Json.JsonException)
            {
                error.WriteLine($"C52 ERROR {exception.Message}");
                error.WriteLine("Report was not completed. Live local-machine use requires --live --manager-consent-granted.");
                return 2;
            }
        }

        private static Dictionary<string, string> ParseOptions(string[] args)
        {
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < args.Length; index++)
            {
                var name = args[index];
                if (!name.StartsWith("--", StringComparison.Ordinal) || name.Length == 2)
                {
                    throw new ArgumentException($"Unexpected argument: {name}");
                }

                name = name[2..];
                if (name is "live" or "manager-consent-granted")
                {
                    options.Add(name, string.Empty);
                    continue;
                }

                if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                {
                    throw new ArgumentException($"Option --{name} requires a value.");
                }

                options.Add(name, args[index]);
            }

            return options;
        }

        private static DiscoveryResult DiscoverLive()
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("Live registry discovery is Windows-only.");
            }

            return WindowsLiveProbe.Discover();
        }

        private static void RequireLiveConsent(IReadOnlyDictionary<string, string> options)
        {
            if (!options.ContainsKey("live") || !options.ContainsKey("manager-consent-granted"))
            {
                throw new InvalidOperationException(
                    "Live local-machine access is blocked unless both --live and --manager-consent-granted are present.");
            }
        }

        private static string Required(IReadOnlyDictionary<string, string> options, string name) =>
            options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new ArgumentException($"Option --{name} is required.");

        private static void RequireOnly(IReadOnlyDictionary<string, string> options, params string[] allowed)
        {
            var unknown = options.Keys.FirstOrDefault(key => !allowed.Contains(key, StringComparer.Ordinal));
            if (unknown is not null) throw new ArgumentException($"Option --{unknown} is not valid for this command.");
        }

        private static void WriteHelp(TextWriter output)
        {
            output.WriteLine("Kandoo C-52 — read-only, network-free Holoo discovery");
            output.WriteLine("Synthetic: demo --sandbox <fixture-dir> [--report <path>]");
            output.WriteLine("Live discovery: discover --live --manager-consent-granted [--backup <file>] [--report <path>]");
            output.WriteLine("User-selected backup: inspect --file <path> --live --manager-consent-granted [--report <path>]");
            output.WriteLine($"Default report: {ReportWriter.DefaultPath}");
            output.WriteLine("Live mode is not authorized by this repository; PM must relay explicit manager consent.");
        }
    }
}
