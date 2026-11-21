using System.Net;
using System.Text.Json;
using Xunit;

namespace Kandoo.Hub.Tests;

public sealed class OpenApiContractTests
{
    [Fact]
    public async Task C23_GeneratedDocumentContainsApiFirstRoutes()
    {
        using var factory = new HubApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(raw);
        Assert.StartsWith("3.", document.RootElement.GetProperty("openapi").GetString());
        var paths = document.RootElement.GetProperty("paths");
        foreach (var path in new[]
                 {
                     "/v1/stores/{store_id}/products",
                     "/v1/stores/{store_id}/products/cascade-options",
                     "/v1/stores/{store_id}/products/{product_id}",
                     "/v1/stores/{store_id}/service-types",
                     "/v1/stores/{store_id}/stats/v0",
                     "/v1/stores/{store_id}/status/v0",
                     "/v1/stores/{store_id}/devices/{device_id}/level",
                     "/v1/stores/{store_id}/invoices/drafts",
                     "/v1/stores/{store_id}/invoices/{invoice_id}/items",
                     "/v1/stores/{store_id}/invoices/{invoice_id}/issue",
                     "/v1/stores/{store_id}/invoices/{invoice_id}/sms/resend",
                     "/v1/stores/{store_id}/invoices/{invoice_id}/sms/{operation_id}",
                     "/v1/stores/{store_id}/reviews",
                     "/v1/stores/{store_id}/reviews/{review_id}/issue"
                 })
        {
            Assert.True(paths.TryGetProperty(path, out _), $"Missing generated OpenAPI path: {path}");
        }

        var statusPath = paths.GetProperty("/v1/stores/{store_id}/status/v0");
        Assert.True(statusPath.TryGetProperty("get", out _), "C-43 status must be generated as GET.");
        Assert.False(statusPath.TryGetProperty("post", out _), "C-43 status must remain read-only.");

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty("HubProductRegistrationRequest", out var registrationSchema), schemas.GetRawText());
        Assert.True(registrationSchema.TryGetProperty("properties", out var registrationProperties), registrationSchema.GetRawText());
        Assert.True(registrationProperties.TryGetProperty("purchase_price_rials", out var purchasePrice), registrationProperties.GetRawText());
        Assert.Equal("integer", purchasePrice.GetProperty("type").GetString());
        Assert.Equal("int64", purchasePrice.GetProperty("format").GetString());
        Assert.True(purchasePrice.TryGetProperty("nullable", out var nullable), purchasePrice.GetRawText());
        Assert.True(nullable.GetBoolean());

        var output = Environment.GetEnvironmentVariable("KANDOO_OPENAPI_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            await File.WriteAllTextAsync(output, raw);
        }
    }
}
