namespace Kandoo.App.Logic;

/// <summary>Locates the Kandoo.Hub.exe executable relative to the app folder.</summary>
/// <remarks>
/// Install layout (C-95): {install}\app\Kandoo.App.exe, Hub at {install}\hub\Kandoo.Hub.exe.
/// Dev layout: app and Hub side by side, Hub at .\hub\Kandoo.Hub.exe relative to app folder.
/// </remarks>
public static class HubLocator
{
    private const string HubExeName = "Kandoo.Hub.exe";

    /// <summary>Resolves the full path to Kandoo.Hub.exe, checking install and dev layouts.</summary>
    /// <exception cref="FileNotFoundException">Includes every path that was checked.</exception>
    public static string ResolveHubExe(string appFolder)
    {
        var checkedPaths = new List<string>();

        // Install layout: app in {install}\app\, Hub in {install}\hub\
        var installHub = Path.Combine(appFolder, "..", "hub", HubExeName);
        var installHubFull = Path.GetFullPath(installHub);
        checkedPaths.Add(installHubFull);
        if (File.Exists(installHubFull))
        {
            return installHubFull;
        }

        // Dev layout: Hub in .\hub\ relative to app folder
        var devHub = Path.Combine(appFolder, "hub", HubExeName);
        var devHubFull = Path.GetFullPath(devHub);
        checkedPaths.Add(devHubFull);
        if (File.Exists(devHubFull))
        {
            return devHubFull;
        }

        var message = "فایل Kandoo.Hub.exe در پوشه نصب یافت نشد. مسیرهای بررسی‌شده: " +
            string.Join("; ", checkedPaths);
        throw new FileNotFoundException(message);
    }
}
