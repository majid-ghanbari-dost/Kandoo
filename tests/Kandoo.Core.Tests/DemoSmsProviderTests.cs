using System.IO;
using Kandoo.Core;
using Xunit;

namespace Kandoo.Core.Tests;

public sealed class DemoSmsProviderTests : IDisposable
{
    private readonly string _outboxDir;
    private readonly DemoSmsProvider _provider;

    public DemoSmsProviderTests()
    {
        _outboxDir = Path.Combine(Path.GetTempPath(), "kandoo-demo-sms-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outboxDir);
        _provider = new DemoSmsProvider(_outboxDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_outboxDir, recursive: true);
        }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }

    [Fact]
    public void C80_DemoSmsWritesToOutboxAndInMemoryLog()
    {
        var message = _provider.Send("+989120000101", "سلام، نمونه پیامک دمو", storeName: "فروشگاه نمونه", displayNumber: 7);

        Assert.Equal(1, _provider.CapturedCount);
        var log = _provider.Log;
        Assert.Single(log);
        Assert.Equal("+989120000101", log[0].RecipientPhone);
        Assert.Equal("سلام، نمونه پیامک دمو", log[0].Body);
        Assert.Equal("فروشگاه نمونه", log[0].StoreName);
        Assert.Equal(7, log[0].DisplayNumber);
        Assert.NotEqual(Guid.Empty, log[0].MessageId);

        var outboxFile = Path.Combine(_outboxDir, "demo-sms.log");
        Assert.True(File.Exists(outboxFile), "Demo outbox file should be created.");
        var content = File.ReadAllText(outboxFile);
        Assert.Contains("+989120000101", content);
        Assert.Contains("سلام، نمونه پیامک دمو", content);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void C80_DemoSmsRejectsEmptyRecipient(string? phone)
    {
        var ex = Assert.Throws<ArgumentException>(() => _provider.Send(phone!, "body"));
        Assert.Contains("recipient phone", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void C80_DemoSmsRejectsEmptyBody(string? body)
    {
        var ex = Assert.Throws<ArgumentException>(() => _provider.Send("+989120000101", body!));
        Assert.Contains("SMS body", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }
}
