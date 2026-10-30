namespace Kandoo.Cloud;

/// <summary>
/// Faraz adapter configuration. There are deliberately no credential fields or
/// repository defaults; owner-approved transport and secret mapping are pending.
/// </summary>
public sealed record FarazSmsOptions
{
    public bool Enabled { get; set; }
}

public sealed class SmsProviderUnavailableException : InvalidOperationException
{
    public SmsProviderUnavailableException(string code)
        : base(code) => Code = code;

    public string Code { get; }
}

/// <summary>
/// Fail-closed Faraz seam only. It performs no I/O and never fabricates a
/// provider message ID. The deterministic stub remains the test provider.
/// </summary>
public sealed class FarazSmsProvider : ISmsProvider
{
    private readonly FarazSmsOptions _options;

    public FarazSmsProvider(FarazSmsOptions? options = null) =>
        _options = options ?? new FarazSmsOptions();

    public Task<SmsProviderResult> SendAsync(
        SmsProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_options.Enabled)
        {
            throw new SmsProviderUnavailableException("SMS_PROVIDER_DISABLED");
        }

        // Enabling a placeholder must not silently become a live integration.
        throw new SmsProviderUnavailableException("FARAZ_SMS_TRANSPORT_NOT_IMPLEMENTED");
    }
}
