using System.Text;
using System.Text.Json;

namespace Kandoo.App;

public sealed class OtpIssueResponse
{
    public string Status { get; set; } = string.Empty;
    public Guid ChallengeId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class OtpVerifyResponse
{
    public string Status { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}

public partial class CustomerOtpPage : ContentPage
{
    private readonly HttpClient _httpClient = new();
    private string? _challengeId;
    private string? _verifiedPhone;
    private string? _requestedPhone;
    private string? _lastError;

    public CustomerOtpPage()
    {
        InitializeComponent();
        UpdateUIState();
    }

    private async void OnRequestOtpClicked(object? sender, EventArgs e)
    {
            _requestedPhone = PhoneEntry.Text?.Trim();
            if (string.IsNullOrWhiteSpace(_requestedPhone))
            {
                OtpStatusLabel.Text = "شماره موبایل را وارد کنید.";
                return;
            }

        RequestOtpButton.IsEnabled = false;
        OtpStatusLabel.Text = "در حال ارسال کد تایید…";
        _lastError = null;

        try
        {
            var operationId = Guid.NewGuid().ToString("D");
            var body = new { Phone = _requestedPhone };
            var json = JsonSerializer.Serialize(body);
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.kandoo.cloud/v1/customer/otp/challenges")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Idempotency-Key", operationId);

            using var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                OtpStatusLabel.Text = $"خطا: {(int)response.StatusCode}";
                return;
            }

            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            var status = root.TryGetProperty("status", out var s) ? s.GetString() : string.Empty;
            _challengeId = root.TryGetProperty("challenge_id", out var cid) ? Guid.Parse(cid.GetString()!) : Guid.Empty;

            if (status == "OTP_SENT" || status == "DUPLICATE")
            {
                VerifyOtpButton.IsEnabled = true;
                OtpStatusLabel.Text = "کد تایید ارسال شد. کد را وارد کنید.";
            }
            else
            {
                OtpStatusLabel.Text = $"وضعیت: {status}";
            }
        }
        catch (Exception exception)
        {
            OtpStatusLabel.Text = $"خطا در ارتباط: {exception.Message}";
        }
        finally
        {
            RequestOtpButton.IsEnabled = true;
        }
    }

    private async void OnVerifyOtpClicked(object? sender, EventArgs e)
    {
        var code = OtpCodeEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(code) || code.Length != 6)
        {
            VerifyStatusLabel.Text = "کد ۶ رقمی را وارد کنید.";
            return;
        }
        if (_challengeId is null)
        {
            VerifyStatusLabel.Text = "ابتدا کد تایید را درخواست کنید.";
            return;
        }

        VerifyOtpButton.IsEnabled = false;
        VerifyStatusLabel.Text = "در حال تایید…";

        try
        {
            var body = new { Code = code };
            var json = JsonSerializer.Serialize(body);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.kandoo.cloud/v1/customer/otp/challenges/{_challengeId}/verify")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            using var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                VerifyStatusLabel.Text = $"خطا: {(int)response.StatusCode}";
                return;
            }

            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            var status = root.TryGetProperty("status", out var s) ? s.GetString() : string.Empty;

            if (status == "VERIFIED")
            {
                _verifiedPhone = phone;
                var tokenProp = root.TryGetProperty("access_token", out var tok) ? tok.GetString() : string.Empty;
                if (!string.IsNullOrEmpty(tokenProp))
                {
                    var record = new DeviceTokenRecord(tokenProp, Guid.Empty);
                    var tokenFile = new SharedTokenFile();
                    tokenFile.Save(record);
                }
                _verifiedPhone = _requestedPhone;
                UpdateUIState();
                VerifyStatusLabel.Text = "تأیید شد. به قسمت مشتری منتقل شدید.";
            }
            else
            {
                VerifyStatusLabel.Text = $"وضعیت: {status}";
            }
        }
        catch (Exception exception)
        {
            VerifyStatusLabel.Text = $"خطا در ارتباط: {exception.Message}";
        }
        finally
        {
            VerifyOtpButton.IsEnabled = true;
        }
    }

    private void UpdateUIState()
    {
        var verified = _verifiedPhone is not null;
        PhoneEntry.IsVisible = !verified;
        RequestOtpButton.IsVisible = !verified;
        OtpStatusLabel.IsVisible = !verified;
        OtpCodeEntry.IsVisible = verified && _challengeId is not null;
        VerifyOtpButton.IsVisible = verified && _challengeId is not null;
        VerifyStatusLabel.IsVisible = verified && _challengeId is not null;
        CustomerState.IsVisible = verified;
        CustomerPhoneLabel.Text = _verifiedPhone ?? string.Empty;

        if (verified)
        {
            StatusLabel.Text = "ورود مشتری فعال است.";
        }
    }
}
