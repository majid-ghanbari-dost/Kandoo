using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Kandoo.C30.Maui;

public sealed record HubProbeResult(int PathCount, long ElapsedMilliseconds);

public static class HubProbe
{
    public static async Task<HubProbeResult> FetchAsync(Uri endpoint)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var stopwatch = Stopwatch.StartNew();
        using var response = await http.GetAsync(endpoint);
        var content = await response.Content.ReadAsByteArrayAsync();
        stopwatch.Stop();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Hub returned HTTP {(int)response.StatusCode}.");
        }

        using var document = JsonDocument.Parse(content);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("openapi", out _) ||
            !document.RootElement.TryGetProperty("paths", out var paths) ||
            paths.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("The Hub response is not an OpenAPI document.");
        }

        return new HubProbeResult(paths.EnumerateObject().Count(), stopwatch.ElapsedMilliseconds);
    }

    public static async Task<int> RunAsync(string endpoint, string outputPath)
    {
        var result = await FetchAsync(new Uri(endpoint));
        var evidence = new
        {
            status = "PASS",
            technology = "MAUI",
            endpoint,
            result.PathCount,
            result.ElapsedMilliseconds
        };
        await File.WriteAllTextAsync(
            outputPath,
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }) + "\n",
            new UTF8Encoding(false));
        Console.WriteLine($"C30_PROBE PASS technology=MAUI paths={result.PathCount} elapsed_ms={result.ElapsedMilliseconds}");
        return 0;
    }
}
