using System.Windows.Forms;
using Kandoo.App.Launcher;

// CI smoke hook: `Kandoo.SetupLauncher.exe --smoke [outputFile]` proves the host/runtime starts
// (no UI, no Hub). It writes one line to the file and exits.
if (args.Length > 0 && string.Equals(args[0], "--smoke", StringComparison.OrdinalIgnoreCase))
{
    var smokePath = args.Length > 1
        ? args[1]
        : Path.Combine(Path.GetTempPath(), "kandoo-launcher-smoke.txt");
    File.WriteAllText(
        smokePath,
        "KANDOO_LAUNCHER_OK runtime=" + Environment.Version + " dir=" + Path.GetDirectoryName(Environment.ProcessPath));
    return;
}

Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);
Application.Run(new MainForm(new LocalProfileStore()));
