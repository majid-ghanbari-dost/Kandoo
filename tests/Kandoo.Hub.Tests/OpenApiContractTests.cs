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
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.StartsWith("3.", document.RootElement.GetProperty("openapi").GetString());
        var paths = document.RootElement.GetProperty("paths");
        foreach (var path in new[]
                 {
                     "/v1/stores/{store_id}/products",
                     "/v1/stores/{store_id}/products/cascade-options",
                     "/v1/stores/{store_id}/products/{product_id}",
                     "/v1/stores/{store_id}/service-types",
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
    }
}
