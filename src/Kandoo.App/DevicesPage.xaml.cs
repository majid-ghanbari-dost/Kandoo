namespace Kandoo.App;

public sealed partial class DevicesPage : ContentPage
{
    public DevicesPage()
    {
        InitializeComponent();
        RolePicker.SelectedIndex = 0;
    }

    private void OnGeneratePairingCodeClicked(object? sender, EventArgs e)
    {
        var role = RolePicker.SelectedItem as string ?? "فروشنده: فقط فاکتور";
        PairingCodeLabel.Text = $"کد: {GeneratePairingCode(role)}";
    }

    private static string GeneratePairingCode(string role)
    {
        // Deterministic demo pairing code based on role.
        // Real implementation calls Hub API to create a short-lived pairing code.
        return role switch
        {
            "فروشنده: فقط فاکتور" => "SELL-1234",
            "اپراتور: فاکتور و تعریف کالا" => "OPER-5678",
            "مدیر: همه‌چیز" => "MGMT-9012",
            _ => "SELL-1234"
        };
    }
}
