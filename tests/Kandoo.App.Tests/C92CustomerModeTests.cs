using System.IO;
using System.Text.Json;
using Kandoo.App;
using Xunit;

namespace Kandoo.App.Tests;

public sealed class C92CustomerModeTests
{
    [Fact]
    public void C92_SharedTokenFile_RemembersVerifiedDevice()
    {
        // Arrange: temp folder for the shared token file
        var tempDir = Path.Combine(Path.GetTempPath(), $"kandoo-c92-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var storeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...";
            var file = new SharedTokenFile(tempDir);

            // Act: save the verified device token (after successful OTP login)
            file.Save(new DeviceTokenRecord(token, storeId));

            // Assert: load returns the same token and store ID
            var loaded = file.Load();
            Assert.NotNull(loaded);
            Assert.Equal(token, loaded.Token);
            Assert.Equal(storeId, loaded.StoreId);

            // Act: clear simulates logout / device unverified
            file.Clear();
            Assert.Null(file.Load());
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void C92_DeviceTokenRecord_RequiresTokenAndStoreId()
    {
        var storeId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        // Empty token should throw
        Assert.Throws<ArgumentException>(() => new DeviceTokenRecord("", storeId));

        // Empty store ID should throw
        Assert.Throws<ArgumentException>(() => new DeviceTokenRecord("token", Guid.Empty));
    }

    [Fact]
    public void C92_SharedTokenFile_JsonIsSnakeCase()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"kandoo-c92-json-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var file = new SharedTokenFile(tempDir);
            file.Save(new DeviceTokenRecord("abcd1234", Guid.Parse("11111111-1111-1111-1111-111111111111")));

            var json = File.ReadAllText(Path.Combine(tempDir, "kandoo.device-token.v1.json"));
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            Assert.True(root.TryGetProperty("token", out _));
            Assert.True(root.TryGetProperty("store_id", out _));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
