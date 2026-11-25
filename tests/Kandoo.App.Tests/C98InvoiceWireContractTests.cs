using System.Text.Json;
using Kandoo.App;
using Xunit;

namespace Kandoo.App.Tests;

/// <summary>
/// C-98 / DoD-A point 3: the app's invoice calls must match the Hub's wire contract.
/// </summary>
/// <remarks>
/// Every one of these assertions failed before this file existed, and no test covered the path,
/// so CI stayed green while creating an invoice from the Windows app could never work:
///
/// <list type="bullet">
/// <item>the Hub wraps results in an operation envelope and puts the invoice at
/// <c>response.invoice.invoice_id</c>; the app read <c>invoice_id</c> from the root and threw
/// <c>"Hub draft response missing invoice_id"</c> on every call;</item>
/// <item>the Hub's items route requires <c>operation_id</c>, <c>description</c>,
/// <c>quantity_millis</c> and <c>unit_price_rials</c>; the app sent <c>product_id</c>,
/// <c>quantity</c> and <c>unit_price_rials</c>, so the route answered 400 VALIDATION_ERROR;</item>
/// <item>the Hub's issue route requires <c>service_type</c> as well as <c>operation_id</c>.</item>
/// </list>
///
/// The fixture below is the shape the Hub actually returns, taken from
/// <c>HubApiFirstEndpoints.OperationWithCurrency</c> and <c>HubOperationService.InvoiceProjection</c>.
/// </remarks>
public sealed class C98InvoiceWireContractTests
{
    [Fact]
    public void C98_ReadsInvoiceIdFromTheOperationEnvelope()
    {
        // Verbatim shape of a Hub draft reply (status/response envelope, invoice nested twice).
        const string hubReply = """
        {
          "status": "SUCCEEDED",
          "result_status": "ACCEPTED",
          "operation_seq": 3,
          "request_hash": "abc",
          "idempotent_replay": false,
          "response": {
            "invoice": {
              "invoice_id": "9f8b7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d",
              "display_number": null,
              "status": "DRAFT"
            },
            "lock": { },
            "next_display_number": 1
          },
          "currency": { "code": "IRR" }
        }
        """;

        using var document = JsonDocument.Parse(hubReply);
        Assert.True(OperationResponse.TryReadInvoiceId(document.RootElement, out var invoiceId));
        Assert.Equal("9f8b7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d", invoiceId);
    }

    [Fact]
    public void C98_RootLevelInvoiceIdIsNotAccepted()
    {
        // The old expectation, kept as a negative: a reply shaped this way is NOT what the Hub
        // sends, and treating it as valid is what hid the bug.
        using var document = JsonDocument.Parse("""{ "status":"SUCCEEDED", "invoice_id":"abc" }""");
        Assert.False(OperationResponse.TryReadInvoiceId(document.RootElement, out _));
    }

    [Theory]
    [InlineData("""{ "response": { "invoice": { } } }""")]
    [InlineData("""{ "response": { } }""")]
    [InlineData("""{ "status": "SUCCEEDED" }""")]
    [InlineData("""{ "response": { "invoice": { "invoice_id": "" } } }""")]
    public void C98_MalformedRepliesAreRejectedRatherThanGuessed(string reply)
    {
        using var document = JsonDocument.Parse(reply);
        Assert.False(OperationResponse.TryReadInvoiceId(document.RootElement, out var invoiceId));
        Assert.Equal(string.Empty, invoiceId);
    }

    [Fact]
    public void C98_ItemRowCarriesTheDescriptionTheHubRequires()
    {
        var row = new InvoiceItemRow("محصول نمونه", 2, 12500, 25000, "product-1");
        Assert.Equal("محصول نمونه", row.Description);
        Assert.Equal("product-1", row.ProductId);
        // Total is displayed to the owner in Rials; keep the existing formatting contract.
        Assert.Contains("ریال", row.TotalDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public void C98_ItemRowWorksWithoutAProductId()
    {
        // A free-text line has no catalogue product; the Hub treats product_id as optional and
        // the app must not invent one.
        var row = new InvoiceItemRow("خط آزاد", 1, 1000, 1000);
        Assert.Null(row.ProductId);
        Assert.Equal("خط آزاد", row.Description);
    }
}
