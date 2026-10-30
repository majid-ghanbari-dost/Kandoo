using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Kandoo.Hub;

/// <summary>Keeps the latest short-lived pairing payload available only on the local console/page.</summary>
public sealed class HubPairingGrantDisplay
{
    private readonly object _gate = new();
    private JsonElement? _current;

    public void Publish(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("A pairing payload must be an object.", nameof(payload));
        }

        lock (_gate)
        {
            _current = payload.Clone();
        }

        // This explicit operator-facing line is intentional; ASP.NET request
        // logging is not used to disclose credentials.
        Console.WriteLine($"KANDOO_PAIRING_GRANT={payload.GetRawText()}");
    }

    public JsonElement? Current
    {
        get
        {
            lock (_gate)
            {
                return _current?.Clone();
            }
        }
    }
}

public static class HubHostEndpoints
{
    public static void MapHubHostLocalPages(this WebApplication app)
    {
        app.MapGet("/", (HttpContext context, HubPairingGrantDisplay display) =>
        {
            if (!IsLocal(context))
            {
                return Results.NotFound();
            }

            var payload = display.Current;
            var content = payload is null
                ? "No pairing grant has been issued in this process."
                : HtmlEncoder.Default.Encode(payload.Value.GetRawText());
            context.Response.Headers.CacheControl = "no-store";
            return Results.Content(
                "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Kandoo Hub pairing</title></head>" +
                "<body><h1>Kandoo Hub local pairing</h1><p>This page is available only on this machine.</p>" +
                $"<pre id=\"pairing-grant\">{content}</pre></body></html>",
                "text/html; charset=utf-8");
        });

        app.MapGet("/pairing-grant", (HttpContext context, HubPairingGrantDisplay display) =>
        {
            if (!IsLocal(context))
            {
                return Results.NotFound();
            }

            var payload = display.Current;
            context.Response.Headers.CacheControl = "no-store";
            return payload is null
                ? Results.Json(new { status = "PAIRING_GRANT_NOT_ISSUED" }, HubJson.Options, statusCode: 404)
                : Results.Json(payload.Value, HubJson.Options);
        });

        // Disabled by default. CI enables this loopback-only endpoint so its
        // smoke script can demonstrate graceful WebApplication shutdown.
        app.MapPost("/host/shutdown", (
            HttpContext context,
            IConfiguration configuration,
            IHostApplicationLifetime lifetime) =>
        {
            if (!IsLocal(context) || configuration.GetValue<bool>("Hub:LocalShutdownEnabled") != true)
            {
                return Results.NotFound();
            }

            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(250);
                lifetime.StopApplication();
            });
            return Results.Json(new { status = "HOST_STOPPING" }, HubJson.Options);
        });
    }

    private static bool IsLocal(HttpContext context) =>
        context.Connection.RemoteIpAddress is not { } address || IPAddress.IsLoopback(address);
}
