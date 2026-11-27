using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kandoo.Core;
using Xunit;

namespace Kandoo.Capture.Tests;

public sealed class CaptureToReviewTests
{
    private static readonly Guid TestStoreId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid TestDeviceId = Guid.Parse("22222222-2222-4222-8222-222222222222");

    [Fact]
    public void PersianNumberParser_Parses_Persian_Arabic_Grouping_And_Rejects_Garbage()
    {
        Assert.True(PersianNumberParser.TryParseNonNegativeInt64("۷,۴۶۰,۰۰۰", out var total));
        Assert.Equal(7_460_000L, total);

        Assert.True(PersianNumberParser.TryParseNonNegativeInt64("١٬٤٥٠٬٠٠٠", out var lineTotal));
        Assert.Equal(1_450_000L, lineTotal);

        Assert.True(PersianNumberParser.TryParsePositiveQuantityMillis("۱", out var q1));
        Assert.Equal(1000L, q1);

        Assert.True(PersianNumberParser.TryParsePositiveQuantityMillis("۲٫۵", out var q2));
        Assert.Equal(2500L, q2);

        Assert.False(PersianNumberParser.TryParsePositiveQuantityMillis("۰", out _));
        Assert.False(PersianNumberParser.TryParsePositiveQuantityMillis("-1", out _));
        Assert.False(PersianNumberParser.TryParsePositiveQuantityMillis("1.2345", out _));
        Assert.False(PersianNumberParser.TryParseNonNegativeInt64("1403/08/15", out _));
        Assert.False(PersianNumberParser.TryParseNonNegativeInt64("", out _));
    }

    [Fact]
    public void CaptureFolderBuilder_Produces_Valid_E30_Folder_From_EscPos_And_Passes_Intake()
    {
        var tempDir = CreateTempDir();
        try
        {
            var escPosBytes = new byte[] { 0x1D, 0x76, 0x30, 0, 1, 0, 2, 0, 0x80, 0x01 };
            var capturedAt = new DateTimeOffset(2026, 11, 27, 10, 30, 0, TimeSpan.FromMinutes(210));
            var build = CaptureFolderBuilder.Build(new CaptureFolderBuildRequest(
                tempDir,
                "job_001",
                "spool-job-101",
                escPosBytes,
                CapturedAt: capturedAt,
                PrinterName: "Thermal-80mm",
                DocumentName: "Receipt 101"));

            Assert.True(build.Inspection.IsValid, string.Join("; ", build.Inspection.Problems.Select(p => p.Code)));
            Assert.Single(build.PagePngPaths);
            Assert.Equal("spool-job-101", build.Inspection.Report?.SpoolJobId);
            Assert.Equal(1, build.Inspection.Report?.ReportedPageCount);
            Assert.Equal(build.SplSha256, build.Inspection.Report?.ReportedSplSha256);
            Assert.Equal(8, build.Inspection.Pages[0].Width);
            Assert.Equal(2, build.Inspection.Pages[0].Height);
        }
        finally
        {
            DeleteIfExists(tempDir);
        }
    }

    [Fact]
    public void CaptureFolderBuilder_Extracts_OpenXps_Text_When_Supplied()
    {
        var tempDir = CreateTempDir();
        try
        {
            byte[] xpsBytes;
            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var entry = zip.CreateEntry("Documents/1/Pages/1.fpage");
                    using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                    writer.Write("""
                        <FixedPage xmlns="http://schemas.openxps.org/oxps/v1.0">
                          <Glyphs OriginX="10" OriginY="10" UnicodeString="باتک" />
                        </FixedPage>
                        """);
                }

                xpsBytes = ms.ToArray();
            }

            var dummyPng = MakeValidPngBytes();
            var build = CaptureFolderBuilder.Build(new CaptureFolderBuildRequest(
                tempDir,
                "job_xps",
                "xps-job-1",
                xpsBytes,
                ExplicitPagePngs: [dummyPng]));

            Assert.True(build.Inspection.IsValid);
            Assert.Equal("کتاب", build.XpsText);
            Assert.NotNull(build.XpsTextPath);
            Assert.True(File.Exists(build.XpsTextPath));
        }
        finally
        {
            DeleteIfExists(tempDir);
        }
    }

    [Fact]
    public void CaptureIntakeValidator_Catches_Hash_Mismatch_Page_Count_Mismatch_And_Corrupt_Png()
    {
        var tempDir = CreateTempDir();
        try
        {
            var jobDir = Path.Combine(tempDir, "bad_job");
            Directory.CreateDirectory(jobDir);
            var spl = new byte[] { 0x1B, 0x40, 0x0A };
            File.WriteAllBytes(Path.Combine(jobDir, "print.spl"), spl);
            File.WriteAllBytes(Path.Combine(jobDir, "page_001.png"), Encoding.ASCII.GetBytes("NOT_A_PNG"));
            File.WriteAllText(Path.Combine(jobDir, "Report.txt"), $"""
                Spool job ID: job-bad
                Timestamp: 2026-11-27T10:00:00+00:00
                Page count: 2
                SPL SHA-256: {new string('0', 64)}
                """);

            var inspection = CaptureIntakeValidator.InspectFolder(jobDir);
            Assert.False(inspection.IsValid);
            var codes = inspection.Problems.Select(p => p.Code).ToHashSet(StringComparer.Ordinal);
            Assert.Contains("INVALID_PNG", codes);
            Assert.Contains("SPL_HASH_MISMATCH", codes);
            Assert.NotEmpty(CaptureIntakeValidator.FormatPlainTriageReasons(inspection.Problems));
        }
        finally
        {
            DeleteIfExists(tempDir);
        }
    }

    [Fact]
    public void CaptureIntakeValidator_BatchTriage_Holds_All_Duplicates_Unassigned_And_Missing_JobId()
    {
        var tempDir = CreateTempDir();
        try
        {
            var spl = new byte[] { 0x1D, 0x76, 0x30, 0, 1, 0, 1, 0, 0xFF };
            CaptureFolderBuilder.Build(new CaptureFolderBuildRequest(tempDir, "job_a", "dup-77", spl));
            CaptureFolderBuilder.Build(new CaptureFolderBuildRequest(tempDir, "job_b", "dup-77", spl));
            CaptureFolderBuilder.Build(new CaptureFolderBuildRequest(tempDir, "job_ok", "ok-88", spl));

            // Folder named Unassigned must be rejected
            var unassigned = Path.Combine(tempDir, "Unassigned");
            Directory.CreateDirectory(unassigned);
            File.WriteAllBytes(Path.Combine(unassigned, "u.spl"), spl);
            File.WriteAllBytes(Path.Combine(unassigned, "page_001.png"), MakeValidPngBytes());
            File.WriteAllText(Path.Combine(unassigned, "Report.txt"), $"""
                Spool job ID: unassigned-1
                Timestamp: 2026-11-27T10:00:00Z
                Page count: 1
                SPL SHA-256: {Convert.ToHexString(SHA256.HashData(spl)).ToLowerInvariant()}
                """);

            // Folder missing Spool job ID in Report.txt
            var noId = Path.Combine(tempDir, "job_noid");
            Directory.CreateDirectory(noId);
            File.WriteAllBytes(Path.Combine(noId, "noid.spl"), spl);
            File.WriteAllBytes(Path.Combine(noId, "page_001.png"), MakeValidPngBytes());
            File.WriteAllText(Path.Combine(noId, "Report.txt"), $"""
                Timestamp: 2026-11-27T10:00:00Z
                Page count: 1
                SPL SHA-256: {Convert.ToHexString(SHA256.HashData(spl)).ToLowerInvariant()}
                """);

            var batch = CaptureIntakeValidator.InspectBatch(tempDir);
            Assert.False(batch.AllValid);
            Assert.Single(batch.ReadyJobs);
            Assert.Equal("job_ok", batch.ReadyJobs[0].FolderName);

            Assert.Equal(4, batch.RejectedJobs.Count);
            Assert.Contains(batch.RejectedJobs, r => r.FolderName == "job_a" && r.Problems.Any(p => p.Code == "DUPLICATE_JOB_ID"));
            Assert.Contains(batch.RejectedJobs, r => r.FolderName == "job_b" && r.Problems.Any(p => p.Code == "DUPLICATE_JOB_ID"));
            Assert.Contains(batch.RejectedJobs, r => r.FolderName == "Unassigned" && r.Problems.Any(p => p.Code == "JOB_IN_WRONG_FOLDER"));
            Assert.Contains(batch.RejectedJobs, r => r.FolderName == "job_noid" && r.Problems.Any(p => p.Code == "MISSING_JOB_ID"));
        }
        finally
        {
            DeleteIfExists(tempDir);
        }
    }

    [Fact]
    public void CaptureReviewItemAssembler_Converts_Pinned_Golden_29_Into_Strict_Canonical_Invoice_V0()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "extract-capture-folder-29.json");
        var rawText = File.ReadAllText(fixturePath, Encoding.UTF8).Replace("\r\n", "\n", StringComparison.Ordinal);
        var normalizedSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawText))).ToLowerInvariant();
        Assert.Equal("336f33fab4997da2963d72c0a07d3589cf0baeb3d61f418042f596748640219e", normalizedSha);

        using var doc = JsonDocument.Parse(rawText);
        var extractionRun = new CaptureExtractionRunResult(
            CaptureExtractionDisposition.Review,
            "EXTRACTION_REVIEW",
            10,
            doc.RootElement.Clone());

        var pkg = CaptureReviewItemAssembler.Assemble(
            new CaptureProducerContext(TestStoreId, TestDeviceId),
            inspection: null,
            extractionRun: extractionRun,
            now: new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero));

        // Must be strictly valid per Kandoo.Core CanonicalInvoiceConsumer
        var consumed = CanonicalInvoiceConsumer.ConsumeForReview(pkg.CanonicalInvoiceJson);
        Assert.Equal("REVIEW", consumed.QueueStatus);
        Assert.Equal("EXTRACTED", consumed.SourceState);
        Assert.Null(consumed.DisplayNumber);
        Assert.Equal(7_460_000L, consumed.PayableRials);
        Assert.Equal(4, consumed.LineItems.Count);
        Assert.Equal(1000L, consumed.LineItems[0].QuantityMillis);
        Assert.Equal(1_450_000L, consumed.LineItems[0].UnitPriceRials);
        Assert.Equal(1_450_000L, consumed.LineItems[0].LineTotalRials);
        Assert.Equal(4_870_000L, consumed.LineItems[1].LineTotalRials);
        Assert.Equal(570_000L, consumed.LineItems[2].LineTotalRials);
        Assert.Equal(570_000L, consumed.LineItems[3].LineTotalRials);

        // Fail-closed attention cells must require operator phone, product names, unit prices, and currency confirmation
        var attentionFields = consumed.Source.GetProperty("attention_cells")
            .EnumerateArray()
            .Select(c => (Field: c.GetProperty("field").GetString()!, Reason: c.GetProperty("reason").GetString()!))
            .ToList();

        Assert.Contains(attentionFields, a => a.Field == "customer.phone" && a.Reason == "OPERATOR_PHONE_REQUIRED");
        Assert.Contains(attentionFields, a => a.Field == "line_items/0/product_name");
        Assert.Contains(attentionFields, a => a.Field == "line_items/0/unit_price" && a.Reason == "DERIVED_UNIT_PRICE_CONFIRMATION_REQUIRED");
        Assert.Contains(attentionFields, a => a.Field == "summary/payable" && a.Reason.Contains("CURRENCY_UNIT_UNCONFIRMED", StringComparison.Ordinal));

        Assert.Equal(
            "61d6d16594ca35d622099ad5c453292d0279127ee65744ce784952b3a781586a",
            consumed.Source.GetProperty("provenance").GetProperty("image_sha256").GetString());
        Assert.Equal(
            "b42688770113faaba9e4024e3af66fa4eed2cb039d184daff6429e0cef047931",
            consumed.Source.GetProperty("provenance").GetProperty("template_library_id").GetString());

        Assert.Equal(4, pkg.Evidence.WhatIReadLines.Count);
        Assert.Equal("۷,۴۶۰,۰۰۰", pkg.Evidence.PayableRawText);
        Assert.Equal("e30-golden-job-29", pkg.Evidence.SpoolJobId);
    }

    [Fact]
    public void CaptureReviewItemAssembler_Blocked_Extraction_Produces_FailClosed_Draft_Review_With_Zero_Silent_Issues()
    {
        var run = new CaptureExtractionRunResult(
            CaptureExtractionDisposition.Blocked,
            "EXTRACTION_BLOCKED",
            20,
            null,
            "IMAGE_BLURRY");

        var pkg = CaptureReviewItemAssembler.Assemble(
            new CaptureProducerContext(TestStoreId, TestDeviceId, PrintedInvoiceNumber: "102"),
            inspection: null,
            extractionRun: run,
            now: new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal("DRAFT", pkg.ConsumedReview.SourceState);
        Assert.Single(pkg.ConsumedReview.LineItems);
        Assert.Equal(0L, pkg.ConsumedReview.PayableRials);
        Assert.NotEmpty(pkg.ConsumedReview.Source.GetProperty("attention_cells").EnumerateArray());
        Assert.NotEmpty(pkg.Evidence.ExtractionErrors);
    }

    [Fact]
    public async Task CaptureToReviewPipeline_And_SpoolSink_Bridge_End_To_End_With_Sidecars()
    {
        var tempDir = CreateTempDir();
        try
        {
            var spoolDir = Path.Combine(tempDir, "spool");
            var captureDir = Path.Combine(tempDir, "captures");
            Directory.CreateDirectory(spoolDir);

            var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "extract-capture-folder-29.json");
            using var goldenDoc = JsonDocument.Parse(File.ReadAllText(fixturePath, Encoding.UTF8));
            var fakeExtractor = new StubCaptureFolderExtractor(new CaptureExtractionRunResult(
                CaptureExtractionDisposition.Review,
                "EXTRACTION_REVIEW",
                10,
                goldenDoc.RootElement.Clone()));

            var acceptedPackages = new List<CaptureReviewPackage>();
            var sink = new CaptureFolderSpoolSink(
                captureDir,
                TestStoreId,
                TestDeviceId,
                fakeExtractor,
                (pkg, _) =>
                {
                    acceptedPackages.Add(pkg);
                    return Task.CompletedTask;
                });

            var escPosSpl = new byte[] { 0x1D, 0x76, 0x30, 0, 1, 0, 2, 0, 0x80, 0x01 };
            await File.WriteAllBytesAsync(Path.Combine(spoolDir, "00029.SPL"), escPosSpl);

            var tracker = new SpoolFileTracker(new PrintCaptureOptions
            {
                SpoolDirectory = spoolDir,
                CaptureDirectory = captureDir,
                StableScans = 2,
            }, sink);

            await tracker.ScanOnceAsync();
            Assert.Empty(acceptedPackages);
            await tracker.ScanOnceAsync();

            var pkg = Assert.Single(acceptedPackages);
            Assert.Equal("EXTRACTED", pkg.ConsumedReview.SourceState);
            Assert.Equal(7_460_000L, pkg.ConsumedReview.PayableRials);
            Assert.Single(fakeExtractor.InvokedFolders);
            Assert.True(File.Exists(Path.Combine(fakeExtractor.InvokedFolders[0], "canonical_invoice_v0.json")));
            Assert.True(File.Exists(Path.Combine(fakeExtractor.InvokedFolders[0], "review_evidence.json")));

            var splHash = Convert.ToHexString(SHA256.HashData(escPosSpl)).ToLowerInvariant();
            var reviewManifestPath = Path.Combine(captureDir, splHash + ".spl.review.json");
            Assert.True(File.Exists(reviewManifestPath));
            using var manifestDoc = JsonDocument.Parse(File.ReadAllText(reviewManifestPath));
            Assert.False(manifestDoc.RootElement.GetProperty("native_effects").GetBoolean());
            Assert.Equal(pkg.ReviewId, manifestDoc.RootElement.GetProperty("review_id").GetString());
        }
        finally
        {
            DeleteIfExists(tempDir);
        }
    }

    private static byte[] MakeValidPngBytes()
    {
        var data = new byte[] { 0x1D, 0x76, 0x30, 0, 1, 0, 1, 0, 0xFF };
        var img = new EscPosRasterExtractor().Extract(data);
        using var ms = new MemoryStream();
        img.WritePng(ms);
        return ms.ToArray();
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "kandoo-capture-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteIfExists(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private sealed class StubCaptureFolderExtractor(CaptureExtractionRunResult result) : ICaptureFolderExtractor
    {
        public List<string> InvokedFolders { get; } = new();

        public Task<CaptureExtractionRunResult> ExtractAsync(
            string captureFolderPath,
            DateTimeOffset? capturedAt = null,
            CancellationToken cancellationToken = default)
        {
            InvokedFolders.Add(captureFolderPath);
            return Task.FromResult(result);
        }
    }
}
