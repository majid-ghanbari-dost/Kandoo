namespace Kandoo.App;

using System.Diagnostics;
using System.ComponentModel;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Kandoo.App.Logic;

public partial class SetupWizardPage : ContentPage
{
    private readonly IUserProfileStore _profiles;
    private readonly SharedTokenFile _tokenFile = new();
    private HttpClient _http = new();
    private string? _lastError;
    private Process? _hubProcess;
    private string? _hubBaseUrl;
    private LocalUserProfile? _shopProfile;

    // C-95: Hub port is chosen dynamically per run (free localhost port)
    // so this run never conflicts with a Hub from a previous run.

    public SetupWizardPage(IUserProfileStore profiles)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        // _http.BaseAddress is set dynamically in ConfigureShopAsync after a
        // free port is chosen for this run.
        InitializeComponent();
        UpdateStepVisibility();
        StatusLabel.Text = "مرحله ۱: نام فروشگاه را وارد کنید.";
    }

    private void UpdateStepVisibility()
    {
        Step1Panel.IsVisible = true;
        Step2Panel.IsVisible = false;
        Step3Panel.IsVisible = false;

        Step1Label.TextColor = Colors.Black;
        Step2Label.TextColor = Colors.Gray;
        Step3Label.TextColor = Colors.Gray;
    }

    private void AdvanceToStep2()
    {
        Step1Panel.IsVisible = false;
        Step2Panel.IsVisible = true;
        Step3Panel.IsVisible = false;

        Step1Label.TextColor = Colors.Gray;
        Step2Label.TextColor = Colors.Black;
        Step3Label.TextColor = Colors.Gray;

        StatusLabel.Text = "مرحله ۲: تنظیمات خودکار فروشگاه در حال اجرا است…";
    }

    private void AdvanceToStep3()
    {
        Step1Panel.IsVisible = false;
        Step2Panel.IsVisible = false;
        Step3Panel.IsVisible = true;

        Step1Label.TextColor = Colors.Gray;
        Step2Label.TextColor = Colors.Gray;
        Step3Label.TextColor = Colors.Black;

        StatusLabel.Text = "مرحله ۳: فروشگاه آماده است. وارد شدن به برنامه…";
    }

    private async void OnStartShopClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(StoreNameEntry.Text))
        {
            StatusLabel.Text = "نام فروشگاه را وارد کنید.";
            CopyReportButton.IsVisible = false;
            return;
        }

        _lastError = null;
        CopyReportButton.IsVisible = false;
        StartShopButton.IsEnabled = false;
        AdvanceToStep2();

        try
        {
            await ConfigureShopAsync(StoreNameEntry.Text.Trim());
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _lastError = exception.Message;
            StatusLabel.Text = "خطا در راه‌اندازی خودکار: " + exception.Message;
            CopyReportButton.IsVisible = true;
            Step2Panel.IsVisible = false;
            Step1Panel.IsVisible = true;
            Step1Label.TextColor = Colors.Black;
            Step2Label.TextColor = Colors.Gray;
        }
        finally
        {
            StartShopButton.IsEnabled = true;
        }
    }

    private async Task ConfigureShopAsync(string storeName)
    {
        // C-95: Stop the Hub started by a previous run of this setup, if any.
        StopPreviousHub();

        // BaseAddress cannot be changed after an HttpClient has sent a request.
        // Each retry starts a new Hub on a new port, so it needs a fresh client.
        _http.Dispose();
        _http = new HttpClient();

        // C-95 + HOTFIX-1: Locate Hub.exe using HubLocator (install layout: ..\hub\,
        // dev layout: .\hub\). Error lists every path checked.
        var appDir = Path.GetDirectoryName(Environment.ProcessPath) ?? string.Empty;
        var hubExe = HubLocator.ResolveHubExe(appDir);
        // HOTFIX-1: the Hub must start in its OWN folder ({install}\hub or .\hub), not the app
        // folder. Deriving it from the resolved exe keeps both layouts correct.
        var hubDir = Path.GetDirectoryName(hubExe) ?? appDir;

        // C-95: Generate OwnerBootstrapProof so the First-Run Setup can configure
        // owner bootstrap without editing JSON or using PowerShell.
        var bootstrapProof = Guid.NewGuid().ToString("D");

        // C-95: Pick a free random localhost port so this run never binds to a
        // port already held by a Hub from an earlier click/test with a
        // different proof.
        var hubPort = GetFreePort();
        _hubBaseUrl = $"http://localhost:{hubPort}";
        _http.BaseAddress = new Uri(_hubBaseUrl + "/");

        // C-95: Start Hub.exe as a background process with OwnerBootstrapProof
        // passed via environment variable and ASPNETCORE_URLS on the chosen port.
        var startInfo = new ProcessStartInfo
        {
            FileName = hubExe,
            WorkingDirectory = hubDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            // Undrained redirected pipes can fill and block the Hub.
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };
        startInfo.EnvironmentVariables["Hub__OwnerBootstrapProof"] = bootstrapProof;
        startInfo.EnvironmentVariables["ASPNETCORE_URLS"] = _hubBaseUrl;

        _hubProcess = Process.Start(startInfo);
        if (_hubProcess is null)
        {
            throw new InvalidOperationException("نمی‌توان Kandoo.Hub.exe را راه‌اندازی کرد.");
        }

        // C-95: Wait for Hub to become available on the chosen port.
        StatusLabel.Text = "در حال راه‌اندازی سرویس هاب محلی…";
        await WaitForHubAsync(TimeSpan.FromSeconds(30));

        // C-87: Create a store ID for this shop and issue an owner session.
        // M-005 item 3c: the endpoint reads HubDeviceId/OwnerUserId/BootstrapProof
        // from the request body (NOT from the Authorization header). Send the
        // full OwnerSessionRequest shape, matching the HubApiClient.Owner test
        // helper and the OwnerSessionRequest contract.
        var storeId = Guid.NewGuid();
        var hubDeviceId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        StatusLabel.Text = "در حال ایجاد نشست مالک فروشگاه…";

        var ownerSessionBody = new
        {
            hub_device_id = hubDeviceId,
            owner_user_id = ownerUserId,
            bootstrap_proof = bootstrapProof
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/stores/{storeId}/owner-sessions");
        request.Content = new StringContent(
            JsonSerializer.Serialize(ownerSessionBody),
            Encoding.UTF8,
            "application/json");

        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"نشست مالک ایجاد نشد (HTTP {(int)response.StatusCode}): {body}");
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        var sessionToken = root.TryGetProperty("session_token", out var tokenProp)
            ? tokenProp.GetString() ?? string.Empty
            : string.Empty;

        if (string.IsNullOrWhiteSpace(sessionToken))
        {
            throw new InvalidOperationException("نشست مالک issuing نشد: session_token در پاسخ موجود نیست.");
        }

        // C-95: Save profile, session token, and Hub base URL so the app can
        // connect to the Hub this setup started.
        _shopProfile = new LocalUserProfile(AppUserRole.Store);
        _profiles.Save(_shopProfile);

        _tokenFile.Save(new DeviceTokenRecord(sessionToken, storeId));
        SaveHubBaseUrl(_hubBaseUrl);

        AdvanceToStep3();
    }

    private async void OnFinishClicked(object? sender, EventArgs e)
    {
        // Small delay so the user sees the "ready" message, then navigate.
        await Task.Delay(200);
        Application.Current!.MainPage = new NavigationPage(new MainPage(_shopProfile ?? new LocalUserProfile(AppUserRole.Store), _profiles));
    }

    private async Task WaitForHubAsync(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var health = await _http.GetAsync("/health");
                if (health.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
                // Hub not yet available; keep waiting.
            }
            await Task.Delay(500);
        }
        throw new InvalidOperationException("سرویس هاب در زمان انتظار در دسترس نشد.");
    }

    private async void OnCopyReportClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastError))
            return;

        try
        {
            await Clipboard.Default.SetTextAsync(_lastError);
            CopyStatusLabel.Text = "گزارش کپی شد.";
        }
        catch (Exception exception)
        {
            CopyStatusLabel.Text = $"کپی ناموفق: {exception.Message}";
        }
    }

    /// <summary>Stops the Hub process started by a previous run of this setup, if any.</summary>
    private void StopPreviousHub()
    {
        if (_hubProcess is not null && !_hubProcess.HasExited)
        {
            try
            {
                _hubProcess.Kill();
                _hubProcess.WaitForExit(5000);
            }
            catch
            {
                // Best-effort kill; if the process is already gone, that is fine.
            }
        }

        _hubProcess = null;
    }

    /// <summary>Returns a free TCP port on localhost.</summary>
    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Persists the chosen Hub base URL so Kandoo.App can read it.</summary>
    private static void SaveHubBaseUrl(string hubBaseUrl)
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "kandoo.hub-url.v1.txt");
        File.WriteAllText(path, hubBaseUrl);
    }

    private static bool IsExpected(Exception exception) => exception is ArgumentException or
        FormatException or InvalidOperationException or IOException or HttpRequestException or
        TaskCanceledException or Win32Exception or SocketException;
}
