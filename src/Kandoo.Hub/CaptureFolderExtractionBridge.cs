using System.Text.Json;

namespace Kandoo.Hub;

/// <summary>
/// C# caller for E-30 extract --capture-folder. Process/result failures never
/// become an invoice verdict and no native sale, customer, or stock is written.
/// </summary>
public sealed class CaptureFolderExtractionBridge
{
    private readonly ExtractorBridgeOptions _options;
    private readonly IExtractorProcessRunner _runner;

    public CaptureFolderExtractionBridge(
        ExtractorBridgeOptions options,
        IExtractorProcessRunner? runner = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _runner = runner ?? new ExtractorProcessRunner();
    }

    public async Task<CaptureFolderExtractionResult> ExtractAsync(
        CaptureFolderExtractionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.CaptureFolder) ||
            HasEmptyOptional(request.E05Bank) || HasEmptyOptional(request.CapturedAt) ||
            HasEmptyOptional(request.Templates) || HasEmptyOptional(request.DriftBaseline))
        {
            return Failure("VALIDATION_ERROR", null, "CAPTURE_FOLDER_ARGUMENTS_INVALID");
        }

        if (string.IsNullOrWhiteSpace(_options.ExecutablePath))
        {
            return Failure("EXTRACTOR_NOT_CONFIGURED", null, "PYTHON_EXTRACTOR_PATH_EMPTY");
        }

        if (!Path.IsPathFullyQualified(_options.ExecutablePath) ||
            (!string.IsNullOrEmpty(_options.WorkingDirectory) && !Path.IsPathFullyQualified(_options.WorkingDirectory)) ||
            _options.Timeout <= TimeSpan.Zero || _options.Timeout > ExtractionBridgeContract.MaximumTimeout)
        {
            return Failure("EXTRACTOR_CONFIGURATION_INVALID", null, "QUALIFIED_PATH_AND_TIMEOUT_REQUIRED");
        }

        var arguments = new List<string>();
        if (_options.PythonModuleMode)
        {
            arguments.Add("-m");
            arguments.Add("image_extraction_adapter.cli");
        }

        arguments.Add("extract");
        arguments.Add("--capture-folder");
        arguments.Add(request.CaptureFolder);
        AddOptional(arguments, "--e05-bank", request.E05Bank);
        AddOptional(arguments, "--captured-at", request.CapturedAt);
        AddOptional(arguments, "--templates", request.Templates);
        AddOptional(arguments, "--drift-baseline", request.DriftBaseline);
        var invocation = new ExtractorProcessInvocation(
            _options.ExecutablePath,
            arguments,
            string.IsNullOrEmpty(_options.WorkingDirectory) ? Directory.GetCurrentDirectory() : _options.WorkingDirectory,
            _options.Environment);

        ExtractorProcessRun run;
        try
        {
            run = await _runner.RunAsync(invocation, _options.Timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Failure("EXTRACTOR_PROCESS_FAILURE", null, "PROCESS_START_OR_IO_FAILURE");
        }

        if (run.TimedOut)
        {
            // Partial output is deliberately discarded. Timeout is REVIEW, never
            // a completed extraction or an invoice-level REJECT decision.
            return new CaptureFolderExtractionResult(
                CaptureFolderBridgeDisposition.Review,
                "EXTRACTOR_TIMEOUT",
                null,
                null,
                Array.Empty<byte>(),
                Array.Empty<byte>());
        }

        if (run.ExitCode is 0 or 10 or 20)
        {
            if (!TryReadDocument(run.StandardOutput, out var document, out var detail) ||
                !CaptureFolderResultValidator.TryValidate(document, out detail))
            {
                return Failure("EXTRACTOR_OUTPUT_INVALID", run.ExitCode, detail, run);
            }

            var status = document.GetProperty("status").GetString();
            var verdict = document.GetProperty("verdict").GetString();
            var matchesExit = run.ExitCode switch
            {
                0 => status == "ACCEPT" && verdict == "ACCEPT",
                10 => status == "REVIEW" && verdict == "REVIEW",
                20 => status != "ERROR" && (status == "BLOCKED" || verdict == "REJECT"),
                _ => false
            };
            if (!matchesExit)
            {
                return Failure("EXTRACTOR_OUTPUT_INVALID", run.ExitCode, "EXIT_RESULT_MISMATCH", run);
            }

            var disposition = run.ExitCode switch
            {
                0 => CaptureFolderBridgeDisposition.Completed,
                10 => CaptureFolderBridgeDisposition.Review,
                _ => CaptureFolderBridgeDisposition.Blocked
            };
            var code = run.ExitCode switch
            {
                0 => "EXTRACTION_COMPLETED",
                10 => "EXTRACTION_REVIEW",
                _ => "EXTRACTION_BLOCKED"
            };
            return new CaptureFolderExtractionResult(
                disposition, code, run.ExitCode, document, run.StandardOutput, run.StandardError);
        }

        if (run.ExitCode == 30)
        {
            if (run.StandardOutput.Length == 0)
            {
                return Failure("EXTRACTOR_ERROR", run.ExitCode, "CLI_INPUT_OR_RUNTIME_ERROR", run);
            }

            if (!TryReadDocument(run.StandardOutput, out var document, out var detail) ||
                !CaptureFolderResultValidator.TryValidate(document, out detail))
            {
                return Failure("EXTRACTOR_OUTPUT_INVALID", run.ExitCode, detail, run);
            }

            if (document.GetProperty("status").GetString() != "ERROR")
            {
                return Failure("EXTRACTOR_OUTPUT_INVALID", run.ExitCode, "EXIT_RESULT_MISMATCH", run);
            }

            return new CaptureFolderExtractionResult(
                CaptureFolderBridgeDisposition.Error,
                "EXTRACTOR_ERROR",
                run.ExitCode,
                document,
                run.StandardOutput,
                run.StandardError);
        }

        return Failure("EXTRACTOR_UNEXPECTED_EXIT", run.ExitCode, "UNEXPECTED_PROCESS_EXIT", run);
    }

    private static bool TryReadDocument(byte[] output, out JsonElement document, out string? detail)
    {
        document = default;
        detail = null;
        if (output.Length == 0 || output[^1] != (byte)'\n')
        {
            detail = "STDOUT_FINAL_LF_REQUIRED";
            return false;
        }

        if (output.Length >= 3 && output[0] == 0xEF && output[1] == 0xBB && output[2] == 0xBF)
        {
            detail = "STDOUT_BOM_FORBIDDEN";
            return false;
        }

        if (output.Any(value => value == (byte)'\r'))
        {
            detail = "STDOUT_CR_FORBIDDEN";
            return false;
        }

        try
        {
            using var parsed = JsonDocument.Parse(output);
            document = parsed.RootElement.Clone();
            if (document.ValueKind != JsonValueKind.Object)
            {
                detail = "ROOT_NOT_OBJECT";
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            detail = "STDOUT_JSON_INVALID";
            return false;
        }
    }

    private static CaptureFolderExtractionResult Failure(
        string code,
        int? exitCode,
        string? detail,
        ExtractorProcessRun? run = null) =>
        new(
            CaptureFolderBridgeDisposition.Error,
            code,
            exitCode,
            null,
            run?.StandardOutput ?? Array.Empty<byte>(),
            run?.StandardError ?? Array.Empty<byte>(),
            detail);

    private static void AddOptional(List<string> arguments, string name, string? value)
    {
        if (value is not null)
        {
            arguments.Add(name);
            arguments.Add(value);
        }
    }

    private static bool HasEmptyOptional(string? value) => value is not null && value.Length == 0;
}
