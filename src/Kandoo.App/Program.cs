using System.Diagnostics;

namespace Kandoo.App;

/// <summary>Application entry point. Handles --self-check before any UI is created.</summary>
public static class Program
{
    /// <summary>Runs the headless self-check: resolves Hub, starts it, performs owner-session call.</summary>
    /// <param name="args">--self-check [resultFile]. If resultFile is given, the result line is
    /// also written to that file.</param>
    /// <returns>0 on success, 1 on failure.</returns>
    public static int Main(string[] args)
    {
        if (args.Length >= 1 && args[0] == "--self-check")
        {
            return RunSelfCheck(args.Length >= 2 ? args[1] : null);
        }

        // Normal MAUI startup.
        return MauiProgram.CreateMauiApp() is not null ? 0 : 1;
    }

    private static int RunSelfCheck(string? resultFile)
    {
        try
        {
            // Resolve Hub path using HubLocator.
            var appFolder = Path.GetDirectoryName(Environment.ProcessPath) ?? string.Empty;
            var hubExe = HubLocator.ResolveHubExe(appFolder);
            var hubDir = Path.GetDirectoryName(hubExe) ?? string.Empty;

            // Generate bootstrap proof and pick a free port.
            var bootstrapProof = Guid.NewGuid().ToString("D");
            var hubPort = GetFreePort();
            var hubBaseUrl = $"http://localhost:{hubPort}";

            // Start Hub.exe.
            var startInfo = new ProcessStartInfo
            {
                FileName = hubExe,
                WorkingDirectory = hubDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.EnvironmentVariables["Hub__OwnerBootstrapProof"] = bootstrapProof;
            startInfo.EnvironmentVariables["ASPNETCORE_URLS"] = hubBaseUrl;

            using var hubProcess = Process.Start(startInfo);
            if (hubProcess is null)
            {
                var fail = "KANDOO_SETUP_FAIL Kandoo.Hub.exe را راه‌اندازی نکرد.";
                WriteResult(resultFile, fail);
                Console.WriteLine(fail);
                return 1;
            }

            // Wait for Hub to become available.
            using var httpClient = new System.Net.Http.HttpClient
            {
                BaseAddress = new System.Uri(hubBaseUrl + "/")
            };
            var deadline = DateTimeOffset.UtcNow.Add(TimeSpan.FromSeconds(30));
            while (DateTimeOffset.UtcNow < deadline)
            {
                try
                {
                    using var health = httpClient.GetAsync("/health").GetAwaiter().GetResult();
                    if (health.IsSuccessStatusCode)
                    {
                        break;
                    }
                }
                catch { /* Hub not yet available; keep waiting. */ }
                Thread.Sleep(500);
            }

            // Perform owner-session call.
            var storeId = Guid.NewGuid();
            var hubDeviceId = Guid.NewGuid();
            var ownerUserId = Guid.NewGuid();

            var ownerSessionBody = new
            {
                hub_device_id = hubDeviceId,
                owner_user_id = ownerUserId,
                bootstrap_proof = bootstrapProof
            };
            using var request = new System.Net.Http.HttpRequestMessage(
                System.Net.Http.HttpMethod.Post, $"/v1/stores/{storeId}/owner-sessions");
            request.Content = new System.Net.StringContent(
                System.Text.Json.JsonSerializer.Serialize(ownerSessionBody),
                System.Text.Encoding.UTF8,
                "application/json");

            using var response = httpClient.SendAsync(request).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var fail = $"KANDOO_SETUP_FAIL نشست مالک ایجاد نشد (HTTP {(int)response.StatusCode}): {body}";
                WriteResult(resultFile, fail);
                Console.WriteLine(fail);
                return 1;
            }

            var ok = $"KANDOO_SETUP_OK port={hubPort}";
            WriteResult(resultFile, ok);
            Console.WriteLine(ok);
            return 0;
        }
        catch (Exception exception)
        {
            var fail = $"KANDOO_SETUP_FAIL {exception.Message}";
            WriteResult(resultFile, fail);
            Console.WriteLine(fail);
            return 1;
        }
    }

    private static void WriteResult(string? resultFile, string line)
    {
        Console.WriteLine(line);
        if (!string.IsNullOrEmpty(resultFile))
        {
            try
            {
                File.WriteAllText(resultFile, line);
            }
            catch
            {
                // Best-effort; stdout is the primary channel.
            }
        }
    }

    private static int GetFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(
            System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
