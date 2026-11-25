using System.Text.Json;

namespace Kandoo.App;

public partial class CreateInvoicePage : ContentPage
{
    private readonly HttpClient _httpClient = new();
    private readonly HubConnectionSettings? _settings;
    private readonly List<InvoiceItemRow> _items = new();
    private string? _lastError;

    public CreateInvoicePage(HubConnectionSettings? settings = null)
    {
        _settings = settings;
        InitializeComponent();
        CreateButton.IsEnabled = _settings is not null;
        StatusLabel.Text = _settings is null
            ? "تنظیمات هاب لازم است."
            : "آماده ساخت فاکتور.";
    }

    private async void OnAddItemClicked(object? sender, EventArgs e)
    {
        if (_settings is null)
        {
            StatusLabel.Text = "تنظیمات هاب فراهم نشده است.";
            return;
        }

        if (string.IsNullOrWhiteSpace(ProductIdEntry.Text) ||
            string.IsNullOrWhiteSpace(QuantityEntry.Text) ||
            string.IsNullOrWhiteSpace(UnitPriceEntry.Text))
        {
            StatusLabel.Text = "تمام فیلدهای کالا را پر کنید.";
            return;
        }

        if (!int.TryParse(QuantityEntry.Text, out var quantity) || quantity <= 0)
        {
            StatusLabel.Text = "تعداد باید عدد مثبت باشد.";
            return;
        }

        if (!long.TryParse(UnitPriceEntry.Text, out var unitPrice) || unitPrice < 0)
        {
            StatusLabel.Text = "قیمت واحد معتبر نیست.";
            return;
        }

        var item = new InvoiceItemRow(
            ProductIdEntry.Text,
            quantity,
            unitPrice,
            (long)quantity * unitPrice);
        _items.Add(item);

        var label = new Label
        {
            Text = $"{ProductIdEntry.Text} × {quantity} = {item.TotalDisplay}",
            Style = Resources["BodyStyle"] as Style
        };
        ItemsList.Add(label);

        ProductIdEntry.Text = string.Empty;
        QuantityEntry.Text = string.Empty;
        UnitPriceEntry.Text = string.Empty;
        StatusLabel.Text = $"{_items.Count} کالا به فاکتور اضافه شد.";
    }

    private async void OnCreateClicked(object? sender, EventArgs e)
    {
        if (_settings is null)
        {
            StatusLabel.Text = "تنظیمات هاب لازم است.";
            return;
        }

        if (_items.Count == 0)
        {
            StatusLabel.Text = "حداقل یک کالا به فاکتور اضافه کنید.";
            return;
        }

        _lastError = null;
        try
        {
            var dataSource = new HubInvoiceDataSource(_httpClient, _settings);

            var operationId = string.IsNullOrWhiteSpace(OperationIdEntry.Text)
                ? $"invoice-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}"
                : OperationIdEntry.Text;

            var invoiceId = await dataSource.CreateDraftAsync(
                operationId,
                "SALE",
                string.IsNullOrWhiteSpace(CustomerPhoneEntry.Text) ? null : CustomerPhoneEntry.Text);

            foreach (var item in _items)
            {
                await dataSource.AddItemAsync(
                    invoiceId,
                    description: item.Description,
                    productId: item.ProductId,
                    quantity: item.Quantity,
                    unitPriceRials: item.UnitPriceRials);
            }

            await dataSource.IssueAsync(invoiceId);

            if (PrintCheckbox.IsChecked is true)
            {
                // Print is optional; mark intent but don't block on it.
                StatusLabel.Text = "فاکتور ساخته شد؛ چاپ در صف چاپ قرار می‌گیرد.";
            }
            else
            {
                StatusLabel.Text = "فاکتور دیجیتال ساخته و صادر شد.";
            }

            // Navigate back with result
            await Navigation.PopAsync();
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _lastError = exception.Message;
            StatusLabel.Text = exception.Message;
            CopyReportButton.IsVisible = true;
        }
    }

    private async void OnCopyReportClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastError))
            return;

        try
        {
            await Clipboard.Default.SetTextAsync(_lastError);
            CopyStatusLabel.Text = "گزارش کپی شد.";
        }
        catch (Exception exception)
        {
            CopyStatusLabel.Text = $"کپی ناموفق: {exception.Message}";
        }
    }

    private static bool IsExpected(Exception exception) => exception is ArgumentException or
        FormatException or HttpRequestException or JsonException or InvalidOperationException or
        TaskCanceledException;
}
