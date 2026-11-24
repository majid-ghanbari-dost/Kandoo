namespace Kandoo.App;

public sealed partial class StatesPage : ContentPage
{
    public StatesPage()
    {
        InitializeComponent();
    }

    private void OnRetryClicked(object? sender, EventArgs e)
    {
        // Placeholder: real implementation retries the Hub connection.
        RetryButton.Text = "در حال تلاش...";
    }

    private void OnCopyReportClicked(object? sender, EventArgs e)
    {
        // Placeholder: real implementation copies error report to clipboard.
        CopyReportButton.Text = "کپی شد";
    }
}
