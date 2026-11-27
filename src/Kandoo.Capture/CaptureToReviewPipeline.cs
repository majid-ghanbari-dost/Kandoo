using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Kandoo.Capture;

public sealed record CaptureBatchProcessResult(
    CaptureBatchTriage Triage,
    IReadOnlyList<CaptureReviewPackage> ReviewPackages);

/// <summary>
/// End-to-end Mode-B pipeline: validates capture folder/batch intake, runs optional E-30 extraction,
/// assembles strict canonical_invoice_v0 + "what I read" sidecar evidence, and forwards to a review queue sink.
/// </summary>
public sealed class CaptureToReviewPipeline
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    private readonly ICaptureFolderExtractor? _extractor;
    private readonly Func<CaptureReviewPackage, CancellationToken, Task>? _onReviewReadyAsync;
    private readonly bool _persistSidecars;

    public CaptureToReviewPipeline(
        ICaptureFolderExtractor? extractor = null,
        Func<CaptureReviewPackage, CancellationToken, Task>? onReviewReadyAsync = null,
        bool persistSidecars = true)
    {
        _extractor = extractor;
        _onReviewReadyAsync = onReviewReadyAsync;
        _persistSidecars = persistSidecars;
    }

    public async Task<CaptureReviewPackage> ProcessCaptureFolderAsync(
        string captureFolderPath,
        CaptureProducerContext context,
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var inspection = CaptureIntakeValidator.InspectFolder(captureFolderPath);

        CaptureExtractionRunResult? extractionRun = null;
        if (inspection.IsValid && _extractor is not null)
        {
            DateTimeOffset? capturedAt = null;
            if (inspection.Report?.CapturedAtIso is not null &&
                DateTimeOffset.TryParse(inspection.Report.CapturedAtIso, out var parsedDto))
            {
                capturedAt = parsedDto;
            }

            extractionRun = await _extractor.ExtractAsync(inspection.CaptureFolderPath, capturedAt, cancellationToken).ConfigureAwait(false);
        }

        var effectiveContext = EnrichXpsTextIfPresent(inspection, context);
        var package = CaptureReviewItemAssembler.Assemble(effectiveContext, inspection, extractionRun, now);

        if (_persistSidecars && Directory.Exists(inspection.CaptureFolderPath))
        {
            var canonicalPath = Path.Combine(inspection.CaptureFolderPath, "canonical_invoice_v0.json");
            var evidencePath = Path.Combine(inspection.CaptureFolderPath, "review_evidence.json");
            await File.WriteAllTextAsync(canonicalPath, package.CanonicalInvoiceJson + "\n", Utf8NoBom, cancellationToken).ConfigureAwait(false);
            var evidenceJson = JsonSerializer.Serialize(package.Evidence, PrettyJson);
            await File.WriteAllTextAsync(evidencePath, evidenceJson + "\n", Utf8NoBom, cancellationToken).ConfigureAwait(false);
        }

        if (_onReviewReadyAsync is not null)
            await _onReviewReadyAsync(package, cancellationToken).ConfigureAwait(false);

        return package;
    }

    public async Task<CaptureBatchProcessResult> ProcessBatchAsync(
        string batchRootDirectory,
        Func<CaptureIntakeInspection, CaptureProducerContext> contextFactory,
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        var triage = CaptureIntakeValidator.InspectBatch(batchRootDirectory);
        var packages = new List<CaptureReviewPackage>();

        foreach (var ready in triage.ReadyJobs)
        {
            var ctx = EnrichXpsTextIfPresent(ready, contextFactory(ready));
            CaptureExtractionRunResult? run = null;
            if (_extractor is not null)
            {
                DateTimeOffset? capturedAt = null;
                if (ready.Report?.CapturedAtIso is not null &&
                    DateTimeOffset.TryParse(ready.Report.CapturedAtIso, out var parsedDto))
                {
                    capturedAt = parsedDto;
                }

                run = await _extractor.ExtractAsync(ready.CaptureFolderPath, capturedAt, cancellationToken).ConfigureAwait(false);
            }

            var pkg = CaptureReviewItemAssembler.Assemble(ctx, ready, run, now);
            await PersistAndNotifyAsync(ready.CaptureFolderPath, pkg, cancellationToken).ConfigureAwait(false);
            packages.Add(pkg);
        }

        foreach (var rejected in triage.RejectedJobs)
        {
            var ctx = EnrichXpsTextIfPresent(rejected, contextFactory(rejected));
            var pkg = CaptureReviewItemAssembler.Assemble(ctx, rejected, null, now);
            await PersistAndNotifyAsync(rejected.CaptureFolderPath, pkg, cancellationToken).ConfigureAwait(false);
            packages.Add(pkg);
        }

        return new CaptureBatchProcessResult(triage, packages);
    }

    private async Task PersistAndNotifyAsync(
        string folderPath,
        CaptureReviewPackage package,
        CancellationToken cancellationToken)
    {
        if (_persistSidecars && Directory.Exists(folderPath))
        {
            var canonicalPath = Path.Combine(folderPath, "canonical_invoice_v0.json");
            var evidencePath = Path.Combine(folderPath, "review_evidence.json");
            await File.WriteAllTextAsync(canonicalPath, package.CanonicalInvoiceJson + "\n", Utf8NoBom, cancellationToken).ConfigureAwait(false);
            var evidenceJson = JsonSerializer.Serialize(package.Evidence, PrettyJson);
            await File.WriteAllTextAsync(evidencePath, evidenceJson + "\n", Utf8NoBom, cancellationToken).ConfigureAwait(false);
        }

        if (_onReviewReadyAsync is not null)
            await _onReviewReadyAsync(package, cancellationToken).ConfigureAwait(false);
    }

    private static CaptureProducerContext EnrichXpsTextIfPresent(
        CaptureIntakeInspection inspection,
        CaptureProducerContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.XpsText) || string.IsNullOrEmpty(inspection.CaptureFolderPath))
            return context;

        var xpsPath = Path.Combine(inspection.CaptureFolderPath, "xps_text.txt");
        if (!File.Exists(xpsPath))
            return context;

        try
        {
            var text = File.ReadAllText(xpsPath, Encoding.UTF8);
            return string.IsNullOrWhiteSpace(text) ? context : context with { XpsText = text };
        }
        catch (Exception)
        {
            return context;
        }
    }
}

/// <summary>
/// ICapturedSpoolSink bridging PrintSpoolMonitor artifacts directly into E-30/E-73
/// capture folders and Mode-B review packages (fail-closed, zero native side effects).
/// </summary>
public sealed class CaptureFolderSpoolSink : ICapturedSpoolSink
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    private readonly string _captureRootDirectory;
    private readonly Guid _storeId;
    private readonly Guid _deviceId;
    private readonly CaptureToReviewPipeline _pipeline;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<byte[]>>>? _pageRendererAsync;

    public CaptureFolderSpoolSink(
        string captureRootDirectory,
        Guid storeId,
        Guid deviceId,
        ICaptureFolderExtractor? extractor = null,
        Func<CaptureReviewPackage, CancellationToken, Task>? onReviewReadyAsync = null,
        Func<string, CancellationToken, Task<IReadOnlyList<byte[]>>>? pageRendererAsync = null)
    {
        if (string.IsNullOrWhiteSpace(captureRootDirectory))
            throw new ArgumentException("Capture root directory is required.", nameof(captureRootDirectory));
        if (storeId == Guid.Empty)
            throw new ArgumentException("StoreId is required.", nameof(storeId));
        if (deviceId == Guid.Empty)
            throw new ArgumentException("DeviceId is required.", nameof(deviceId));

        _captureRootDirectory = Path.GetFullPath(captureRootDirectory);
        _storeId = storeId;
        _deviceId = deviceId;
        _pipeline = new CaptureToReviewPipeline(extractor, onReviewReadyAsync, persistSidecars: true);
        _pageRendererAsync = pageRendererAsync;
    }

    public async Task<CaptureDisposition> AcceptAsync(
        CapturedSpoolArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        var manifestPath = artifact.CapturedPath + ".review.json";
        if (artifact.Extension.Equals(".shd", StringComparison.OrdinalIgnoreCase))
        {
            var shdManifest = JsonSerializer.Serialize(new
            {
                disposition = "REVIEW",
                native_effects = false,
                artifact.SourcePath,
                artifact.CapturedPath,
                artifact.Extension,
                artifact.Size,
                artifact.Sha256,
                artifact.CapturedAt,
            }, PrettyJson);
            await File.WriteAllTextAsync(manifestPath, shdManifest + "\n", Utf8NoBom, cancellationToken).ConfigureAwait(false);
            return CaptureDisposition.Review;
        }

        IReadOnlyList<byte[]>? renderedPages = null;
        if (_pageRendererAsync is not null)
            renderedPages = await _pageRendererAsync(artifact.CapturedPath, cancellationToken).ConfigureAwait(false);

        var batchRoot = Path.Combine(_captureRootDirectory, "jobs");
        var build = await CaptureFolderBuilder.BuildFromSpoolArtifactAsync(
            artifact,
            batchRoot,
            renderedPages,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var package = await _pipeline.ProcessCaptureFolderAsync(
            build.FolderPath,
            new CaptureProducerContext(
                _storeId,
                _deviceId,
                OperationId: $"capture:{artifact.Sha256}",
                XpsText: build.XpsText),
            artifact.CapturedAt,
            cancellationToken).ConfigureAwait(false);

        var manifest = JsonSerializer.Serialize(new
        {
            disposition = "REVIEW",
            native_effects = false,
            review_id = package.ReviewId,
            capture_folder = build.FolderPath,
            canonical_invoice_path = Path.Combine(build.FolderPath, "canonical_invoice_v0.json"),
            evidence_path = Path.Combine(build.FolderPath, "review_evidence.json"),
            artifact.SourcePath,
            artifact.CapturedPath,
            artifact.Extension,
            artifact.Size,
            artifact.Sha256,
            artifact.CapturedAt,
        }, PrettyJson);
        await File.WriteAllTextAsync(manifestPath, manifest + "\n", Utf8NoBom, cancellationToken).ConfigureAwait(false);

        return CaptureDisposition.Review;
    }
}
