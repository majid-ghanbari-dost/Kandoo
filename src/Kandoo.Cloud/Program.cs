using System.Text;
using System.Text.Json;
using Kandoo.Cloud;

var builder = WebApplication.CreateBuilder(args);
// Framework request logs can include raw paths. Public capability tokens must
// never be emitted through request/routing diagnostics.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", _ => false);
builder.Logging.AddFilter("Microsoft.AspNetCore.Routing", _ => false);
var postgresConnection = builder.Configuration.GetConnectionString("CloudPostgres")
    ?? "Host=localhost;Port=5432;Database=kandoo;Username=postgres;Password=postgres;Pooling=false";
var cloudStore = new PostgresCloudStore(postgresConnection);
builder.Services.AddSingleton<ICloudStore>(cloudStore);
builder.Services.AddSingleton<IPublicInvoiceStore>(cloudStore);
var farazOptions = builder.Configuration.GetSection("FarazSms").Get<FarazSmsOptions>() ?? new FarazSmsOptions();
builder.Services.AddSingleton(farazOptions);
builder.Services.AddSingleton<ISmsProvider>(builder.Environment.IsEnvironment("Testing")
    ? (ISmsProvider)new DeterministicStubSmsProvider()
    : new FarazSmsProvider(farazOptions));
builder.Services.AddSingleton(serviceProvider => new CustomerAccountService(
    new PostgresCustomerAccountStore(postgresConnection, cloudStore),
    serviceProvider.GetRequiredService<ISmsProvider>(),
    new SystemCloudClock()));
builder.Services.AddSingleton(new PublicInvoiceRateLimiter(
    new SystemCloudClock(),
    builder.Configuration.GetValue("PublicToken:RateLimitPerMinute", 60)));

var app = builder.Build();

app.MapGet("/health", () => Results.Json(new { status = "ok" }, CloudJson.Options));

app.MapGet("/i/{token}", (
    HttpContext context,
    string token,
    IPublicInvoiceStore store,
    IHostEnvironment environment,
    PublicInvoiceRateLimiter rateLimiter) =>
{
    var rate = rateLimiter.Check(context.Connection.RemoteIpAddress?.ToString());
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.Vary = "Accept";
    if (!rate.Allowed)
    {
        context.Response.Headers.RetryAfter = rate.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Results.Json(
            CloudErrors.Failure(429, "PUBLIC_RATE_LIMITED", "Too many public invoice requests.", "RETRY_AFTER").Body,
            CloudJson.Options,
            statusCode: 429);
    }

    PublicInvoice? invoice;
    try
    {
        invoice = store.GetPublicInvoice(token);
    }
    catch (UnauthorizedAccessException)
    {
        // Unknown and unauthorized capabilities deliberately collapse here.
        invoice = null;
    }

    IResult response;
    if (invoice is null)
    {
        // Deliberately identical for malformed, unknown, and invisible tokens.
        response = Results.Json(
            CloudErrors.Failure(404, "PUBLIC_INVOICE_NOT_FOUND", "The requested invoice is not available.", "NONE").Body,
            CloudJson.Options,
            statusCode: 404);
    }
    else if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
    {
        // D14 remains a production release gate; previews use the explicit placeholder.
        response = Results.Json(
            CloudErrors.Failure(503, "LEGAL_COPY_UNAVAILABLE", "Public invoice wording is awaiting lawyer approval.", "ENABLE_LEGAL_COPY").Body,
            CloudJson.Options,
            statusCode: 503);
    }
    else if (string.Equals(context.Request.Query["format"].ToString(), "json", StringComparison.OrdinalIgnoreCase)
             || (context.Request.Headers.Accept.Any(value => value?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
                 && !context.Request.Headers.Accept.Any(value => value?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true)))
    {
        response = Results.Json(invoice.View, CloudJson.Options);
    }
    else
    {
        response = Results.Content(PublicInvoiceHtml.Render(invoice), "text/html", Encoding.UTF8);
    }

    return response;
});

app.MapPost("/v1/stores/{store_id}/operation-batches", async (
    HttpContext context,
    string store_id,
    ICloudStore store) =>
{
    CloudIngestResult result;
    if (!Guid.TryParse(store_id, out var routeStoreId))
    {
        result = CloudErrors.Failure(400, "VALIDATION_ERROR", "store_id must be a UUID.", "FIX_REQUEST");
    }
    else if (context.Request.ContentLength > PostgresCloudStore.MaxBatchBytes)
    {
        result = CloudErrors.Failure(
            413,
            "PAYLOAD_TOO_LARGE",
            "The request exceeds the batch byte limit.",
            "FIX_REQUEST",
            ("max_batch_entries", PostgresCloudStore.MaxBatchEntries),
            ("max_batch_bytes", PostgresCloudStore.MaxBatchBytes));
    }
    else
    {
        OperationBatch? batch;
        try
        {
            batch = await JsonSerializer.DeserializeAsync<OperationBatch>(context.Request.Body, CloudJson.Options);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or FormatException)
        {
            batch = null;
        }

        if (batch is null)
        {
            result = CloudErrors.Failure(400, "VALIDATION_ERROR", "The request body must be a valid operation batch.", "FIX_REQUEST");
        }
        else
        {
            var idempotencyKey = context.Request.Headers["Idempotency-Key"].FirstOrDefault();
            result = store.Ingest(routeStoreId, batch, idempotencyKey);
        }
    }

    context.Response.Headers["RateLimit-Limit"] = "120";
    context.Response.Headers["RateLimit-Remaining"] = "119";
    context.Response.Headers["RateLimit-Reset"] = "60";
    if (result.HttpStatus == 429)
    {
        context.Response.Headers["Retry-After"] = "2";
    }

    return Results.Json(result.Body, CloudJson.Options, statusCode: result.HttpStatus);
});

CustomerAccountEndpoints.Map(app);

app.Run();

public partial class Program
{
}
