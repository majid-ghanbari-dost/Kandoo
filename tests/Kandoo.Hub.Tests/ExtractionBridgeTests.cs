using System.Text;
using System.Text.Json;
using Xunit;

namespace Kandoo.Hub.Tests;

public sealed class ExtractionBridgeTests
{
    [Fact]
    public async Task C21_SourceModuleArgumentsAndReviewMappingFollowE30()
    {
        var runner = new FakeRunner(_ => Run(10, ValidJson("REVIEW", "REVIEW"), "review summary"u8.ToArray()));
        var options = Options();
        options.PythonModuleMode = true;
        var bridge = new CaptureFolderExtractionBridge(options, runner);

        var result = await bridge.ExtractAsync(new CaptureFolderExtractionRequest(
            "capture folder", "bank.json", "2026-10-30T00:00:00Z", "templates.json", "baseline.json"));

        Assert.Equal(CaptureFolderBridgeDisposition.Review, result.Disposition);
        Assert.Equal("EXTRACTION_REVIEW", result.Code);
        Assert.Equal(10, result.ExitCode);
        Assert.NotNull(result.Document);
        var invocation = runner.Invocation ?? throw new InvalidOperationException("The fake runner was not invoked.");
        Assert.Equal(options.ExecutablePath, invocation.FileName);
        Assert.Equal(new[]
        {
            "-m", "image_extraction_adapter.cli", "extract", "--capture-folder", "capture folder",
            "--e05-bank", "bank.json", "--captured-at", "2026-10-30T00:00:00Z",
            "--templates", "templates.json", "--drift-baseline", "baseline.json"
        }, invocation.Arguments);
    }

    [Fact]
    public async Task C21_BlockedAndProcessErrorRemainDistinct()
    {
        var blockedRunner = new FakeRunner(_ => Run(20, ValidJson("BLOCKED", "REVIEW")));
        var blocked = await new CaptureFolderExtractionBridge(Options(), blockedRunner)
            .ExtractAsync(new CaptureFolderExtractionRequest("capture"));
        Assert.Equal(CaptureFolderBridgeDisposition.Blocked, blocked.Disposition);
        Assert.Equal("EXTRACTION_BLOCKED", blocked.Code);
        Assert.NotNull(blocked.Document);

        var errorRunner = new FakeRunner(_ => Run(30, ValidJson("ERROR", "REJECT")));
        var error = await new CaptureFolderExtractionBridge(Options(), errorRunner)
            .ExtractAsync(new CaptureFolderExtractionRequest("capture"));
        Assert.Equal(CaptureFolderBridgeDisposition.Error, error.Disposition);
        Assert.Equal("EXTRACTOR_ERROR", error.Code);
        Assert.Equal(30, error.ExitCode);
        Assert.NotNull(error.Document);
    }

    [Fact]
    public async Task C21_TimeoutDiscardsPartialOutputAndRoutesReview()
    {
        var runner = new FakeRunner(_ => new ExtractorProcessRun(
            null, ValidJson("REVIEW", "REVIEW"), "partial"u8.ToArray(), TimedOut: true));
        var result = await new CaptureFolderExtractionBridge(Options(), runner)
            .ExtractAsync(new CaptureFolderExtractionRequest("capture"));

        Assert.Equal(CaptureFolderBridgeDisposition.Review, result.Disposition);
        Assert.Equal("EXTRACTOR_TIMEOUT", result.Code);
        Assert.Null(result.ExitCode);
        Assert.Null(result.Document);
        Assert.Empty(result.StandardOutput);
        Assert.Empty(result.StandardError);
    }

    [Fact]
    public async Task C21_InvalidOutputAndExitMismatchFailClosed()
    {
        var malformed = await Invoke(10, "{}\n"u8.ToArray());
        Assert.Equal(CaptureFolderBridgeDisposition.Error, malformed.Disposition);
        Assert.Equal("EXTRACTOR_OUTPUT_INVALID", malformed.Code);

        var mismatch = await Invoke(10, ValidJson("ACCEPT", "ACCEPT"));
        Assert.Equal("EXTRACTOR_OUTPUT_INVALID", mismatch.Code);
        Assert.Equal("EXIT_RESULT_MISMATCH", mismatch.Detail);

        var missingLf = await Invoke(10, ValidJson("REVIEW", "REVIEW")[..^1]);
        Assert.Equal("STDOUT_FINAL_LF_REQUIRED", missingLf.Detail);

        var emptyError = await Invoke(30, Array.Empty<byte>());
        Assert.Equal("EXTRACTOR_ERROR", emptyError.Code);
        Assert.Null(emptyError.Document);
    }

    [Fact]
    public async Task C21_ConfigurationIsDisabledAndQualifiedPathIsRequired()
    {
        var runner = new FakeRunner(_ => throw new InvalidOperationException("must not start"));
        var disabled = await new CaptureFolderExtractionBridge(Options() with { ExecutablePath = "" }, runner)
            .ExtractAsync(new CaptureFolderExtractionRequest("capture"));
        Assert.Equal("EXTRACTOR_NOT_CONFIGURED", disabled.Code);

        var relative = await new CaptureFolderExtractionBridge(Options() with { ExecutablePath = "python" }, runner)
            .ExtractAsync(new CaptureFolderExtractionRequest("capture"));
        Assert.Equal("EXTRACTOR_CONFIGURATION_INVALID", relative.Code);
        Assert.Null(runner.Invocation);
    }

    [Fact]
    public void C21_PinnedSchemaRejectsAdditionalAndDuplicateProperties()
    {
        using var additionalDocument = JsonDocument.Parse(ValidJson("REVIEW", "REVIEW"));
        var additional = additionalDocument.RootElement.Clone();
        Assert.True(CaptureFolderResultValidator.TryValidate(additional, out _));

        var extra = JsonNodeObject(additional);
        extra["unexpected"] = true;
        Assert.False(CaptureFolderResultValidator.TryValidate(Element(extra), out var detail));
        Assert.Equal("ROOT_ADDITIONAL_PROPERTY", detail);

        var duplicate = JsonNodeObject(additional);
        duplicate["scoring_fields"] = new System.Text.Json.Nodes.JsonArray(
            System.Text.Json.Nodes.JsonValue.Create("quantity"),
            System.Text.Json.Nodes.JsonValue.Create("quantity"));
        Assert.False(CaptureFolderResultValidator.TryValidate(Element(duplicate), out detail));
        Assert.Equal("ROOT_ARRAYS_INVALID", detail);
    }

    private static async Task<CaptureFolderExtractionResult> Invoke(int exitCode, byte[] stdout)
    {
        var runner = new FakeRunner(_ => Run(exitCode, stdout));
        return await new CaptureFolderExtractionBridge(Options(), runner)
            .ExtractAsync(new CaptureFolderExtractionRequest("capture"));
    }

    private static ExtractorBridgeOptions Options() => new()
    {
        ExecutablePath = Path.GetFullPath("fake-extractor"),
        WorkingDirectory = Directory.GetCurrentDirectory()
    };

    private static ExtractorProcessRun Run(int exitCode, byte[] stdout, byte[]? stderr = null) =>
        new(exitCode, stdout, stderr ?? Array.Empty<byte>());

    private static byte[] ValidJson(string status, string verdict) => Encoding.UTF8.GetBytes($$"""
        {
          "result_version": "capture-folder-result-v1",
          "status": "{{status}}",
          "verdict": "{{verdict}}",
          "verdict_source": "test",
          "capture_folder": "capture",
          "captured_at": null,
          "scoring_fields": ["quantity", "line_total", "payable"],
          "accuracy": {
            "status": "NOT_SCORED",
            "expected_values_available": false,
            "fields": ["quantity", "line_total", "payable"],
            "reason": "no labels"
          },
          "pages": [],
          "numeric_candidates": { "quantity": [], "line_total": [], "payable": [] },
          "e06": null,
          "errors": []
        }

        """);

    private static System.Text.Json.Nodes.JsonObject JsonNodeObject(JsonElement element) =>
        System.Text.Json.Nodes.JsonNode.Parse(element.GetRawText())!.AsObject();

    private static JsonElement Element(System.Text.Json.Nodes.JsonNode node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }

    private sealed class FakeRunner : IExtractorProcessRunner
    {
        private readonly Func<ExtractorProcessInvocation, ExtractorProcessRun> _handler;

        public FakeRunner(Func<ExtractorProcessInvocation, ExtractorProcessRun> handler) => _handler = handler;

        public ExtractorProcessInvocation? Invocation { get; private set; }

        public Task<ExtractorProcessRun> RunAsync(
            ExtractorProcessInvocation invocation,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            Invocation = invocation;
            return Task.FromResult(_handler(invocation));
        }
    }
}
