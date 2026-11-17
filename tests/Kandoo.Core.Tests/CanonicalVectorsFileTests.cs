using System.Text;
using System.Text.Json;
using Kandoo.Core;
using Xunit;

namespace Kandoo.Core.Tests;

/// <summary>Permanent CI test for the C-66 canonical JSON + SHA-256 conformance vectors.</summary>
/// <remarks>
/// Loads docs/vectors/canonical-vectors-v1.json directly from the repository and, for every
/// one of the 57 vectors (V1-V12, C00-C1F, X1-X13): parses input_json, reproduces the
/// canonical form with CanonicalJson.Serialize, matches canonical_hex byte-for-byte, compares
/// SHA-256 of the canonical bytes with the sha256 field, and asserts the total count is 57.
/// This gives CI-permanent coverage of the C00-C1F and X1-X13 families that the pinned
/// CanonicalJsonU7Tests (V1-V12 only) does not reach.
/// </remarks>
public sealed class CanonicalVectorsFileTests
{
    private const int ExpectedVectorCount = 57;
    private const string RelativeVectorsPath = "docs/vectors/canonical-vectors-v1.json";

    [Fact]
    public void AllVectorsInFileMatchCanonicalJson()
    {
        var vectorsFile = ResolveVectorsFile();
        var json = File.ReadAllText(vectorsFile);
        using var doc = JsonDocument.Parse(json);
        var vectors = doc.RootElement.GetProperty("vectors").EnumerateArray().ToList();

        // (ت) total count is 57
        Assert.Equal(ExpectedVectorCount, vectors.Count);

        var mismatches = new List<string>();
        foreach (var vector in vectors)
        {
            var id = vector.GetProperty("id").GetString() ?? "<unknown>";
            var inputJson = vector.GetProperty("input_json").GetString() ?? string.Empty;
            var expectedCanon = vector.GetProperty("canonical_hex").GetString() ?? string.Empty;
            var expectedSha = vector.GetProperty("sha256").GetString() ?? string.Empty;

            // (الف) input_json is one JSON value that IS the input document
            using var inputDoc = JsonDocument.Parse(inputJson);
            var element = inputDoc.RootElement.Clone();

            // (ب) reproduce canonical form with CanonicalJson.Serialize
            var canonical = CanonicalJson.Serialize(element);
            var actualCanon = Convert.ToHexString(
                Encoding.UTF8.GetBytes(canonical)).ToLowerInvariant();

            // (پ) SHA-256 of canonical bytes equals the sha256 field
            var actualSha = CanonicalJson.Sha256Hex(element);

            if (actualCanon != expectedCanon)
                mismatches.Add($"{id}: canonical hex (actual={actualCanon}, expected={expectedCanon})");
            if (actualSha != expectedSha)
                mismatches.Add($"{id}: sha256 (actual={actualSha}, expected={expectedSha})");
        }

        Assert.Empty(mismatches);
    }

    private static string ResolveVectorsFile()
    {
        // Walk up from the test output directory until the repository root (which contains
        // docs/vectors/canonical-vectors-v1.json) is found.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, RelativeVectorsPath);
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate {RelativeVectorsPath} by walking up from {AppContext.BaseDirectory}.");
    }
}
