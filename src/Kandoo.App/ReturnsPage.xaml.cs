namespace Kandoo.App;

public sealed partial class ReturnsPage : ContentPage
{
    private readonly HubConnectionSettings? _settings;

    public ReturnsPage(HubConnectionSettings? settings = null)
    {
        InitializeComponent();
        _settings = settings;
    }

    private void OnSubmitReturnClicked(object? sender, EventArgs e)
    {
        // Placeholder: real implementation calls Hub API to register a return event.
        // The original invoice is NOT deleted; a new "return" event is recorded
        // and inventory/customer balances are adjusted automatically.
        StatusLabel.Text = "برگشت ثبت شد (نمونه‌ای — اتصال به API فعال نشده است).";
    }
}
