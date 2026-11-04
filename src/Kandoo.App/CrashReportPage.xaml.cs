namespace Kandoo.App;

public partial class CrashReportPage : ContentPage
{
    private string _report = string.Empty;

    public CrashReportPage()
    {
        InitializeComponent();
        LoadReport();
    }

    private void LoadReport()
    {
        try
        {
            _report = AppCrashReports.Store.LoadLastReport();
        }
        catch (Exception exception)
        {
            _report = string.Empty;
            CopyStatusLabel.Text = $"خواندن گزارش ممکن نشد: {exception.Message}";
        }

        ReportEditor.Text = string.IsNullOrWhiteSpace(_report)
            ? "گزارشی ثبت نشده است."
            : _report;
        CopyButton.IsEnabled = !string.IsNullOrWhiteSpace(_report);
    }

    private async void OnCopyClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_report))
        {
            return;
        }

        try
        {
            await Clipboard.Default.SetTextAsync(_report);
            CopyStatusLabel.Text = "گزارش کپی شد.";
        }
        catch (Exception exception)
        {
            CopyStatusLabel.Text = $"کپی ممکن نشد: {exception.Message}";
        }
    }
}
