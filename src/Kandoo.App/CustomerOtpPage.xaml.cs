namespace Kandoo.App;

/// <summary>
/// Customer login (C-92/C-97: Customer-Login-v2 board + Customer-Locked state).
/// OTP login against the PHP Cloud customer API (P-31 accepted contract):
/// request code → verify code → remembered session (verified device). The mobile
/// number is the identity. Board copy is verbatim; the short StatusLabel lines are
/// operational feedback only (reported in the PR description).
/// </summary>
public partial class CustomerOtpPage : ContentPage
{
    private const int MaxWrongCodes = 3;

    private readonly CustomerSessionStore _sessions = new();
    private readonly CustomerLocalConfig _config = CustomerLocalConfig.Load();
    private string? _sendIdempotencyKey;
    private string? _sendKeyPhone;
    private string? _challengeId;
    private string? _requestedPhone;
    private int _wrongCodes;
    private bool _forwarding;

    public CustomerOtpPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var session = _sessions.Load();
        if (!_forwarding && session is { IsLoggedIn: true })
        {
            _forwarding = true;
            await GoToInvoicesAsync();
        }
    }

    private async void OnSendCodeClicked(object? sender, EventArgs e)
    {
        if (!CustomerPhone.TryNormalize(PhoneEntry.Text, out var phone))
        {
            StatusLabel.Text = "شماره موبایل را وارد کنید.";
            return;
        }

        // One idempotency key per logical send (P-31): a new key only when the
        // number changes or a login cycle finished — retries of the same send stay idempotent.
        if (_sendKeyPhone != phone || _sendIdempotencyKey is null)
        {
            _sendKeyPhone = phone;
            _sendIdempotencyKey = Guid.NewGuid().ToString("D");
        }

        SendCodeButton.IsEnabled = false;
        StatusLabel.Text = "در حال ارسال کد تایید…";
        try
        {
            using var client = NewClient();
            var result = await client.RequestOtpAsync(phone, _sendIdempotencyKey);
            switch (result.Status)
            {
                case CustomerApiStatus.OtpSent:
                case CustomerApiStatus.Duplicate:
                    _requestedPhone = phone;
                    _challengeId = result.Value?.ChallengeId;
                    LoginButton.IsEnabled = _challengeId is not null;
                    StatusLabel.Text = "کد تایید ارسال شد. کد را وارد کنید.";
                    break;
                case CustomerApiStatus.Telegram:
                    ShowLockedState();
                    break;
                case CustomerApiStatus.RateLimited:
                    StatusLabel.Text = "درخواست‌ها زیاد است؛ کمی بعد دوباره تلاش کنید.";
                    break;
                default:
                    // The API deliberately gives one generic answer for «no invoice»
                    // and malformed input — the board footer already explains the rule.
                    StatusLabel.Text = "درخواست انجام نشد.";
                    break;
            }
        }
        catch (Exception)
        {
            StatusLabel.Text = "در حال حاضر ارتباط با سرور ممکن نیست.";
        }
        finally
        {
            SendCodeButton.IsEnabled = true;
        }
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        var code = PersianDigits.ToAsciiDigits((CodeEntry.Text ?? string.Empty).Trim());
        if (code.Length != 6 || code.Any(c => c is < '0' or > '9'))
        {
            StatusLabel.Text = "کد ۶ رقمی را وارد کنید.";
            return;
        }

        if (_challengeId is null)
        {
            StatusLabel.Text = "ابتدا کد تایید را درخواست کنید.";
            return;
        }

        LoginButton.IsEnabled = false;
        StatusLabel.Text = "در حال تایید…";
        try
        {
            using var client = NewClient();
            var result = await client.VerifyOtpAsync(_challengeId, code);
            if (result.Status == CustomerApiStatus.Ok && result.Value?.Token is not null)
            {
                var device = _sessions.LoadOrCreateDevice();
                _sessions.Save(new CustomerSessionRecord(device.DeviceId, result.Value.Token, _requestedPhone));
                StatusLabel.Text = "تأیید شد.";
                _forwarding = true;
                await GoToInvoicesAsync();
            }
            else
            {
                _wrongCodes++;
                if (_wrongCodes >= MaxWrongCodes)
                {
                    // Three wrong entries lock the challenge (OTP policy, UX spec §6).
                    ShowLockedState();
                }
                else
                {
                    StatusLabel.Text = "کد نادرست یا منقضی شده است.";
                }
            }
        }
        catch (Exception)
        {
            StatusLabel.Text = "در حال حاضر ارتباط با سرور ممکن نیست.";
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }

    private async void OnSupportClicked(object? sender, EventArgs e)
    {
        if (_config.TelegramUrl is not null)
        {
            try
            {
                await Launcher.Default.OpenAsync(_config.TelegramUrl);
                return;
            }
            catch (Exception)
            {
                // fall through to the note below
            }
        }

        StatusLabel.Text = "آدرس پشتیبانی هنوز در پیکربندی محلی ثبت نشده است.";
    }

    private void ShowLockedState()
    {
        LoginCard.IsVisible = false;
        LockedCard.IsVisible = true;
    }

    private async Task GoToInvoicesAsync()
    {
        if (Navigation.ModalStack.Count > 0 && ReferenceEquals(Navigation.ModalStack[^1], this))
        {
            await Navigation.PopModalAsync();
            if (Application.Current?.MainPage is NavigationPage navigationPage)
            {
                await navigationPage.CurrentPage.Navigation.PushAsync(new CustomerInvoicesPage());
            }
        }
        else
        {
            await Navigation.PushAsync(new CustomerInvoicesPage());
        }
    }

    private CustomerApiClient NewClient()
    {
        var device = _sessions.LoadOrCreateDevice();
        try
        {
            return new CustomerApiClient(_config.ApiBaseUrl, device.DeviceId);
        }
        catch (ArgumentException)
        {
            return new CustomerApiClient(CustomerApiClient.DefaultBaseUrl, device.DeviceId);
        }
    }
}
