using System.Text.Json;

namespace Kandoo.C30.Maui;

public partial class MainPage : ContentPage
{
    public MainPage() => InitializeComponent();

    private async void OnProbeClicked(object sender, EventArgs e)
    {
        ProbeResult.Text = "در حال فراخوانی Hub…";
        try
        {
            var result = await HubProbe.FetchAsync(new Uri(Endpoint.Text));
            ProbeResult.Text = $"اتصال موفق؛ {result.PathCount} مسیر در {result.ElapsedMilliseconds} میلی‌ثانیه.";
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or UriFormatException or InvalidOperationException or TaskCanceledException)
        {
            ProbeResult.Text = "اتصال یا قرارداد پاسخ معتبر نیست.";
        }
    }
}
