using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace KandooPrintProbe
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }

    public class MainForm : Form
    {
        private readonly PrintSpoolMonitor monitor;

        private Button startButton = null!;
        private Button stopButton = null!;
        private Button clearButton = null!;
        private TextBox logBox = null!;

        public MainForm()
        {
            Text = "Kandoo Print Probe - Diagnostic";
            Width = 1100;
            Height = 750;
            StartPosition = FormStartPosition.CenterScreen;

            RightToLeft = RightToLeft.Yes;
            RightToLeftLayout = true;

            Font = new Font("Tahoma", 10);

            monitor = new PrintSpoolMonitor();

            CreateControls();
            ConnectEvents();

            FormClosed += (_, _) =>
            {
                monitor.Dispose();
            };
        }

        private void CreateControls()
        {
            startButton = new Button
            {
                Text = "شروع پایش",
                Width = 140,
                Height = 42,
                Top = 20,
                RightToLeft = RightToLeft.Yes,
                Font = new Font("Tahoma", 10)
            };

            stopButton = new Button
            {
                Text = "توقف",
                Width = 140,
                Height = 42,
                Top = 20,
                RightToLeft = RightToLeft.Yes,
                Font = new Font("Tahoma", 10)
            };

            clearButton = new Button
            {
                Text = "پاک کردن گزارش",
                Width = 150,
                Height = 42,
                Top = 20,
                RightToLeft = RightToLeft.Yes,
                Font = new Font("Tahoma", 10)
            };

            logBox = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                ReadOnly = true,
                Font = new Font("Consolas", 10),
                RightToLeft = RightToLeft.No,
                WordWrap = false
            };

            Controls.Add(startButton);
            Controls.Add(stopButton);
            Controls.Add(clearButton);
            Controls.Add(logBox);

            ResizeControls();

            Resize += (_, _) =>
            {
                ResizeControls();
            };
        }

        private void ConnectEvents()
        {
            startButton.Click += StartButton_Click;

            stopButton.Click += StopButton_Click;

            clearButton.Click += (_, _) =>
            {
                logBox.Clear();
            };

            monitor.PrintJobDetected +=
                OnPrintJobDetected;

            monitor.SpoolFileDetected +=
                OnSpoolFileDetected;

            monitor.LogMessage +=
                OnMonitorLog;
        }

        private void StartButton_Click(
            object? sender,
            EventArgs e)
        {
            try
            {
                monitor.Start();

                Log("");
                Log("======================================");
                Log("پایش شروع شد.");
                Log("======================================");
                Log("");
                Log("اکنون یک فایل آزمایشی را چاپ کنید.");
                Log("");
                Log("اگر روی سیستم هلو هستید:");
                Log("یک فاکتور واقعی هلو چاپ کنید.");
                Log("");
                Log("پنجره را نبندید تا چاپ کامل شود.");
                Log("======================================");
            }
            catch (Exception ex)
            {
                Log("");
                Log("======================================");
                Log("خطا در شروع پایش");
                Log("======================================");
                Log(ex.ToString());
            }
        }

        private void StopButton_Click(
            object? sender,
            EventArgs e)
        {
            try
            {
                monitor.Stop();

                Log("");
                Log("======================================");
                Log("پایش متوقف شد.");
                Log("======================================");
                Log("");
                Log("فایل‌های خام در پوشه زیر ذخیره شده‌اند:");
                Log(Path.Combine(
                    AppContext.BaseDirectory,
                    "DiagnosticResults"));
            }
            catch (Exception ex)
            {
                Log("");
                Log("خطا در توقف پایش:");
                Log(ex.ToString());
            }
        }

        private void OnPrintJobDetected(
            object? sender,
            PrintJobDetectedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() =>
                    OnPrintJobDetected(sender, e));

                return;
            }

            Log("");
            Log("======================================");
            Log(">>> PRINT JOB شناسایی شد <<<");
            Log($"Job ID      : {e.JobId}");
            Log($"Printer     : {e.PrinterName}");
            Log($"Document    : {e.DocumentName}");
            Log($"Owner       : {e.Owner}");
            Log($"Pages       : {e.Pages}");
            Log("======================================");
        }

        private void OnSpoolFileDetected(
            object? sender,
            SpoolFileDetectedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() =>
                    OnSpoolFileDetected(sender, e));

                return;
            }

            Log("");
            Log(">>> فایل Spool شناسایی شد <<<");
            Log($"Event       : {e.EventType}");
            Log($"File        : {e.FileName}");
            Log($"Extension   : {e.Extension}");
            Log($"Size        : {e.Size:N0} bytes");
            Log($"Signature   : {e.Signature}");
            Log($"Path        : {e.FilePath}");

            Log("Hex Preview:");
            Log(e.HexPreview);

            Log("--------------------------------------");
        }

        private void OnMonitorLog(
            object? sender,
            string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() =>
                    OnMonitorLog(sender, message));

                return;
            }

            Log(message);
        }

        private void Log(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() =>
                    Log(text));

                return;
            }

            logBox.AppendText(
                $"[{DateTime.Now:HH:mm:ss.fff}] {text}" +
                Environment.NewLine);

            logBox.SelectionStart =
                logBox.TextLength;

            logBox.ScrollToCaret();
        }

        private void ResizeControls()
        {
            int margin = 20;
            int gap = 10;

            startButton.Left =
                ClientSize.Width -
                margin -
                startButton.Width;

            stopButton.Left =
                startButton.Left -
                gap -
                stopButton.Width;

            clearButton.Left =
                stopButton.Left -
                gap -
                clearButton.Width;

            logBox.Left = margin;
            logBox.Top = 80;

            logBox.Width =
                ClientSize.Width -
                (margin * 2);

            logBox.Height =
                ClientSize.Height -
                100;
        }
    }
}