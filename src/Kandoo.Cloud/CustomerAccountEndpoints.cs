using System.Text.Json;

namespace Kandoo.Cloud;

public sealed record CustomerOtpIssueRequest(string? Phone);
public sealed record CustomerOtpVerifyRequest(string? Code);

public static class CustomerAccountEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/v1/customer/otp/challenges", async (HttpContext context, CustomerAccountService service) =>
        {
            var request = await ReadJson<CustomerOtpIssueRequest>(context);
            if (request is null)
            {
                return Respond(CustomerAccountResult.Failure(400, "VALIDATION_ERROR", "The request body is invalid.", "FIX_REQUEST"));
            }

            var operationId = context.Request.Headers["Idempotency-Key"].FirstOrDefault();
            var result = await service.IssueOtpAsync(request.Phone, operationId, context.RequestAborted);
            context.Response.Headers.CacheControl = "no-store";
            return Respond(result);
        });

        app.MapPost("/v1/customer/otp/challenges/{challenge_id}/verify", async (HttpContext context, string challenge_id, CustomerAccountService service) =>
        {
            var request = await ReadJson<CustomerOtpVerifyRequest>(context);
            if (request is null || !Guid.TryParse(challenge_id, out var challengeId))
            {
                return Respond(CustomerAccountResult.Failure(400, "OTP_INVALID", "The verification request is invalid.", "FIX_REQUEST"));
            }

            var result = service.VerifyOtp(challengeId, request.Code);
            context.Response.Headers.CacheControl = "no-store";
            return Respond(result);
        });

        app.MapPost("/v1/public/invoices/{token}/claim", (HttpContext context, string token, CustomerAccountService service) =>
        {
            var account = service.AuthenticateBearer(context.Request.Headers.Authorization.FirstOrDefault());
            if (account is null)
            {
                return Respond(Unauthenticated());
            }

            return Respond(service.ClaimInvoice(account, token));
        });

        app.MapGet("/v1/customer/invoices", (HttpContext context, CustomerAccountService service) =>
        {
            var account = service.AuthenticateBearer(context.Request.Headers.Authorization.FirstOrDefault());
            if (account is null)
            {
                return Respond(Unauthenticated());
            }

            context.Response.Headers.CacheControl = "no-store";
            return Respond(CustomerAccountResult.Success(200, new { items = service.ListInvoices(account) }));
        });
    }

    private static CustomerAccountResult Unauthenticated() =>
        CustomerAccountResult.Failure(401, "UNAUTHENTICATED", "A valid customer bearer token is required.", "REAUTHENTICATE");

    private static IResult Respond(CustomerAccountResult result) => Results.Json(result.Body, CloudJson.Options, statusCode: result.HttpStatus);

    private static async Task<T?> ReadJson<T>(HttpContext context)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(context.Request.Body, CloudJson.Options);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or FormatException)
        {
            return default;
        }
    }
}
