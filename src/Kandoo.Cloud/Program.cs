using System.Text.Json;
using Kandoo.Cloud;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<InMemoryCloudStore>();

var app = builder.Build();

app.MapGet("/health", () => Results.Json(new { status = "ok" }, CloudJson.Options));

app.MapPost("/v1/stores/{store_id}/operation-batches", async (
    HttpContext context,
    string store_id,
    InMemoryCloudStore store) =>
{
    CloudIngestResult result;
    if (!Guid.TryParse(store_id, out var routeStoreId))
    {
        result = CloudErrors.Failure(400, "VALIDATION_ERROR", "store_id must be a UUID.", "FIX_REQUEST");
    }
    else if (context.Request.ContentLength > InMemoryCloudStore.MaxBatchBytes)
    {
        result = CloudErrors.Failure(
            413,
            "PAYLOAD_TOO_LARGE",
            "The request exceeds the batch byte limit.",
            "FIX_REQUEST",
            ("max_batch_entries", InMemoryCloudStore.MaxBatchEntries),
            ("max_batch_bytes", InMemoryCloudStore.MaxBatchBytes));
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

app.Run();

public partial class Program
{
}
