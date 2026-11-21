using Xunit;

namespace Kandoo.Cloud.Tests;

public sealed class FarazSmsProviderTests
{
    [Fact]
    public async Task C24_FarazAdapterIsDisabledByDefault()
    {
        var options = new FarazSmsOptions();
        var provider = new FarazSmsProvider(options);

        Assert.False(options.Enabled);
        var exception = await Assert.ThrowsAsync<SmsProviderUnavailableException>(
            () => provider.SendAsync(Request()));
        Assert.Equal("SMS_PROVIDER_DISABLED", exception.Code);
    }

    [Fact]
    public async Task C24_EnablingTheSkeletonCannotFabricateLiveDelivery()
    {
        var provider = new FarazSmsProvider(new FarazSmsOptions { Enabled = true });

        var exception = await Assert.ThrowsAsync<SmsProviderUnavailableException>(
            () => provider.SendAsync(Request()));
        Assert.Equal("FARAZ_SMS_TRANSPORT_NOT_IMPLEMENTED", exception.Code);
    }

    private static SmsProviderRequest Request() => new(
        Guid.NewGuid(),
        "c24-faraz-disabled",
        Guid.NewGuid(),
        "+989120000000",
        "test body; no provider call is allowed",
        "test-only-fingerprint");
}
