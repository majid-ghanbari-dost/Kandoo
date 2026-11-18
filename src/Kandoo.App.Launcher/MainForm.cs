using System.Diagnostics;
using System.Drawing;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace Kandoo.App.Launcher;

public class MainForm : Form
{
    private readonly IUserProfileStore _profiles;
    private readonly SharedTokenFile _tokenFile = new();
    private readonly HttpClient _http = new();
    private string? _lastError;

    // C-87 First-Run Setup: fixed port for local Hub on this machine.
    private const string HubBaseUrl = "http://localhost:5000";

    // UI controls
    private TextBox _storeNameEntry = null!;
    private Label _statusLabel = null!;
    private Label _errorLabel = null!;
    private Button _startShopButton = null!;
    private Button _copyReportButton = null!;
    private Button _finishButton = null!;
    private ProgressBar _configProgress = null!;
    private Label _step2Status = null!;
    private Label _step3Status = null!;
    private Panel _step1Panel = null!;
    private Panel _step2Panel = null!;
    private Panel _step3Panel = null!;

    public MainForm(IUserProfileStore profiles)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _http.BaseAddress = new Uri(HubBaseUrl);

        Text = "کندو — راه‌اندازی اولیهٔ فروشگاه";
        Size = new Size(560, 560);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        InitializeUI();
        UpdateStepVisibility();
        _statusLabel.Text = "مرحله ۱: نام فروشگاه را وارد کنید.";
    }

    private void InitializeUI()
    {
        var font = new Font("Tahoma", 12, FontStyle.Regular);

        // Header
        var titleLabel = new Label
        {
            Text = "کندو",
            Font = new Font("Tahoma", 24, FontStyle.Bold),
            ForeColor = Color.FromArgb(43, 29, 14),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 50
        };
        var subtitleLabel = new Label
        {
            Text = "راه‌اندازی اولیهٔ فروشگاه",
            Font = new Font("Tahoma", 14, FontStyle.Regular),
            ForeColor = Color.FromArgb(90, 74, 51),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 30
        };

        var headerPanel = new Panel { Dock = DockStyle.Top, Height = 90 };
        headerPanel.Controls.Add(subtitleLabel);
        headerPanel.Controls.Add(titleLabel);

        // Step 1 panel
        _step1Panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };
        var step1Label = new Label
        {
            Text = "۱. نام فروشگاه",
            Font = new Font("Tahoma", 14, FontStyle.Bold),
            ForeColor = Color.FromArgb(43, 29, 14),
            Dock = DockStyle.Top,
            Height = 30
        };
        _storeNameEntry = new TextBox
        {
            Font = font,
            Height = 36,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 10),
            PlaceholderText = "نام فروشگاه را وارد کنید"
        };
        _startShopButton = new Button
        {
            Text = "شروع راه‌اندازی خودکار",
            Font = font,
            Height = 40,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 10),
            BackColor = Color.FromArgb(242, 169, 0),
            ForeColor = Color.FromArgb(43, 29, 14),
            FlatStyle = FlatStyle.Flat
        };
        _startShopButton.FlatAppearance.BorderSize = 0;
        _startShopButton.Click += OnStartShopClicked;
        _statusLabel = new Label
        {
            Text = "مرحله ۱: نام فروشگاه را وارد کنید.",
            Font = font,
            ForeColor = Color.FromArgb(51, 51, 51),
            Dock = DockStyle.Top,
            Height = 60
        };
        _errorLabel = new Label
        {
            Text = string.Empty,
            Font = new Font("Tahoma", 10),
            ForeColor = Color.FromArgb(154, 59, 10),
            Dock = DockStyle.Top,
            Height = 40
        };
        _copyReportButton = new Button
        {
            Text = "کپی گزارش خطا",
            Font = font,
            Height = 30,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 10, 0, 0),
            Visible = false
        };
        _copyReportButton.Click += OnCopyReportClicked;

        _step1Panel.Controls.Add(_copyReportButton);
        _step1Panel.Controls.Add(_errorLabel);
        _step1Panel.Controls.Add(_statusLabel);
        _step1Panel.Controls.Add(_startShopButton);
        _step1Panel.Controls.Add(_storeNameEntry);
        _step1Panel.Controls.Add(step1Label);

        // Step 2 panel
        _step2Panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20), Visible = false };
        var step2Label = new Label
        {
            Text = "۲. پیکربندی خودکار فروشگاه",
            Font = new Font("Tahoma", 14, FontStyle.Bold),
            ForeColor = Color.FromArgb(43, 29, 14),
            Dock = DockStyle.Top,
            Height = 30
        };
        _configProgress = new ProgressBar
        {
            Dock = DockStyle.Top,
            Height = 24,
            Margin = new Padding(0, 0, 0, 10),
            Style = ProgressBarStyle.Marquee
        };
        _step2Status = new Label
        {
            Text = "در حال راه‌اندازی خودکار فروشگاه…",
            Font = font,
            ForeColor = Color.FromArgb(51, 51, 51),
            Dock = DockStyle.Top,
            Height = 60
        };
        _step2Panel.Controls.Add(_step2Status);
        _step2Panel.Controls.Add(_configProgress);
        _step2Panel.Controls.Add(step2Label);

        // Step 3 panel
        _step3Panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20), Visible = false };
        var step3Label = new Label
        {
            Text = "۳. پایان راه‌اندازی",
            Font = new Font("Tahoma", 14, FontStyle.Bold),
            ForeColor = Color.FromArgb(43, 29, 14),
            Dock = DockStyle.Top,
            Height = 30
        };
        var readyLabel = new Label
        {
            Text = "فروشگاه شما آماده است.",
            Font = new Font("Tahoma", 12, FontStyle.Regular),
            ForeColor = Color.FromArgb(37, 107, 67),
            Dock = DockStyle.Top,
            Height = 30
        };
        _finishButton = new Button
        {
            Text = "ورود به برنامه",
            Font = font,
            Height = 40,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 10),
            BackColor = Color.FromArgb(242, 169, 0),
            ForeColor = Color.FromArgb(43, 29, 14),
            FlatStyle = FlatStyle.Flat
        };
        _finishButton.FlatAppearance.BorderSize = 0;
        _finishButton.Click += OnFinishClicked;
        _step3Status = new Label
        {
            Text = "مرحله ۳: فروشگاه آماده است. وارد شدن به برنامه…",
            Font = font,
            ForeColor = Color.FromArgb(51, 51, 51),
            Dock = DockStyle.Top,
            Height = 60
        };
        _step3Panel.Controls.Add(_step3Status);
        _step3Panel.Controls.Add(_finishButton);
        _step3Panel.Controls.Add(readyLabel);
        _step3Panel.Controls.Add(step3Label);

        var mainContainer = new Panel { Dock = DockStyle.Fill };
        mainContainer.Controls.Add(_step3Panel);
        mainContainer.Controls.Add(_step2Panel);
        mainContainer.Controls.Add(_step1Panel);

        Controls.Add(mainContainer);
        Controls.Add(headerPanel);
    }

    private void UpdateStepVisibility()
    {
        _step1Panel.Visible = true;
        _step2Panel.Visible = false;
        _step3Panel.Visible = false;
    }

    private void AdvanceToStep2()
    {
        _step1Panel.Visible = false;
        _step2Panel.Visible = true;
        _step3Panel.Visible = false;

        _statusLabel.Text = string.Empty;
        _step2Status.Text = "مرحله ۲: تنظیمات خودکار فروشگاه در حال اجرا است…";
    }

    private void AdvanceToStep3()
    {
        _step1Panel.Visible = false;
        _step2Panel.Visible = false;
        _step3Panel.Visible = true;

        _step3Status.Text = "مرحله ۳: فروشگاه آماده است. وارد شدن به برنامه…";
    }

    private async void OnStartShopClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_storeNameEntry.Text))
        {
            _statusLabel.Text = "نام فروشگاه را وارد کنید.";
            _copyReportButton.Visible = false;
            return;
        }

        _lastError = null;
        _copyReportButton.Visible = false;
        _errorLabel.Text = string.Empty;
        AdvanceToStep2();

        try
        {
            await ConfigureShopAsync(_storeNameEntry.Text.Trim());
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _lastError = exception.Message;
            _statusLabel.Text = "خطا در راه‌اندازی خودکار: " + exception.Message;
            _errorLabel.Text = _lastError;
            _copyReportButton.Visible = true;
            _step2Panel.Visible = false;
            _step1Panel.Visible = true;
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
        // passed via environment variable.
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

        Process.Start(startInfo);

        // C-87: Wait for Hub to become available on localhost:5000.
        _step2Status.Text = "در حال راه‌اندازی سرویس هاب محلی…";
        await WaitForHubAsync(TimeSpan.FromSeconds(30));

        // C-87: Create a store ID for this shop and issue an owner session.
        var storeId = Guid.NewGuid();
        _step2Status.Text = "در حال ایجاد نشست مالک فروشگاه…";

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
        _profiles.Save(new LocalUserProfile(AppUserRole.Store));
        _tokenFile.Save(new DeviceTokenRecord(sessionToken, storeId));

        AdvanceToStep3();
    }

    private async void OnFinishClicked(object? sender, EventArgs e)
    {
        await Task.Delay(500);
        Close();
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

    private void OnCopyReportClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastError))
            return;

        try
        {
            Clipboard.SetText(_lastError);
        }
        catch { }
    }

    private static bool IsExpected(Exception exception) => exception is ArgumentException or
        FormatException or InvalidOperationException or IOException;
}
