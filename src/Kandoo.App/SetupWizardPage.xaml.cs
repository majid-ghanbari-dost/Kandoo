namespace Kandoo.App;

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

public partial class SetupWizardPage : ContentPage
{
    private readonly IUserProfileStore _profiles;
    private readonly SharedTokenFile _tokenFile = new();
    private readonly HttpClient _http = new();
    private string? _lastError;
    private Process? _hubProcess;
    private LocalUserProfile? _shopProfile;

    // C-87 First-Run Setup: fixed port for local Hub on this machine.
    private const string HubBaseUrl = "http://localhost:5000";

    public SetupWizardPage(IUserProfileStore profiles)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _http.BaseAddress = new Uri(HubBaseUrl);
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
    }

    private async Task ConfigureShopAsync(string storeName)
    {
        // C-87: Locate Hub.exe next to the installed app.
        var appDir = Path.GetDirectoryName(Environment.ProcessPath) ?? string.Empty;
        var hubExe = Path.Combine(appDir, "Kandoo.Hub.exe");

        if (!File.Exists(hubExe))
        {
            throw new InvalidOperationException("فایل Kandoo.Hub.exe در پوشه نصب یافت نشد.");
        }

        // C-87: Generate OwnerBootstrapProof so the First-Run Setup can configure
        // owner bootstrap without editing JSON or using PowerShell.
        var bootstrapProof = Guid.NewGuid().ToString("D");

        // C-87: Start Hub.exe as a background process with OwnerBootstrapProof
        // passed via environment variable (ASP.NET Core configuration pipeline
        // maps Hub__OwnerBootstrapProof to Hub:OwnerBootstrapProof).
        var startInfo = new ProcessStartInfo
        {
            FileName = hubExe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.EnvironmentVariables["Hub__OwnerBootstrapProof"] = bootstrapProof;
        startInfo.EnvironmentVariables["ASPNETCORE_URLS"] = "http://localhost:5000";

        _hubProcess = Process.Start(startInfo);
        if (_hubProcess is null)
        {
            throw new InvalidOperationException("ניתן راه‌اندازی Kandoo.Hub.exe.");
        }

        // C-87: Wait for Hub to become available on localhost:5000.
        StatusLabel.Text = "در حال راه‌اندازی سرویس هاب محلی…";
        await WaitForHubAsync(TimeSpan.FromSeconds(30));

        // C-87: Create a store ID for this shop and issue an owner session.
        var storeId = Guid.NewGuid();
        StatusLabel.Text = "در حال ایجاد نشست مالک فروشگاه…";

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/stores/{storeId}/owner-sessions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bootstrapProof);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

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

        // C-87: Save profile and session token so the app enters the real environment.
        _shopProfile = new LocalUserProfile(AppUserRole.Store);
        _profiles.Save(_shopProfile);

        _tokenFile.Save(new DeviceTokenRecord(sessionToken, storeId));

        // C-87: Stop Hub process — it will continue running independently.
        // The process was started detached; it keeps running after setup completes.

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

    private static bool IsExpected(Exception exception) => exception is ArgumentException or
        FormatException or InvalidOperationException or IOException;
}
