using System.IO;
using System.Security.Cryptography;

namespace Kandoo.HolooConnector;

/// <summary>
/// Verifies the Holoo CSV bundle against the canonical per-file SHA-256 acceptance table
/// (committed as bundle-sha256.tsv: relative_path | size_bytes | sha256).
/// </summary>
public sealed class BundleEntry
{
    public string RelativePath { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string Sha256 { get; init; } = string.Empty;
}

public sealed class VerifyFailure
{
    public string RelativePath { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

public sealed class BundleVerifyResult
{
    public bool AllPass { get; init; }
    public IReadOnlyList<VerifyFailure> Failures { get; init; } = System.Array.Empty<VerifyFailure>();
}

public static class HolooBundleVerifier
{
    public static IReadOnlyList<BundleEntry> ReadManifest(string manifestPath)
    {
        var entries = new List<BundleEntry>();
        foreach (var raw in File.ReadAllLines(manifestPath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("relative_path", System.StringComparison.OrdinalIgnoreCase))
                continue;
            var parts = line.Split('|');
            if (parts.Length != 3)
                continue;
            entries.Add(new BundleEntry
            {
                RelativePath = parts[0].Trim(),
                SizeBytes = long.Parse(parts[1].Trim()),
                Sha256 = parts[2].Trim()
            });
        }
        return entries;
    }

    static string Sha256OfFile(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    public static BundleVerifyResult Verify(string manifestPath, string bundleRoot)
    {
        var entries = ReadManifest(manifestPath);
        var failures = new List<VerifyFailure>();
        foreach (var entry in entries)
        {
            var fullPath = Path.Combine(bundleRoot, entry.RelativePath);
            if (!File.Exists(fullPath))
            {
                failures.Add(new VerifyFailure { RelativePath = entry.RelativePath, Reason = "missing" });
                continue;
            }
            var info = new FileInfo(fullPath);
            if (info.Length != entry.SizeBytes)
            {
                failures.Add(new VerifyFailure { RelativePath = entry.RelativePath, Reason = $"size {info.Length} != {entry.SizeBytes}" });
                continue;
            }
            var actual = Sha256OfFile(fullPath);
            if (!actual.Equals(entry.Sha256, System.StringComparison.OrdinalIgnoreCase))
            {
                failures.Add(new VerifyFailure { RelativePath = entry.RelativePath, Reason = $"sha256 {actual} != {entry.Sha256}" });
                continue;
            }
        }
        return new BundleVerifyResult { AllPass = failures.Count == 0, Failures = failures };
    }
}
