using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kandoo.Hub;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    string? outputPath = null;
    try
    {
        var arguments = Parse(args);
        outputPath = arguments.Output;
        var repository = RequireDirectory(arguments.PythonRepository, "Python repository");
        var expectedPath = Path.Combine(repository,
            "docs", "extraction-bridge", "golden-pack", "v1", "expected", "extract-capture-folder-29.json");
        var schemaPath = Path.Combine(repository,
            "schemas", "extraction-bridge", "v1", "capture-folder-result.schema.json");
        var expected = await File.ReadAllBytesAsync(expectedPath);
        var schema = await File.ReadAllBytesAsync(schemaPath);
        if (expected.Length != ExtractionBridgeContract.CaptureFolderGoldenBytes ||
            Hash(expected) != ExtractionBridgeContract.CaptureFolderGoldenSha256)
        {
            throw new InvalidDataException("E-30 capture-folder golden bytes/hash changed.");
        }

        if (Hash(schema) != ExtractionBridgeContract.SchemaSha256)
        {
            throw new InvalidDataException("E-30 capture-folder schema hash changed.");
        }

        var options = new ExtractorBridgeOptions
        {
            ExecutablePath = RequireFile(arguments.PythonExecutable, "Python executable"),
            PythonModuleMode = true,
            WorkingDirectory = repository,
            Environment =
            {
                ["PYTHONPATH"] = string.Join(Path.PathSeparator,
                    Path.Combine(repository, "src"), Path.Combine(repository, "scripts")),
                ["PYTHONDONTWRITEBYTECODE"] = "1"
            }
        };
        var bridge = new CaptureFolderExtractionBridge(options);
        const string captureFolder = "docs/extraction-bridge/golden-pack/v1/fixtures/capture-folder";
        const string bank = "docs/extraction-bridge/golden-pack/v1/fixtures/e05-layout-bank.json";
        var result = await bridge.ExtractAsync(new CaptureFolderExtractionRequest(captureFolder, bank));
        if (result.Disposition != CaptureFolderBridgeDisposition.Review ||
            result.Code != "EXTRACTION_REVIEW" || result.ExitCode != 10 || result.Document is not { } document)
        {
            throw new InvalidDataException($"C# bridge did not route the golden to REVIEW/exit 10: {result.Code}/{result.Detail}");
        }

        if (result.StandardError.Length == 0)
        {
            throw new InvalidDataException("Capture-folder REVIEW must retain the human-readable stderr summary.");
        }

        if (!expected.AsSpan().SequenceEqual(result.StandardOutput))
        {
            var mismatch = MismatchOffset(expected, result.StandardOutput);
            throw new InvalidDataException(
                $"Golden byte mismatch at offset {mismatch}; actual={result.StandardOutput.Length}, expected={expected.Length}.");
        }

        if (document.GetProperty("capture_folder").GetString() != captureFolder ||
            document.GetProperty("status").GetString() != "REVIEW" ||
            document.GetProperty("verdict").GetString() != "REVIEW")
        {
            throw new InvalidDataException("Golden capture path/status projection changed.");
        }

        var report = new
        {
            status = "PASS",
            contract = ExtractionBridgeContract.ContractId,
            source_commit = ExtractionBridgeContract.SourceCommit,
            schema_sha256 = Hash(schema),
            golden = "extract-capture-folder-29.json",
            golden_sha256 = Hash(expected),
            golden_bytes = expected.Length,
            exit_code = result.ExitCode,
            bridge_disposition = result.Disposition.ToString().ToUpperInvariant(),
            result_status = document.GetProperty("status").GetString(),
            verdict = document.GetProperty("verdict").GetString(),
            stderr_bytes = result.StandardError.Length,
            native_effects = false,
            evidence_boundary = "Synthetic golden mechanics only; not authentic SPL/SHD or production accuracy evidence."
        };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n";
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, json, new UTF8Encoding(false));
        Console.WriteLine(
            $"C21_EXTRACTION_BRIDGE_PASS exit={result.ExitCode} bytes={expected.Length} sha256={Hash(expected)}");
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"C21_PROBE_FAILURE {exception.GetType().Name}: {exception.Message}");
        return 1;
    }
}

static (string PythonRepository, string PythonExecutable, string Output) Parse(string[] args)
{
    string? repository = null;
    string? executable = null;
    string? output = null;
    for (var index = 0; index < args.Length; index += 2)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException("Probe arguments must be name/value pairs.");
        }

        switch (args[index])
        {
            case "--python-repo": repository = args[index + 1]; break;
            case "--python-executable": executable = args[index + 1]; break;
            case "--output": output = args[index + 1]; break;
            default: throw new ArgumentException($"Unknown probe argument: {args[index]}");
        }
    }

    if (repository is null || executable is null || output is null)
    {
        throw new ArgumentException("--python-repo, --python-executable, and --output are required.");
    }

    return (Path.GetFullPath(repository), Path.GetFullPath(executable), Path.GetFullPath(output));
}

static string RequireDirectory(string path, string label) =>
    Directory.Exists(path) ? path : throw new DirectoryNotFoundException($"{label} is missing: {path}");

static string RequireFile(string path, string label) =>
    File.Exists(path) ? path : throw new FileNotFoundException($"{label} is missing.", path);

static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

static int MismatchOffset(byte[] expected, byte[] actual)
{
    var shared = Math.Min(expected.Length, actual.Length);
    for (var index = 0; index < shared; index++)
    {
        if (expected[index] != actual[index])
        {
            return index;
        }
    }

    return shared;
}
