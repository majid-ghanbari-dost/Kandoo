using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;

namespace KandooPrintProbe
{
    public class PrintSpoolMonitor : IDisposable
    {
        private const string SpoolFolder =
            @"C:\Windows\System32\spool\PRINTERS";

        private const int PollIntervalMs = 100;

        private readonly Dictionary<string, FileState> knownFiles =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly object syncLock = new();

        private ManagementEventWatcher? watcher;

        private Thread? pollingThread;

        private volatile bool running;

        private bool baselineReady;

        private int? currentJobId;

        private string? currentJobFolder;

        private string? currentPrinterName;

        private string? currentDocumentName;

        private string? currentOwner;

        private uint currentPages;

        public event EventHandler<PrintJobDetectedEventArgs>?
            PrintJobDetected;

        public event EventHandler<SpoolFileDetectedEventArgs>?
            SpoolFileDetected;

        public event EventHandler<string>?
            LogMessage;

        public void Start()
        {
            lock (syncLock)
            {
                if (running)
                    return;

                running = true;
                baselineReady = false;

                currentJobId = null;
                currentJobFolder = null;
                currentPrinterName = null;
                currentDocumentName = null;
                currentOwner = null;
                currentPages = 0;

                knownFiles.Clear();
            }

            Log("======================================");
            Log("آماده‌سازی پایش خام Spooler");
            Log("======================================");

            Log("پوشه Spooler:");
            Log(SpoolFolder);

            if (!Directory.Exists(SpoolFolder))
            {
                Stop();

                throw new DirectoryNotFoundException(
                    $"Spooler folder not found: {SpoolFolder}");
            }

            CreateBaseline();

            StartWmiWatcher();

            pollingThread =
                new Thread(PollingLoop)
                {
                    IsBackground = true,
                    Name = "KandooPrintProbe-SpoolPolling"
                };

            pollingThread.Start();

            Log("پایش مستقیم پوشه Spooler شروع شد.");
            Log("فایل‌های موجود قبلی نادیده گرفته شدند.");
        }

        public void Stop()
        {
            lock (syncLock)
            {
                if (!running)
                    return;

                running = false;
            }

            try
            {
                watcher?.Stop();
            }
            catch
            {
                // Ignore shutdown errors.
            }

            try
            {
                watcher?.Dispose();
            }
            catch
            {
                // Ignore shutdown errors.
            }

            watcher = null;

            Thread? thread = pollingThread;

            if (thread != null &&
                thread != Thread.CurrentThread)
            {
                try
                {
                    thread.Join(1000);
                }
                catch
                {
                    // Ignore shutdown errors.
                }
            }

            pollingThread = null;
        }

        private void CreateBaseline()
        {
            lock (syncLock)
            {
                knownFiles.Clear();

                string[] files;

                try
                {
                    files = Directory.GetFiles(
                        SpoolFolder);
                }
                catch (Exception ex)
                {
                    throw new IOException(
                        "Unable to read Windows Spooler folder.",
                        ex);
                }

                foreach (string file in files)
                {
                    try
                    {
                        FileInfo info = new(file);

                        knownFiles[file] =
                            new FileState
                            {
                                Size = info.Length,
                                LastWriteTimeUtc =
                                    info.LastWriteTimeUtc
                            };
                    }
                    catch
                    {
                        // A spool file can disappear/change
                        // while baseline is being created.
                    }
                }

                baselineReady = true;

                Log(
                    $"Baseline ثبت شد: {knownFiles.Count} فایل موجود.");
            }
        }

        private void StartWmiWatcher()
        {
            string query =
                "SELECT * FROM __InstanceCreationEvent " +
                "WITHIN 1 " +
                "WHERE TargetInstance ISA 'Win32_PrintJob'";

            watcher =
                new ManagementEventWatcher(
                    new WqlEventQuery(query));

            watcher.EventArrived +=
                WmiWatcher_EventArrived;

            watcher.Start();

            Log("پایش WMI شروع شد.");
        }

        private void WmiWatcher_EventArrived(
            object sender,
            EventArrivedEventArgs e)
        {
            try
            {
                if (!running)
                    return;

                if (!e.NewEvent.Properties
                    .Cast<PropertyData>()
                    .Any(p =>
                        p.Name.Equals(
                            "TargetInstance",
                            StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }

                ManagementBaseObject? target =
                    e.NewEvent["TargetInstance"]
                    as ManagementBaseObject;

                if (target == null)
                    return;

                int jobId =
                    ReadInt32(
                        target,
                        "JobId");

                string printerName =
                    ReadString(
                        target,
                        "Name");

                string documentName =
                    ReadString(
                        target,
                        "Document");

                string owner =
                    ReadString(
                        target,
                        "Owner");

                uint pages =
                    ReadUInt32(
                        target,
                        "TotalPages");

                lock (syncLock)
                {
                    currentJobId = jobId;
                    currentPrinterName = printerName;
                    currentDocumentName = documentName;
                    currentOwner = owner;
                    currentPages = pages;

                    currentJobFolder =
                        CreateJobFolder(jobId);
                }

                WriteJobReport(
                    jobId,
                    printerName,
                    documentName,
                    owner,
                    pages);

                RaisePrintJobDetected(
                    new PrintJobDetectedEventArgs
                    {
                        JobId = jobId,
                        PrinterName = printerName,
                        DocumentName = documentName,
                        Owner = owner,
                        Pages = pages
                    });
            }
            catch (Exception ex)
            {
                Log(
                    "خطا در پردازش WMI Print Job: " +
                    ex.Message);
            }
        }

        private void PollingLoop()
        {
            while (running)
            {
                try
                {
                    ProcessSpoolFolder();
                }
                catch (Exception ex)
                {
                    Log(
                        "خطا در پایش Spooler: " +
                        ex.Message);
                }

                Thread.Sleep(PollIntervalMs);
            }
        }

        private void ProcessSpoolFolder()
        {
            string[] files;

            try
            {
                files =
                    Directory.GetFiles(
                        SpoolFolder);
            }
            catch
            {
                return;
            }

            foreach (string file in files)
            {
                if (!running)
                    return;

                ProcessFile(file);
            }

            RemoveMissingFiles(files);
        }

        private void ProcessFile(string filePath)
        {
            FileInfo info;

            try
            {
                info = new FileInfo(filePath);

                if (!info.Exists)
                    return;
            }
            catch
            {
                return;
            }

            FileState currentState =
                new()
                {
                    Size = info.Length,
                    LastWriteTimeUtc =
                        info.LastWriteTimeUtc
                };

            bool isNew = false;
            bool sizeChanged = false;

            lock (syncLock)
            {
                if (!baselineReady)
                {
                    knownFiles[filePath] =
                        currentState;

                    return;
                }

                if (!knownFiles.TryGetValue(
                        filePath,
                        out FileState? oldState))
                {
                    isNew = true;

                    knownFiles[filePath] =
                        currentState;
                }
                else
                {
                    sizeChanged =
                        oldState.Size != currentState.Size ||
                        oldState.LastWriteTimeUtc !=
                            currentState.LastWriteTimeUtc;

                    if (!sizeChanged)
                        return;

                    knownFiles[filePath] =
                        currentState;
                }
            }

            string eventType =
                isNew
                    ? "Created"
                    : "Changed";

            ProcessDetectedFile(
                filePath,
                eventType,
                currentState.Size);
        }

        private void ProcessDetectedFile(
            string filePath,
            string eventType,
            long size)
        {
            string fileName =
                Path.GetFileName(filePath);

            string extension =
                Path.GetExtension(filePath);

            string signature =
                DetectSignature(filePath);

            string hexPreview =
                ReadHexPreview(filePath);

            RaiseSpoolFileDetected(
                new SpoolFileDetectedEventArgs
                {
                    EventType = eventType,
                    FileName = fileName,
                    Extension = extension,
                    Size = size,
                    Signature = signature,
                    FilePath = filePath,
                    HexPreview = hexPreview
                });

            string? capturedPath =
                CaptureRawFile(
                    filePath,
                    fileName);

            if (capturedPath == null)
                return;

            /*
             * New feature:
             *
             * If the captured file is a non-empty SPL,
             * try to reconstruct the ESC/POS raster image.
             *
             * Failure here must NOT break raw capture.
             */
            if (
                extension.Equals(
                    ".SPL",
                    StringComparison.OrdinalIgnoreCase) &&
                size > 0)
            {
                TryExtractEscPosPng(
                    capturedPath);
            }
        }

        private string? CaptureRawFile(
            string sourcePath,
            string fileName)
        {
            string outputFolder;

            lock (syncLock)
            {
                outputFolder =
                    currentJobFolder ??
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "DiagnosticResults",
                        "Unassigned");
            }

            try
            {
                Directory.CreateDirectory(
                    outputFolder);
            }
            catch (Exception ex)
            {
                Log(
                    "نتوانستم پوشه ذخیره‌سازی را ایجاد کنم: " +
                    ex.Message);

                return null;
            }

            string safeName =
                SanitizeFileName(fileName);

            string outputFileName =
                $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_" +
                $"{safeName}";

            string destination =
                Path.Combine(
                    outputFolder,
                    outputFileName);

            for (int attempt = 1;
                 attempt <= 10;
                 attempt++)
            {
                if (!File.Exists(sourcePath))
                    return null;

                try
                {
                    using FileStream source =
                        new(
                            sourcePath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.ReadWrite |
                            FileShare.Delete);

                    using FileStream destinationStream =
                        new(
                            destination,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.None);

                    source.CopyTo(
                        destinationStream);

                    Log(
                        $"فایل خام ذخیره شد: {safeName}");

                    return destination;
                }
                catch
                {
                    if (attempt == 10)
                        break;

                    Thread.Sleep(100);
                }
            }

            Log(
                $"نتوانستم فایل را کپی کنم: {safeName}");

            return null;
        }

        private void TryExtractEscPosPng(
            string capturedSplPath)
        {
            try
            {
                FileInfo info =
                    new(capturedSplPath);

                if (!info.Exists ||
                    info.Length == 0)
                {
                    return;
                }

                string directory =
                    Path.GetDirectoryName(
                        capturedSplPath)
                    ?? AppContext.BaseDirectory;

                string fileName =
                    Path.GetFileNameWithoutExtension(
                        capturedSplPath);

                string outputPath =
                    Path.Combine(
                        directory,
                        fileName +
                        "_ESC_POS_RASTER.png");

                var extractor =
                    new EscPosRasterExtractor();

                string pngPath =
                    extractor.ExtractToPng(
                        capturedSplPath,
                        outputPath);

                Log(
                    "تصویر ESC/POS ساخته شد: " +
                    pngPath);
            }
            catch (Exception ex)
            {
                /*
                 * Not every SPL is necessarily ESC/POS.
                 * Therefore parser failure is diagnostic only.
                 */
                Log(
                    "استخراج ESC/POS برای این SPL انجام نشد: " +
                    ex.Message);
            }
        }

        private string CreateJobFolder(
            int jobId)
        {
            string root =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "DiagnosticResults");

            Directory.CreateDirectory(root);

            string folderName =
                $"Job_{jobId}_" +
                $"{DateTime.Now:yyyyMMdd_HHmmss_fff}";

            string folder =
                Path.Combine(
                    root,
                    folderName);

            Directory.CreateDirectory(folder);

            return folder;
        }

        private void WriteJobReport(
            int jobId,
            string printerName,
            string documentName,
            string owner,
            uint pages)
        {
            string? folder;

            lock (syncLock)
            {
                folder = currentJobFolder;
            }

            if (string.IsNullOrEmpty(folder))
                return;

            try
            {
                string reportPath =
                    Path.Combine(
                        folder,
                        "Report.txt");

                var text =
                    new StringBuilder();

                text.AppendLine(
                    "Kandoo Print Probe - Diagnostic Report");

                text.AppendLine(
                    "======================================");

                text.AppendLine(
                    $"زمان شناسایی: " +
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");

                text.AppendLine(
                    $"Job ID: {jobId}");

                text.AppendLine(
                    $"Printer: {printerName}");

                text.AppendLine(
                    $"Document: {documentName}");

                text.AppendLine(
                    $"Owner: {owner}");

                text.AppendLine(
                    $"Pages: {pages}");

                text.AppendLine();

                text.AppendLine(
                    "تمام فایل‌هایی که هنگام این Job " +
                    "در Spooler مشاهده شوند، " +
                    "در همین پوشه ذخیره خواهند شد.");

                File.WriteAllText(
                    reportPath,
                    text.ToString(),
                    Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log(
                    "نتوانستم Report.txt را ایجاد کنم: " +
                    ex.Message);
            }
        }

        private string DetectSignature(
            string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return "File not found";

                using FileStream stream =
                    new(
                        filePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite |
                        FileShare.Delete);

                if (stream.Length == 0)
                    return "Unknown / خام";

                int length =
                    (int)Math.Min(
                        stream.Length,
                        8);

                byte[] buffer =
                    new byte[length];

                int read =
                    stream.Read(
                        buffer,
                        0,
                        buffer.Length);

                if (read >= 4 &&
                    buffer[0] == 0x50 &&
                    buffer[1] == 0x4B &&
                    buffer[2] == 0x03 &&
                    buffer[3] == 0x04)
                {
                    return "ZIP / OpenXPS احتمالی";
                }

                if (read >= 4 &&
                    buffer[0] == 0x25 &&
                    buffer[1] == 0x50 &&
                    buffer[2] == 0x44 &&
                    buffer[3] == 0x46)
                {
                    return "PDF";
                }

                if (read >= 2 &&
                    buffer[0] == 0xD7 &&
                    buffer[1] == 0xCD)
                {
                    return "EMF احتمالی";
                }

                if (read >= 1 &&
                    buffer[0] == 0x1B)
                {
                    return "ESC / Printer command احتمالی";
                }

                if (read >= 2 &&
                    buffer[0] == 0x25 &&
                    buffer[1] == 0x21)
                {
                    return "PostScript";
                }

                return "Unknown / خام";
            }
            catch
            {
                return "قابل خواندن نبود";
            }
        }

        private string ReadHexPreview(
            string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return "(file not found)";

                using FileStream stream =
                    new(
                        filePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite |
                        FileShare.Delete);

                if (stream.Length == 0)
                    return "(empty)";

                byte[] buffer =
                    new byte[
                        Math.Min(
                            64,
                            (int)stream.Length)];

                int read =
                    stream.Read(
                        buffer,
                        0,
                        buffer.Length);

                if (read == 0)
                    return "(empty)";

                return string.Join(
                    " ",
                    buffer
                        .Take(read)
                        .Select(
                            b =>
                                b.ToString(
                                    "X2",
                                    CultureInfo.InvariantCulture)));
            }
            catch
            {
                return "(unreadable)";
            }
        }

        private void RemoveMissingFiles(
            string[] currentFiles)
        {
            var currentSet =
                new HashSet<string>(
                    currentFiles,
                    StringComparer.OrdinalIgnoreCase);

            lock (syncLock)
            {
                string[] knownPaths =
                    knownFiles.Keys.ToArray();

                foreach (string path in knownPaths)
                {
                    if (!currentSet.Contains(path))
                    {
                        knownFiles.Remove(path);
                    }
                }
            }
        }

        private static string SanitizeFileName(
            string fileName)
        {
            foreach (char invalidChar
                     in Path.GetInvalidFileNameChars())
            {
                fileName =
                    fileName.Replace(
                        invalidChar,
                        '_');
            }

            return fileName;
        }

        private static string ReadString(
            ManagementBaseObject obj,
            string propertyName)
        {
            try
            {
                return
                    Convert.ToString(
                        obj[propertyName],
                        CultureInfo.InvariantCulture)
                    ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static int ReadInt32(
            ManagementBaseObject obj,
            string propertyName)
        {
            try
            {
                return Convert.ToInt32(
                    obj[propertyName],
                    CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }

        private static uint ReadUInt32(
            ManagementBaseObject obj,
            string propertyName)
        {
            try
            {
                return Convert.ToUInt32(
                    obj[propertyName],
                    CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }

        private void RaisePrintJobDetected(
            PrintJobDetectedEventArgs args)
        {
            try
            {
                PrintJobDetected?.Invoke(
                    this,
                    args);
            }
            catch (Exception ex)
            {
                Log(
                    "خطا در رویداد PrintJobDetected: " +
                    ex.Message);
            }
        }

        private void RaiseSpoolFileDetected(
            SpoolFileDetectedEventArgs args)
        {
            try
            {
                SpoolFileDetected?.Invoke(
                    this,
                    args);
            }
            catch (Exception ex)
            {
                Log(
                    "خطا در رویداد SpoolFileDetected: " +
                    ex.Message);
            }
        }

        private void Log(
            string message)
        {
            try
            {
                LogMessage?.Invoke(
                    this,
                    $"[{DateTime.Now:HH:mm:ss.fff}] {message}");
            }
            catch
            {
                // Never allow UI logging to break monitoring.
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private class FileState
        {
            public long Size { get; set; }

            public DateTime LastWriteTimeUtc { get; set; }
        }
    }

    public class PrintJobDetectedEventArgs : EventArgs
    {
        public int JobId { get; set; }

        public string PrinterName { get; set; } = "";

        public string DocumentName { get; set; } = "";

        public string Owner { get; set; } = "";

        public uint Pages { get; set; }
    }

    public class SpoolFileDetectedEventArgs : EventArgs
    {
        public string EventType { get; set; } = "";

        public string FileName { get; set; } = "";

        public string Extension { get; set; } = "";

        public long Size { get; set; }

        public string Signature { get; set; } = "";

        public string FilePath { get; set; } = "";

        public string HexPreview { get; set; } = "";
    }
}