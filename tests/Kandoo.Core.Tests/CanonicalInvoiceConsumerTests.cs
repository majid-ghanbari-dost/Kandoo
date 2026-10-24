using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kandoo.Core.Tests;

public sealed class CanonicalInvoiceConsumerTests
{
    [Fact]
    public void C07_AllFourE11ValidGoldensPassAndMapOnlyToReview()
    {
        using var manifest = ReadManifest();
        var count = 0;
        foreach (var golden in manifest.RootElement.GetProperty("goldens").EnumerateArray())
        {
            if (golden.GetProperty("expected_verdict").GetString() != "VALID")
            {
                continue;
            }

            count++;
            var relativePath = golden.GetProperty("path").GetString()!;
            var bytes = File.ReadAllBytes(FixturePath("goldens/" + relativePath));
            Assert.Equal(
                golden.GetProperty("file_sha256").GetString(),
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            var result = CanonicalInvoiceConsumer.TryConsumeForReview(System.Text.Encoding.UTF8.GetString(bytes));
            Assert.True(result.Accepted, relativePath + ": " + result.ErrorCode);
            Assert.Null(result.ErrorCode);
            Assert.NotNull(result.Review);
            var review = result.Review!;
            Assert.Equal("REVIEW", review.QueueStatus);
            Assert.Equal(review.Source.GetProperty("state").GetString(), review.SourceState);
            Assert.NotEmpty(review.LineItems);
        }

        Assert.Equal(4, count);
    }

    [Fact]
    public void C07_AllThreeE11InvalidGoldensReturnTheirStableManifestErrorCodes()
    {
        using var manifest = ReadManifest();
        var count = 0;
        foreach (var golden in manifest.RootElement.GetProperty("goldens").EnumerateArray())
        {
            if (golden.GetProperty("expected_verdict").GetString() != "INVALID")
            {
                continue;
            }

            count++;
            var relativePath = golden.GetProperty("path").GetString()!;
            var result = CanonicalInvoiceConsumer.TryConsumeForReview(File.ReadAllText(FixturePath("goldens/" + relativePath)));
            Assert.False(result.Accepted, relativePath);
            Assert.Null(result.Review);
            Assert.Equal(golden.GetProperty("expected_failure_code").GetString(), result.ErrorCode);
        }

        Assert.Equal(3, count);
    }

    [Theory]
    [InlineData("1250.0")]
    [InlineData("1250e0")]
    public void C07_QuantityRejectsFractionalAndExponentNumberTokens(string token)
    {
        var json = File.ReadAllText(FixturePath("goldens/valid/draft_mode_b_null_display_number.json"));
        var changed = json.Replace("\"quantity_millis\":1250", "\"quantity_millis\":" + token, StringComparison.Ordinal);
        Assert.NotEqual(json, changed);
        var result = CanonicalInvoiceConsumer.TryConsumeForReview(changed);
        Assert.False(result.Accepted);
        Assert.Equal(CanonicalInvoiceErrorCodes.QuantityNotInteger, result.ErrorCode);
    }

    [Theory]
    [InlineData("800.0")]
    [InlineData("800e0")]
    public void C07_AmountRejectsFractionalAndExponentNumberTokens(string token)
    {
        var json = File.ReadAllText(FixturePath("goldens/valid/draft_mode_b_null_display_number.json"));
        var changed = json.Replace("\"unit_price\":800", "\"unit_price\":" + token, StringComparison.Ordinal);
        Assert.NotEqual(json, changed);
        var result = CanonicalInvoiceConsumer.TryConsumeForReview(changed);
        Assert.False(result.Accepted);
        Assert.Equal(CanonicalInvoiceErrorCodes.AmountNotInteger, result.ErrorCode);
    }

    [Fact]
    public void C07_NegativeAmountIsRejectedBeforeReviewMapping()
    {
        var json = File.ReadAllText(FixturePath("goldens/valid/draft_mode_b_null_display_number.json"));
        var changed = json.Replace("\"unit_price\":800", "\"unit_price\":-1", StringComparison.Ordinal);
        var result = CanonicalInvoiceConsumer.TryConsumeForReview(changed);
        Assert.False(result.Accepted);
        Assert.Equal(CanonicalInvoiceErrorCodes.AmountNegative, result.ErrorCode);
    }

    [Fact]
    public void C07_MissingRequiredFieldHasStableErrorCode()
    {
        var root = JsonNode.Parse(File.ReadAllText(FixturePath("goldens/valid/draft_mode_b_null_display_number.json")))!.AsObject();
        Assert.True(root.Remove("operation_id"));
        var result = CanonicalInvoiceConsumer.TryConsumeForReview(root.ToJsonString());
        Assert.False(result.Accepted);
        Assert.Equal(CanonicalInvoiceErrorCodes.RequiredFieldMissing, result.ErrorCode);
    }

    private static JsonDocument ReadManifest() => JsonDocument.Parse(File.ReadAllText(FixturePath("goldens/manifest.json")));

    private static string FixturePath(string relative)
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Fixtures", "E11", "contracts");
            if (Directory.Exists(candidate))
            {
                return Path.Combine(candidate, relative.Replace('/', Path.DirectorySeparatorChar));
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the E-11 test fixture directory.");
    }
}
