namespace Kandoo.App;

public partial class CustomersPage : ContentPage
{
    private readonly HttpClient _httpClient = new();
    private readonly HubConnectionSettings? _settings;
    private string? _lastError;

    public CustomersPage(HubConnectionSettings? settings = null)
    {
        _settings = settings;
        InitializeComponent();
        LoadButton.IsEnabled = _settings is not null;
        StatusLabel.Text = _settings is null
            ? "تنظیمات هاب لازم است."
            : "آماده بارگذاری مشتریان.";
    }

    private async void OnLoadClicked(object? sender, EventArgs e)
    {
        if (_settings is null)
        {
            StatusLabel.Text = "تنظیمات هاب فراهم نشده است.";
            return;
        }

        LoadButton.IsEnabled = false;
        _lastError = null;
        try
        {
            var dataSource = new HubCustomersDataSource(_httpClient, _settings);
            var customers = await dataSource.LoadCustomersAsync();

            CustomersList.Clear();
            EmptyState.IsVisible = customers.Count == 0;

            foreach (var customer in customers)
            {
                var bodyStyle = Resources["BodyStyle"] as Style;
                var border = new Border
                {
                    Style = Resources["CardStyle"] as Style ?? bodyStyle
                };
                var layout = new VerticalStackLayout { Style = Resources["RowStackStyle"] as Style ?? bodyStyle };
                layout.Add(new Label
                {
                    Text = customer.Name,
                    Style = Resources["SectionTitleStyle"] as Style ?? bodyStyle
                });
                layout.Add(new Label
                {
                    Text = $"{customer.Phone} — {customer.TotalDisplay}",
                    Style = bodyStyle
                });
                border.Content = layout;
                CustomersList.Add(border);
            }

            StatusLabel.Text = FormattableString.Invariant($"{customers.Count} مشتری بارگذاری شد.");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _lastError = exception.Message;
            StatusLabel.Text = exception.Message;
        }
        finally
        {
            LoadButton.IsEnabled = true;
        }
    }

    private static bool IsExpected(Exception exception) => exception is ArgumentException or
        FormatException or HttpRequestException or JsonException or InvalidOperationException or
        TaskCanceledException;
}
