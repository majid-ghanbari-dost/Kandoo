using System.Text.Json;
using Kandoo.Core;

namespace Kandoo.Hub;

public sealed record HubReviewAcceptRequest(string? CanonicalJson);
public sealed record HubReviewCorrectionRequest(string? FieldPath, JsonElement Value);
public sealed record HubReviewPhoneRequest(string? Phone);
public sealed record HubReviewIssueRequest(string? ServiceType, bool Print = false);

public static partial class HubApiFirstEndpoints
{
    private static void MapReviewApi(WebApplication app)
    {
        app.MapGet("/v1/stores/{store_id}/reviews", (
            Guid store_id,
            HttpContext context,
            IHubAccessStore access,
            IClock clock,
            ModeBReviewService reviews) =>
        {
            var auth = Authenticate(context, store_id, access, clock);
            return !auth.IsAuthenticated
                ? AuthError(auth.Code)
                : Results.Json(new { status = "OK", items = reviews.List(store_id) }, HubJson.Options);
        });

        app.MapPost("/v1/stores/{store_id}/reviews", (
            Guid store_id,
            HubReviewAcceptRequest? request,
            HttpContext context,
            IHubAccessStore access,
            IClock clock,
            ModeBReviewService reviews) =>
        {
            var auth = Authenticate(context, store_id, access, clock);
            if (!auth.IsAuthenticated)
            {
                return AuthError(auth.Code);
            }

            if (request is null || string.IsNullOrWhiteSpace(request.CanonicalJson))
            {
                return HubHttp.Error(400, "VALIDATION_ERROR", "canonical_json is required.");
            }

            return ReviewResult(reviews.Accept(store_id, request.CanonicalJson));
        });

        app.MapGet("/v1/stores/{store_id}/reviews/{review_id}/attention", (
            Guid store_id,
            string review_id,
            HttpContext context,
            IHubAccessStore access,
            IClock clock,
            ModeBReviewService reviews) =>
        {
            var auth = Authenticate(context, store_id, access, clock);
            if (!auth.IsAuthenticated)
            {
                return AuthError(auth.Code);
            }

            return reviews.Get(store_id, review_id) is null
                ? ReviewNotFound()
                : Results.Json(new { status = "OK", items = reviews.ListAttention(review_id) }, HubJson.Options);
        });

        app.MapPost("/v1/stores/{store_id}/reviews/{review_id}/corrections", (
            Guid store_id,
            string review_id,
            HubReviewCorrectionRequest? request,
            HttpContext context,
            IHubAccessStore access,
            IClock clock,
            ModeBReviewService reviews) =>
        {
            var auth = Authenticate(context, store_id, access, clock);
            if (!auth.IsAuthenticated)
            {
                return AuthError(auth.Code);
            }

            if (request is null || string.IsNullOrWhiteSpace(request.FieldPath) || request.Value.ValueKind == JsonValueKind.Undefined)
            {
                return HubHttp.Error(400, "VALIDATION_ERROR", "field_path and value are required.");
            }

            return reviews.Get(store_id, review_id) is null
                ? ReviewNotFound()
                : ReviewResult(reviews.Correct(
                    review_id, request.FieldPath, request.Value, auth.Session!.DeviceId.ToString("D")));
        });

        app.MapPost("/v1/stores/{store_id}/reviews/{review_id}/customer-phone", (
            Guid store_id,
            string review_id,
            HubReviewPhoneRequest? request,
            HttpContext context,
            IHubAccessStore access,
            IClock clock,
            ModeBReviewService reviews) =>
        {
            var auth = Authenticate(context, store_id, access, clock);
            if (!auth.IsAuthenticated)
            {
                return AuthError(auth.Code);
            }

            if (request is null || string.IsNullOrWhiteSpace(request.Phone))
            {
                return HubHttp.Error(400, "VALIDATION_ERROR", "phone is required.");
            }

            return reviews.Get(store_id, review_id) is null
                ? ReviewNotFound()
                : ReviewResult(reviews.SetCustomerPhone(
                    review_id, request.Phone, auth.Session!.DeviceId.ToString("D")));
        });

        app.MapPost("/v1/stores/{store_id}/reviews/{review_id}/issue", async (
            Guid store_id,
            string review_id,
            HubReviewIssueRequest? request,
            HttpContext context,
            IHubAccessStore access,
            IClock clock,
            ModeBReviewService reviews,
            IHubInvoicePrintAdapter printer,
            CancellationToken cancellationToken) =>
        {
            var auth = Authenticate(context, store_id, access, clock);
            if (!auth.IsAuthenticated)
            {
                return AuthError(auth.Code);
            }

            if (request is null)
            {
                return HubHttp.Error(400, "VALIDATION_ERROR", "service_type is required.");
            }

            if (ServiceTypeError(request.ServiceType) is { } disabled)
            {
                return disabled;
            }

            if (reviews.Get(store_id, review_id) is null)
            {
                return ReviewNotFound();
            }

            var result = reviews.Issue(review_id, auth.Session!);
            var printStatus = request.Print ? "NOT_ATTEMPTED" : "NOT_REQUESTED";
            if (request.Print && result.Status == "ISSUED" && result.InvoiceId is Guid invoiceId)
            {
                try
                {
                    printStatus = (await printer.PrintAsync(store_id, invoiceId, cancellationToken)).Status;
                }
                catch (InvalidOperationException)
                {
                    printStatus = "PRINT_FAILED";
                }
            }

            return Results.Json(new
            {
                status = result.Status,
                review = result.Review,
                idempotent_replay = result.IdempotentReplay,
                invoice_id = result.InvoiceId,
                display_number = result.DisplayNumber,
                print = new { requested = request.Print, status = printStatus },
                currency = HubInvoiceCurrency.Body
            }, HubJson.Options, statusCode: ReviewHttpStatus(result.Status));
        });
    }

    private static IResult ReviewResult(ModeBReviewActionResult result) => Results.Json(new
    {
        status = result.Status,
        review = result.Review,
        idempotent_replay = result.IdempotentReplay,
        invoice_id = result.InvoiceId,
        display_number = result.DisplayNumber
    }, HubJson.Options, statusCode: ReviewHttpStatus(result.Status));

    private static IResult ReviewNotFound() =>
        HubHttp.Error(404, "MODE_B_REVIEW_NOT_FOUND", "The review does not exist in this store.");

    private static int ReviewHttpStatus(string status) => status switch
    {
        "REVIEW" or "ISSUED" => 200,
        "MODE_B_REVIEW_NOT_FOUND" => 404,
        "STORE_ACCESS_DENIED" or "REVIEW_STORE_CONFLICT" => 403,
        "UNRESOLVED_ATTENTION_CELLS" or "REVIEW_ALREADY_ISSUED" or "PAYABLE_SUM_MISMATCH" or
            "OPERATOR_PHONE_REQUIRED" or "DRAFT_LOCK_CONFLICT" or "LOCK_NOT_OWNED" or "INVOICE_NOT_ISSUABLE" => 409,
        _ => 400
    };
}
