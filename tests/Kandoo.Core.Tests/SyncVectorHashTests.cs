using System.Text.Json;
using Kandoo.Core;
using Xunit;

namespace Kandoo.Core.Tests;

public sealed class SyncVectorHashTests
{
    [Fact]
    public void P02_All34Vectors_RecomputeEveryHashAndHashReference()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindVectorFile()));
        var root = document.RootElement;
        Assert.Equal(34, root.GetProperty("vector_count").GetInt32());

        var requestCount = 0;
        var entryCount = 0;
        var stateCount = 0;
        var corruptedEntrySeen = false;
        var knownRequestHashes = new HashSet<string>(StringComparer.Ordinal);
        var knownEntryHashes = new HashSet<string>(StringComparer.Ordinal);
        var knownStateHashes = new HashSet<string>(StringComparer.Ordinal);
        var entriesBySequence = new Dictionary<long, string>();

        void CheckRequest(JsonElement request, string vectorId)
        {
            requestCount++;
            var actual = CanonicalJson.RequestHash(request);
            Assert.Equal(request.GetProperty("request_hash").GetString(), actual);
            knownRequestHashes.Add(actual);
        }

        void CheckEntry(JsonElement entry, string vectorId)
        {
            entryCount++;
            CheckRequest(entry, vectorId);
            var actual = CanonicalJson.EntryHash(entry);
            var supplied = entry.GetProperty("entry_hash").GetString();
            if (vectorId == "CLOUD-007")
            {
                corruptedEntrySeen = true;
                Assert.NotEqual(supplied, actual);
            }
            else
            {
                Assert.Equal(supplied, actual);
                entriesBySequence[entry.GetProperty("operation_seq").GetInt64()] = actual;
            }

            knownEntryHashes.Add(actual);
        }

        foreach (var vector in root.GetProperty("vectors").EnumerateArray())
        {
            var vectorId = vector.GetProperty("id").GetString()!;
            var input = vector.GetProperty("input");
            foreach (var step in input.GetProperty("steps").EnumerateArray())
            {
                if (step.TryGetProperty("request", out var request))
                {
                    CheckRequest(request, vectorId);
                }

                if (step.TryGetProperty("batch", out var batch) && batch.TryGetProperty("entries", out var batchEntries))
                {
                    foreach (var entry in batchEntries.EnumerateArray())
                    {
                        CheckEntry(entry, vectorId);
                    }
                }

                if (step.TryGetProperty("delivered_entries", out var deliveredEntries))
                {
                    foreach (var entry in deliveredEntries.EnumerateArray())
                    {
                        CheckEntry(entry, vectorId);
                    }
                }

                if (step.TryGetProperty("replayed_state", out var replayedState))
                {
                    stateCount++;
                    var actual = CanonicalJson.ReplayedStateHash(replayedState);
                    var expected = vector.GetProperty("expected_output").GetProperty("computed_state_hash").GetString();
                    Assert.Equal(expected, actual);
                    knownStateHashes.Add(actual);
                }
            }

            if (input.GetProperty("given").TryGetProperty("cloud", out var cloud) && cloud.TryGetProperty("entries", out var givenEntries))
            {
                foreach (var entry in givenEntries.EnumerateArray())
                {
                    CheckEntry(entry, vectorId);
                }
            }
        }

        Assert.Equal(34, requestCount);
        Assert.Equal(21, entryCount);
        Assert.Equal(2, stateCount);
        Assert.True(corruptedEntrySeen, "CLOUD-007 must remain an intentional negative fixture.");

        var fixtures = root.GetProperty("fixtures");
        Assert.Equal(fixtures.GetProperty("entry_41_hash").GetString(), entriesBySequence[41]);
        Assert.Equal(fixtures.GetProperty("entry_42_hash").GetString(), entriesBySequence[42]);
        Assert.Contains(fixtures.GetProperty("restore_state_hash").GetString()!, knownStateHashes);
        Assert.Contains(fixtures.GetProperty("restore_bad_state_hash").GetString()!, knownStateHashes);
        knownEntryHashes.Add(fixtures.GetProperty("prior_entry_hash").GetString()!);

        // Check every hash-looking reference in the document, including manifests,
        // chain links, stored operation references, and expected-output arrays.
        ValidateHashReferences(root, "$", knownRequestHashes, knownEntryHashes, knownStateHashes);

        var cloud007 = root.GetProperty("vectors").EnumerateArray().Single(v => v.GetProperty("id").GetString() == "CLOUD-007");
        var corrupted = cloud007.GetProperty("input").GetProperty("steps")[0].GetProperty("batch").GetProperty("entries")[0];
        var recomputed = CanonicalJson.EntryHash(corrupted);
        Assert.Equal(cloud007.GetProperty("expected_output").GetProperty("recomputed_entry_hash").GetString(), recomputed);
        Assert.Equal(cloud007.GetProperty("expected_output").GetProperty("supplied_entry_hash").GetString(), corrupted.GetProperty("entry_hash").GetString());
        Assert.NotEqual(corrupted.GetProperty("entry_hash").GetString(), recomputed);
    }

    private static void ValidateHashReferences(
        JsonElement element,
        string path,
        HashSet<string> requestHashes,
        HashSet<string> entryHashes,
        HashSet<string> stateHashes)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var child in element.EnumerateArray())
            {
                ValidateHashReferences(child, $"{path}[{index++}]", requestHashes, entryHashes, stateHashes);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in element.EnumerateObject())
        {
            var childPath = $"{path}.{property.Name}";
            if (IsHash(property.Value))
            {
                var value = property.Value.GetString()!;
                switch (property.Name)
                {
                    case "request_hash":
                    case "stored_request_hash":
                    case "product_request_hash":
                        Assert.True(requestHashes.Contains(value), $"Unknown request hash at {childPath}");
                        break;
                    case "entry_hash":
                    case "entry_41_hash":
                    case "entry_42_hash":
                    case "previous_entry_hash":
                    case "head_entry_hash":
                    case "supplied_entry_hash":
                    case "recomputed_entry_hash":
                        Assert.True(entryHashes.Contains(value), $"Unknown entry hash at {childPath}");
                        break;
                    case "state_hash":
                    case "manifest_state_hash":
                    case "computed_state_hash":
                    case "replayed_state_hash":
                    case "restore_state_hash":
                    case "restore_bad_state_hash":
                        Assert.True(stateHashes.Contains(value), $"Unknown state hash at {childPath}");
                        break;
                }
            }

            if (property.Name == "entry_hashes" && property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var hash in property.Value.EnumerateArray())
                {
                    Assert.True(entryHashes.Contains(hash.GetString()!), $"Unknown entry hash reference at {childPath}");
                }
            }

            ValidateHashReferences(property.Value, childPath, requestHashes, entryHashes, stateHashes);
        }
    }

    private static bool IsHash(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.String &&
               element.GetString() is { Length: 64 } value &&
               value.All(Uri.IsHexDigit);
    }

    private static string FindVectorFile()
    {
        var starts = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
        foreach (var start in starts)
        {
            DirectoryInfo? directory = new(start);
            for (var depth = 0; directory is not null && depth < 12; depth++, directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, "docs", "sync-protocol-test-vectors.json");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new FileNotFoundException("Could not locate docs/sync-protocol-test-vectors.json from the test output or working directory.");
    }
}
