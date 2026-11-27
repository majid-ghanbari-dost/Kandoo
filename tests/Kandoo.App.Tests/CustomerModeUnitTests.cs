using Xunit;

namespace Kandoo.App.Tests;

/// <summary>
/// C-92 customer-mode unit tests (emulator-free): identity normalization,
/// remembered device/session, Persian digit formatting and role-switch routing.
/// All data is synthetic (C-92 / ORDER 16 fictional demo numbers only).
/// </summary>
public sealed class CustomerModeUnitTests
{
    [Theory]
    [InlineData("۰۹۱۲۰۰۰۰۰۰۱", "+989120000001")] // Persian digits, local format
    [InlineData("09120000001", "+989120000001")]
    [InlineData("0912 000 0001", "+989120000001")]
    [InlineData("9120000001", "+989120000001")]
    [InlineData("+989120000001", "+989120000001")]
    [InlineData("00989120000001", "+989120000001")]
    [InlineData("989120000001", "+989120000001")]
    public void C92_Phone_Normalizes_To_E164(string input, string expected)
    {
        Assert.True(CustomerPhone.TryNormalize(input, out var e164));
        Assert.Equal(expected, e164);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("abcdefghij")]
    [InlineData("0912")]
    [InlineData("+12345")]
    public void C92_Phone_Rejects_Invalid(string? input)
    {
        Assert.False(CustomerPhone.TryNormalize(input, out _));
    }

    [Fact]
    public void C92_DeviceId_Is_43Char_Base64Url_And_Stable_When_Persisted()
    {
        var id = CustomerDeviceId.New();
        Assert.Equal(43, id.Length);
        Assert.True(CustomerDeviceId.IsValid(id));
        Assert.False(CustomerDeviceId.IsValid(id + "x"));
        Assert.False(CustomerDeviceId.IsValid(id[..42]));
        Assert.False(CustomerDeviceId.IsValid(id[..42] + "+"));

        var dir = TempDir();
        try
        {
            var store = new CustomerSessionStore(dir);
            var device = store.LoadOrCreateDevice();
            var reloaded = new CustomerSessionStore(dir).LoadOrCreateDevice();
            Assert.Equal(device.DeviceId, reloaded.DeviceId);
            Assert.Null(reloaded.SessionToken);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void C92_Session_Remembers_Verified_Device_And_Clears_Session_On_Logout()
    {
        var dir = TempDir();
        try
        {
            var store = new CustomerSessionStore(dir);
            var device = store.LoadOrCreateDevice();
            var logged = new CustomerSessionRecord(device.DeviceId, "SYNTHETIC-session-token-0001", "+989120000001");
            store.Save(logged);

            var loaded = new CustomerSessionStore(dir).Load();
            Assert.NotNull(loaded);
            Assert.True(loaded!.IsLoggedIn);
            Assert.Equal("+989120000001", loaded.Phone);
            Assert.Equal("SYNTHETIC-session-token-0001", loaded.SessionToken);

            // Logout keeps the device identity (verified device remembered) but drops the session.
            store.ClearSession();
            var after = new CustomerSessionStore(dir).Load();
            Assert.NotNull(after);
            Assert.False(after!.IsLoggedIn);
            Assert.Equal(device.DeviceId, after.DeviceId);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void C92_LocalConfig_Defaults_To_Production_And_Reads_Kit_Override()
    {
        var defaults = new CustomerLocalConfig(null, null);
        Assert.Equal(CustomerApiClient.DefaultBaseUrl, defaults.ApiBaseUrl);
        Assert.Null(defaults.TelegramUrl);

        var dir = TempDir();
        try
        {
            File.WriteAllText(
                Path.Combine(dir, CustomerLocalConfig.FileName),
                "{\"api_base_url\": \"http://192.168.1.5:8080/\", \"telegram_url\": \"https://t.me/example\"}");
            var config = CustomerLocalConfig.Load(dir);
            Assert.Equal("http://192.168.1.5:8080", config.ApiBaseUrl);
            Assert.Equal("https://t.me/example", config.TelegramUrl);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void C92_PersianDigits_Formats_Amounts_Per_Boards()
    {
        Assert.Equal("۱۲۰٬۰۰۰", PersianDigits.Format(120000));
        Assert.Equal("۷", PersianDigits.Format(7));
        Assert.Equal("۱٬۰۰۰", PersianDigits.Format(1000));
        Assert.Equal("۲۵٬۰۰۰ ریال", PersianDigits.FormatAmount(25000, null));
        Assert.Equal("۲۸۵٬۰۰۰ تومان", PersianDigits.FormatAmount(285000, "toman"));
    }

    [Fact]
    public void C92_RoleSwitch_Routes_Per_Role_And_Platform()
    {
        // Android: three roles; customer mode lands on the customer home.
        Assert.Equal(RoleStartKind.CustomerHome, RoleStart.For(AppUserRole.Customer, isWindows: false));
        Assert.Equal(RoleStartKind.StoreWorkspace, RoleStart.For(AppUserRole.Store, isWindows: false));
        Assert.Equal(RoleStartKind.StoreWorkspace, RoleStart.For(AppUserRole.Both, isWindows: false));

        // Windows is store-only for MVP-1 (product-ux-spec §1).
        Assert.Equal(RoleStartKind.StoreWorkspace, RoleStart.For(AppUserRole.Store, isWindows: true));

        Assert.Equal("نقش: مشتری", RoleStart.RoleLabel(AppUserRole.Customer));
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"kandoo-c92-unit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
