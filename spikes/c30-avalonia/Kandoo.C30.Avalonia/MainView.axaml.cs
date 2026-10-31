using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Kandoo.C30.Avalonia;

public partial class MainView : UserControl
{
    public MainView() => AvaloniaXamlLoader.Load(this);

    private async void OnProbeClicked(object? sender, RoutedEventArgs e)
    {
        ProbeResult.Text = "در حال فراخوانی Hub…";
        try
        {
            var result = await HubProbe.FetchAsync(new Uri(Endpoint.Text ?? string.Empty));
            ProbeResult.Text = $"اتصال موفق؛ {result.PathCount} مسیر در {result.ElapsedMilliseconds} میلی‌ثانیه.";
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or UriFormatException or InvalidOperationException or TaskCanceledException)
        {
            ProbeResult.Text = "اتصال یا قرارداد پاسخ معتبر نیست.";
        }
    }
}
