using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Kandoo.Capture;

public sealed record CaptureIntakeProblem(
    string Code,
    string Message,
    string? Path = null,
    string? Detail = null);

public sealed record CapturePngPageArtifact(
    int PageIndex,
    string FileName,
    string FullPath,
    int Width,
    int Height,
    long Bytes,
    string Sha256);

public sealed record CaptureSplArtifactInfo(
    string FileName,
    string FullPath,
    long Bytes,
    string Sha256);

public sealed record CaptureReportArtifactInfo(
    string FileName,
    string FullPath,
    long Bytes,
    int Lines,
    string Sha256,
    string? SpoolJobId,
    string? CapturedAtIso,
    int? ReportedPageCount,
    string? ReportedSplSha256,
    string? PrinterName,
    string? DocumentName);

public sealed record CaptureIntakeInspection(
    bool IsValid,
    string CaptureFolderPath,
    string FolderName,
    CaptureSplArtifactInfo? Spl,
    CaptureReportArtifactInfo? Report,
    IReadOnlyList<CapturePngPageArtifact> Pages,
    IReadOnlyList<string> ExtraFiles,
    IReadOnlyList<CaptureIntakeProblem> Problems);

public sealed record CaptureBatchTriage(
    bool AllValid,
    string BatchRootPath,
    IReadOnlyList<CaptureIntakeInspection> ReadyJobs,
    IReadOnlyList<CaptureIntakeInspection> RejectedJobs,
    IReadOnlyList<CaptureIntakeProblem> BatchProblems);

public sealed class NaturalNameComparer : IComparer<string>
{
    public static NaturalNameComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
            return 0;
        if (x is null)
            return -1;
        if (y is null)
            return 1;

        var leftParts = Tokenize(x.ToLowerInvariant());
        var rightParts = Tokenize(y.ToLowerInvariant());
        var count = Math.Min(leftParts.Count, rightParts.Count);
        for (var i = 0; i < count; i++)
        {
            var left = leftParts[i];
            var right = rightParts[i];
            if (left.IsNumber && right.IsNumber)
            {
                var numCmp = left.Number.CompareTo(right.Number);
                if (numCmp != 0)
                    return numCmp;
                var lenCmp = left.Text.Length.CompareTo(right.Text.Length);
                if (lenCmp != 0)
                    return lenCmp;
            }
            else
            {
                var strCmp = string.CompareOrdinal(left.Text, right.Text);
                if (strCmp != 0)
                    return strCmp;
            }
        }

        return leftParts.Count.CompareTo(rightParts.Count);
    }

    private static List<Part> Tokenize(string value)
    {
        var list = new List<Part>();
        var idx = 0;
        while (idx < value.Length)
        {
            var isDigit = char.IsAsciiDigit(value[idx]);
            var start = idx;
            while (idx < value.Length && char.IsAsciiDigit(value[idx]) == isDigit)
                idx++;

            var slice = value[start..idx];
            if (isDigit && ulong.TryParse(slice, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                list.Add(new Part(true, parsed, slice));
            else
                list.Add(new Part(false, 0, slice));
        }

        return list;
    }

    private readonly record struct Part(bool IsNumber, ulong Number, string Text);
}

/// <summary>
/// Validates capture folders and capture batches against the Kandoo-Digital-Invoice
/// E-30 / E-73 intake contract before extraction or Mode-B review admission.
/// </summary>
public static partial class CaptureIntakeValidator
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly Regex ProbeFieldRegex = BuildProbeFieldRegex();
    private static readonly Regex Hex64Regex = BuildHex64Regex();
    private static readonly Regex IsoOffsetSuffixRegex = BuildIsoOffsetSuffixRegex();

    public static CaptureIntakeInspection InspectFolder(string captureFolderPath)
    {
        var problems = new List<CaptureIntakeProblem>();
        var fullPath = string.IsNullOrWhiteSpace(captureFolderPath)
            ? string.Empty
            : Path.GetFullPath(captureFolderPath);
        var folderName = string.IsNullOrWhiteSpace(fullPath) ? string.Empty : Path.GetFileName(fullPath);

        if (string.IsNullOrWhiteSpace(captureFolderPath) || (!Directory.Exists(fullPath) && !File.Exists(fullPath)))
        {
            problems.Add(new CaptureIntakeProblem("CAPTURE_FOLDER_NOT_FOUND", "Capture folder does not exist.", captureFolderPath));
            return new CaptureIntakeInspection(false, fullPath, folderName, null, null, Array.Empty<CapturePngPageArtifact>(), Array.Empty<string>(), problems);
        }

        if (File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            problems.Add(new CaptureIntakeProblem("CAPTURE_FOLDER_NOT_DIRECTORY", "Capture folder path is not a directory.", fullPath));
            return new CaptureIntakeInspection(false, fullPath, folderName, null, null, Array.Empty<CapturePngPageArtifact>(), Array.Empty<string>(), problems);
        }

        var dirInfo = new DirectoryInfo(fullPath);
        if (IsSymlinkOrReparsePoint(dirInfo))
        {
            problems.Add(new CaptureIntakeProblem("JOB_IN_WRONG_FOLDER", "Capture folder cannot be a symbolic link or junction.", fullPath));
            return new CaptureIntakeInspection(false, fullPath, folderName, null, null, Array.Empty<CapturePngPageArtifact>(), Array.Empty<string>(), problems);
        }

        if (string.Equals(folderName, "Unassigned", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add(new CaptureIntakeProblem(
                "JOB_IN_WRONG_FOLDER",
                "Artifacts inside 'Unassigned' cannot be queued directly; move them into a dedicated job folder.",
                fullPath));
        }

        FileSystemInfo[] entries;
        try
        {
            entries = dirInfo.GetFileSystemInfos();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            problems.Add(new CaptureIntakeProblem("CAPTURE_FOLDER_UNREADABLE", $"Capture folder is unreadable: {ex.Message}", fullPath));
            return new CaptureIntakeInspection(false, fullPath, folderName, null, null, Array.Empty<CapturePngPageArtifact>(), Array.Empty<string>(), problems);
        }

        var splFiles = new List<FileInfo>();
        var pngFiles = new List<FileInfo>();
        var reportFiles = new List<FileInfo>();
        var extraFiles = new List<string>();

        foreach (var entry in entries.OrderBy(static e => e.Name, NaturalNameComparer.Instance))
        {
            if (entry is DirectoryInfo subDir)
            {
                if (ContainsNestedCaptureArtifacts(subDir))
                {
                    problems.Add(new CaptureIntakeProblem(
                        "JOB_IN_WRONG_FOLDER",
                        $"Nested capture artifacts found inside subdirectory '{subDir.Name}'.",
                        subDir.FullName));
                }

                continue;
            }

            if (entry is not FileInfo file)
                continue;

            var ext = file.Extension;
            if (ext.Equals(".spl", StringComparison.OrdinalIgnoreCase))
            {
                splFiles.Add(file);
            }
            else if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                pngFiles.Add(file);
            }
            else if (file.Name.Equals("Report.txt", StringComparison.OrdinalIgnoreCase))
            {
                reportFiles.Add(file);
            }
            else
            {
                extraFiles.Add(file.Name);
            }
        }

        CaptureSplArtifactInfo? splArtifact = null;
        if (splFiles.Count == 0)
        {
            problems.Add(new CaptureIntakeProblem("MISSING_SPL", "Missing required .spl file.", fullPath));
        }
        else if (splFiles.Count > 1)
        {
            problems.Add(new CaptureIntakeProblem("AMBIGUOUS_SPL", "Expected exactly one .spl file in capture folder.", fullPath));
        }
        else
        {
            splArtifact = InspectSpl(splFiles[0], problems);
        }

        var pages = new List<CapturePngPageArtifact>();
        if (pngFiles.Count == 0)
        {
            problems.Add(new CaptureIntakeProblem("MISSING_PNG", "Missing required page PNG image(s).", fullPath));
        }
        else
        {
            var pageIndex = 1;
            foreach (var png in pngFiles)
            {
                var page = InspectPng(png, pageIndex, problems);
                if (page is not null)
                {
                    pages.Add(page);
                    pageIndex++;
                }
            }
        }

        CaptureReportArtifactInfo? reportArtifact = null;
        if (reportFiles.Count == 0)
        {
            problems.Add(new CaptureIntakeProblem("MISSING_REPORT", "Missing required Report.txt file.", fullPath));
        }
        else if (reportFiles.Count > 1)
        {
            problems.Add(new CaptureIntakeProblem("AMBIGUOUS_REPORT", "Expected exactly one Report.txt file.", fullPath));
        }
        else
        {
            reportArtifact = InspectReport(reportFiles[0], splArtifact, pages, problems);
        }

        return new CaptureIntakeInspection(
            problems.Count == 0,
            fullPath,
            folderName,
            splArtifact,
            reportArtifact,
            pages,
            extraFiles,
            problems);
    }

    public static CaptureBatchTriage InspectBatch(string batchRootDirectory)
    {
        var batchProblems = new List<CaptureIntakeProblem>();
        if (string.IsNullOrWhiteSpace(batchRootDirectory) || !Directory.Exists(batchRootDirectory))
        {
            var problem = new CaptureIntakeProblem("CAPTURE_FOLDER_NOT_FOUND", "Capture batch folder does not exist.", batchRootDirectory);
            batchProblems.Add(problem);
            return new CaptureBatchTriage(false, batchRootDirectory ?? string.Empty, Array.Empty<CaptureIntakeInspection>(), Array.Empty<CaptureIntakeInspection>(), batchProblems);
        }

        var fullRoot = Path.GetFullPath(batchRootDirectory);
        var rootInfo = new DirectoryInfo(fullRoot);

        // Artifacts placed directly in the batch root indicate a misplaced job
        foreach (var file in rootInfo.GetFiles().OrderBy(static f => f.Name, NaturalNameComparer.Instance))
        {
            if (file.Extension.Equals(".spl", StringComparison.OrdinalIgnoreCase) ||
                file.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                file.Name.Equals("Report.txt", StringComparison.OrdinalIgnoreCase))
            {
                batchProblems.Add(new CaptureIntakeProblem(
                    "JOB_IN_WRONG_FOLDER",
                    $"Artifact '{file.Name}' sits at batch root instead of inside a job folder.",
                    file.FullName));
            }
        }

        var jobDirs = rootInfo.GetDirectories()
            .OrderBy(static d => d.Name, NaturalNameComparer.Instance)
            .ToList();

        if (jobDirs.Count == 0)
        {
            batchProblems.Add(new CaptureIntakeProblem("EMPTY_CAPTURE_BATCH", "Capture batch contains no job folders.", fullRoot));
            return new CaptureBatchTriage(false, fullRoot, Array.Empty<CaptureIntakeInspection>(), Array.Empty<CaptureIntakeInspection>(), batchProblems);
        }

        var inspections = jobDirs.Select(d => InspectFolder(d.FullName)).ToList();

        // Batch-level checks: missing job ID and duplicate job ID across folders
        var foldersByJobId = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < inspections.Count; i++)
        {
            var insp = inspections[i];
            if (insp.Report is not null)
            {
                if (string.IsNullOrWhiteSpace(insp.Report.SpoolJobId))
                {
                    var enrichedProblems = insp.Problems.ToList();
                    enrichedProblems.Add(new CaptureIntakeProblem(
                        "MISSING_JOB_ID",
                        "Report.txt is missing 'Spool job ID:' metadata.",
                        insp.Report.FullPath));
                    inspections[i] = insp with { IsValid = false, Problems = enrichedProblems };
                }
                else
                {
                    if (!foldersByJobId.TryGetValue(insp.Report.SpoolJobId, out var list))
                    {
                        list = new List<int>();
                        foldersByJobId[insp.Report.SpoolJobId] = list;
                    }

                    list.Add(i);
                }
            }
        }

        foreach (var (jobId, indices) in foldersByJobId)
        {
            if (indices.Count < 2)
                continue;

            var collidingNames = string.Join(", ", indices.Select(idx => inspections[idx].FolderName));
            batchProblems.Add(new CaptureIntakeProblem(
                "DUPLICATE_JOB_ID",
                $"Spool job ID '{jobId}' appears in multiple capture folders ({collidingNames}); all colliding folders are held.",
                fullRoot,
                jobId));

            foreach (var idx in indices)
            {
                var insp = inspections[idx];
                var updated = insp.Problems.ToList();
                updated.Add(new CaptureIntakeProblem(
                    "DUPLICATE_JOB_ID",
                    $"Spool job ID '{jobId}' collides across multiple folders ({collidingNames}).",
                    insp.CaptureFolderPath,
                    jobId));
                inspections[idx] = insp with { IsValid = false, Problems = updated };
            }
        }

        var ready = inspections.Where(static i => i.IsValid).ToList();
        var rejected = inspections.Where(static i => !i.IsValid).ToList();
        var allValid = batchProblems.Count == 0 && rejected.Count == 0 && ready.Count > 0;

        return new CaptureBatchTriage(allValid, fullRoot, ready, rejected, batchProblems);
    }

    public static IReadOnlyList<string> FormatPlainTriageReasons(IEnumerable<CaptureIntakeProblem> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);
        return problems.Select(static p => p.Code switch
        {
            "MISSING_SPL" => "فایل چاپ (.spl) در پوشه پیدا نشد.",
            "AMBIGUOUS_SPL" => "بیش از یک فایل .spl در پوشه قرار دارد.",
            "EMPTY_SPL" => "فایل چاپ (.spl) خالی است.",
            "INVALID_SPL" => "فایل چاپ (.spl) نامعتبر یا ناخوانا است.",
            "MISSING_PNG" => "تصویر صفحه (.png) در پوشه پیدا نشد.",
            "INVALID_PNG" => "فایل تصویر PNG خراب یا ناخوانا است.",
            "MISSING_REPORT" => "فایل گزارش چاپ (Report.txt) پیدا نشد.",
            "AMBIGUOUS_REPORT" => "بیش از یک فایل Report.txt وجود دارد.",
            "EMPTY_REPORT" => "فایل Report.txt خالی است.",
            "INVALID_REPORT" => "ساختار فایل Report.txt نامعتبر است.",
            "SPL_HASH_MISMATCH" => "اثر انگشت فایل .spl با مقدار ثبت‌شده در Report.txt مطابقت ندارد.",
            "PAGE_COUNT_MISMATCH" => "تعداد فایل‌های PNG با تعداد صفحات گزارش در Report.txt هم‌خوانی ندارد.",
            "DUPLICATE_JOB_ID" => "شناسه کار چاپ (Spool job ID) تکراری است و برای بررسی متوقف شده است.",
            "MISSING_JOB_ID" => "شناسه کار چاپ (Spool job ID) در Report.txt ثبت نشده است.",
            "JOB_IN_WRONG_FOLDER" => "پوشه یا فایل در مسیر نادرست قرار گرفته است.",
            "EMPTY_CAPTURE_BATCH" => "هیچ پوشه چاپی در بسته ورودی وجود ندارد.",
            _ => p.Message,
        }).Distinct(StringComparer.Ordinal).ToList();
    }

    private static CaptureSplArtifactInfo? InspectSpl(FileInfo splFile, List<CaptureIntakeProblem> problems)
    {
        if (IsSymlinkOrReparsePoint(splFile))
        {
            problems.Add(new CaptureIntakeProblem("INVALID_SPL", "SPL file cannot be a symbolic link.", splFile.FullName));
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(splFile.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add(new CaptureIntakeProblem("INVALID_SPL", $"SPL file is unreadable: {ex.Message}", splFile.FullName));
            return null;
        }

        if (bytes.Length == 0)
        {
            problems.Add(new CaptureIntakeProblem("EMPTY_SPL", "SPL file is empty (0 bytes).", splFile.FullName));
            return null;
        }

        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new CaptureSplArtifactInfo(splFile.Name, splFile.FullName, bytes.LongLength, sha256);
    }

    private static CapturePngPageArtifact? InspectPng(FileInfo pngFile, int pageIndex, List<CaptureIntakeProblem> problems)
    {
        if (IsSymlinkOrReparsePoint(pngFile))
        {
            problems.Add(new CaptureIntakeProblem("INVALID_PNG", $"PNG file '{pngFile.Name}' cannot be a symbolic link.", pngFile.FullName));
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(pngFile.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add(new CaptureIntakeProblem("INVALID_PNG", $"PNG file '{pngFile.Name}' is unreadable: {ex.Message}", pngFile.FullName));
            return null;
        }

        if (bytes.Length < 24 ||
            !bytes.AsSpan(0, 8).SequenceEqual(PngSignature) ||
            BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(8, 4)) != 13 ||
            bytes[12] != (byte)'I' || bytes[13] != (byte)'H' || bytes[14] != (byte)'D' || bytes[15] != (byte)'R')
        {
            problems.Add(new CaptureIntakeProblem("INVALID_PNG", $"PNG file '{pngFile.Name}' has an invalid PNG header.", pngFile.FullName));
            return null;
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
        if (width <= 0 || height <= 0)
        {
            problems.Add(new CaptureIntakeProblem("INVALID_PNG", $"PNG file '{pngFile.Name}' has invalid dimensions.", pngFile.FullName));
            return null;
        }

        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new CapturePngPageArtifact(pageIndex, pngFile.Name, pngFile.FullName, width, height, bytes.LongLength, sha256);
    }

    private static CaptureReportArtifactInfo? InspectReport(
        FileInfo reportFile,
        CaptureSplArtifactInfo? spl,
        IReadOnlyList<CapturePngPageArtifact> pages,
        List<CaptureIntakeProblem> problems)
    {
        if (IsSymlinkOrReparsePoint(reportFile))
        {
            problems.Add(new CaptureIntakeProblem("INVALID_REPORT", "Report.txt cannot be a symbolic link.", reportFile.FullName));
            return null;
        }

        byte[] rawBytes;
        string text;
        try
        {
            rawBytes = File.ReadAllBytes(reportFile.FullName);
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(rawBytes);
        }
        catch (DecoderFallbackException)
        {
            problems.Add(new CaptureIntakeProblem("INVALID_REPORT", "Report.txt is not valid UTF-8.", reportFile.FullName));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add(new CaptureIntakeProblem("INVALID_REPORT", $"Report.txt is unreadable: {ex.Message}", reportFile.FullName));
            return null;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            problems.Add(new CaptureIntakeProblem("EMPTY_REPORT", "Report.txt is empty.", reportFile.FullName));
            return null;
        }

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in lines)
        {
            var match = ProbeFieldRegex.Match(rawLine);
            if (!match.Success)
                continue;

            var label = match.Groups["label"].Value.Trim();
            var value = match.Groups["value"].Value.Trim();
            if (fields.ContainsKey(label))
            {
                var dupCode = label.Equals("Spool job ID", StringComparison.OrdinalIgnoreCase)
                    ? "DUPLICATE_JOB_ID"
                    : "DUPLICATE_REPORT_FIELD";
                problems.Add(new CaptureIntakeProblem(dupCode, $"Duplicate '{label}:' field in Report.txt.", reportFile.FullName, label));
                continue;
            }

            fields[label] = value;
        }

        fields.TryGetValue("Spool job ID", out var spoolJobId);
        if (fields.ContainsKey("Spool job ID") && string.IsNullOrWhiteSpace(spoolJobId))
        {
            problems.Add(new CaptureIntakeProblem("INVALID_JOB_ID", "Spool job ID in Report.txt cannot be empty.", reportFile.FullName));
            spoolJobId = null;
        }

        string? capturedAtIso = null;
        if (fields.TryGetValue("Timestamp", out var rawTimestamp))
        {
            if (string.IsNullOrWhiteSpace(rawTimestamp) ||
                !IsoOffsetSuffixRegex.IsMatch(rawTimestamp) ||
                !DateTimeOffset.TryParse(rawTimestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDto))
            {
                problems.Add(new CaptureIntakeProblem("INVALID_CAPTURED_AT", "Timestamp in Report.txt must be ISO-8601 with an explicit UTC offset.", reportFile.FullName, rawTimestamp));
            }
            else
            {
                capturedAtIso = parsedDto.ToString("O", CultureInfo.InvariantCulture);
            }
        }

        int? reportedPageCount = null;
        if (fields.TryGetValue("Page count", out var rawPageCount))
        {
            if (!int.TryParse(rawPageCount, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedCount))
            {
                problems.Add(new CaptureIntakeProblem("INVALID_PAGE_COUNT", "Page count in Report.txt must be a positive integer.", reportFile.FullName, rawPageCount));
            }
            else if (parsedCount == 0)
            {
                problems.Add(new CaptureIntakeProblem("EMPTY_JOB", "Report.txt records Page count: 0.", reportFile.FullName));
                reportedPageCount = 0;
            }
            else
            {
                reportedPageCount = parsedCount;
                if (pages.Count > 0 && parsedCount != pages.Count)
                {
                    problems.Add(new CaptureIntakeProblem(
                        "PAGE_COUNT_MISMATCH",
                        $"Report.txt Page count ({parsedCount}) does not match PNG page count ({pages.Count}).",
                        reportFile.FullName));
                }
            }
        }

        string? reportedSplSha = null;
        if (fields.TryGetValue("SPL SHA-256", out var rawSha))
        {
            if (!Hex64Regex.IsMatch(rawSha))
            {
                problems.Add(new CaptureIntakeProblem("INVALID_SPL_SHA256", "SPL SHA-256 in Report.txt must be 64 hexadecimal characters.", reportFile.FullName, rawSha));
            }
            else
            {
                reportedSplSha = rawSha.ToLowerInvariant();
                if (spl is not null && !string.Equals(reportedSplSha, spl.Sha256, StringComparison.Ordinal))
                {
                    problems.Add(new CaptureIntakeProblem(
                        "SPL_HASH_MISMATCH",
                        $"Report.txt SPL SHA-256 ({reportedSplSha}) does not match actual .spl hash ({spl.Sha256}).",
                        reportFile.FullName));
                }
            }
        }

        fields.TryGetValue("Printer", out var printerName);
        fields.TryGetValue("Document", out var documentName);
        var reportSha = Convert.ToHexString(SHA256.HashData(rawBytes)).ToLowerInvariant();
        var nonEmptyLineCount = lines.Count(static l => !string.IsNullOrWhiteSpace(l));

        return new CaptureReportArtifactInfo(
            reportFile.Name,
            reportFile.FullName,
            rawBytes.LongLength,
            nonEmptyLineCount,
            reportSha,
            spoolJobId,
            capturedAtIso,
            reportedPageCount,
            reportedSplSha,
            string.IsNullOrWhiteSpace(printerName) ? null : printerName,
            string.IsNullOrWhiteSpace(documentName) ? null : documentName);
    }

    private static bool ContainsNestedCaptureArtifacts(DirectoryInfo dir)
    {
        if (IsSymlinkOrReparsePoint(dir))
            return false;

        try
        {
            foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                if (file.Extension.Equals(".spl", StringComparison.OrdinalIgnoreCase) ||
                    file.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                    file.Name.Equals("Report.txt", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }

        return false;
    }

    private static bool IsSymlinkOrReparsePoint(FileSystemInfo info) =>
        (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null;

    [GeneratedRegex(@"^(?<label>[^:\r\n]+):(?<value>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex BuildProbeFieldRegex();

    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex BuildHex64Regex();

    [GeneratedRegex("(Z|[+-][0-9]{2}:?[0-9]{2}|[+-][0-9]{2})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BuildIsoOffsetSuffixRegex();
}
