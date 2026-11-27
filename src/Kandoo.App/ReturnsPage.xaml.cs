using System.Security.Cryptography;
using System.Text.Json;

namespace Kandoo.App;

public sealed partial class ReturnsPage : ContentPage
{
    private readonly HttpClient _httpClient = new();
    private readonly List<(CheckBox Box, InvoiceReturnLineCandidate Line)> _lineCheckboxes = new();
    private HubConnectionSettings? _settings;
    private IReadOnlyList<ProductRow> _catalogProducts = Array.Empty<ProductRow>();

    public ReturnsPage(HubConnectionSettings? settings = null)
    {
        _settings = HubConnectionResolver.Resolve(settings) ?? settings;
        InitializeComponent();
        StoreSidebarHelper.AttachSidebar(
            this,
            SidebarHost,
            StoreSidebarSection.Invoices,
            () => _settings);

        StoreSidebarHelper.ApplyRoundedCorners(ReturnFormCard, 12);
        StoreSidebarHelper.ApplyRoundedCorners(RecentEventsCard, 12);
        StoreSidebarHelper.ApplyRoundedCorners(InvoiceNumberBorder, 8);
        StoreSidebarHelper.ApplyRoundedCorners(ProductPickerBorder, 8);
        StoreSidebarHelper.ApplyRoundedCorners(ReturnQuantityBorder, 8);
        StoreSidebarHelper.ApplyRoundedCorners(ReasonBorder, 8);

        RenderLineCandidates(Array.Empty<InvoiceReturnLineCandidate>());
        PopulateCatalogPicker();
        RefreshLocalHistory();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _settings = HubConnectionResolver.Resolve(_settings) ?? _settings;
        RefreshLocalHistory();
        if (HasValidSettings(_settings) && _catalogProducts.Count == 0)
        {
            await TryLoadCatalogProductsAsync();
        }
    }

    private async Task TryLoadCatalogProductsAsync()
    {
        try
        {
            var productSource = new HubProductDataSource(_httpClient, _settings!);
            _catalogProducts = await productSource.LoadProductsAsync(groupId: null, query: null, limit: 200);
            PopulateCatalogPicker();
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            PopulateCatalogPicker();
        }
    }

    private void PopulateCatalogPicker()
    {
        CatalogProductPicker.Items.Clear();
        CatalogProductPicker.Items.Add("بدون اتصال به کالای انبار");
        foreach (var product in _catalogProducts)
        {
            CatalogProductPicker.Items.Add($"{product.CodeDisplay} — {product.Name} (موجودی: {product.StockDisplay})");
        }

        CatalogProductPicker.SelectedIndex = 0;
    }

    private async void OnLookupInvoiceClicked(object? sender, EventArgs e)
    {
        _settings = HubConnectionResolver.Resolve(_settings) ?? _settings;
        if (!HasValidSettings(_settings))
        {
            ShowStatus("تنظیمات هاب یافت نشد؛ می‌توانید شماره فاکتور و کالای مرتبط را دستی انتخاب کنید.", isError: true);
            return;
        }

        LookupInvoiceButton.IsEnabled = false;
        try
        {
            var source = new HubReturnsDataSource(_httpClient, _settings!);
            var lines = await source.TryLoadInvoiceLinesAsync(InvoiceNumberEntry.Text ?? string.Empty);
            RenderLineCandidates(lines);
            if (lines.Count > 0)
            {
                ShowStatus($"{lines.Count} ردیف از فاکتور بارگذاری شد.", isError: false);
            }
            else
            {
                ShowStatus("مسیر خواندن اقلام فاکتور در هاب در دسترس نبود (HTTP 404)؛ ردیف برگشتی را با تعداد و دلیل ثبت کنید.", isError: false);
            }
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            ShowStatus($"خطا در بررسی فاکتور: {exception.Message}", isError: true);
        }
        finally
        {
            LookupInvoiceButton.IsEnabled = true;
        }
    }

    private void RenderLineCandidates(IReadOnlyList<InvoiceReturnLineCandidate> lines)
    {
        _lineCheckboxes.Clear();
        ReturnedItems.Children.Clear();

        if (lines.Count == 0)
        {
            var placeholderRow = new HorizontalStackLayout { Spacing = 8 };
            var check = new CheckBox
            {
                IsChecked = true,
                Color = StoreSidebarHelper.Honey
            };
            placeholderRow.Add(check);
            placeholderRow.Add(new Label
            {
                Text = "کالای فاکتور انتخابی (بر اساس شماره فاکتور بالا)",
                FontFamily = "Vazirmatn",
                FontSize = 14,
                TextColor = StoreSidebarHelper.MutedInk,
                VerticalOptions = LayoutOptions.Center
            });
            ReturnedItems.Add(placeholderRow);
            return;
        }

        foreach (var line in lines)
        {
            var row = new HorizontalStackLayout { Spacing = 8 };
            var box = new CheckBox
            {
                IsChecked = _lineCheckboxes.Count == 0,
                Color = StoreSidebarHelper.Honey
            };
            box.CheckedChanged += (_, args) =>
            {
                if (args.Value && line.ProductId is not null)
                {
                    SelectProductById(line.ProductId);
                }
            };
            row.Add(box);
            row.Add(new Label
            {
                Text = line.DisplayLabel,
                FontFamily = "Vazirmatn",
                FontSize = 15,
                TextColor = StoreSidebarHelper.Ink,
                VerticalOptions = LayoutOptions.Center
            });
            _lineCheckboxes.Add((box, line));
            ReturnedItems.Add(row);
        }

        if (lines[0].ProductId is string firstPid)
        {
            SelectProductById(firstPid);
        }
    }

    private void SelectProductById(string productId)
    {
        for (var i = 0; i < _catalogProducts.Count; i++)
        {
            if (string.Equals(_catalogProducts[i].ProductId, productId, StringComparison.Ordinal))
            {
                CatalogProductPicker.SelectedIndex = i + 1;
                return;
            }
        }
    }

    private async void OnSubmitReturnClicked(object? sender, EventArgs e)
    {
        long? maxSoldMillis = _lineCheckboxes
            .Where(item => item.Box.IsChecked)
            .Select(item => (long?)item.Line.SoldQuantityMillis)
            .FirstOrDefault();

        if (!HubReturnsDataSource.TryValidateReturnForm(
                InvoiceNumberEntry.Text,
                ReturnQuantityEntry.Text,
                ReasonEntry.Text,
                maxSoldMillis,
                out var normalizedInvoiceNumber,
                out var returnQtyMillis,
                out var validationError))
        {
            ShowStatus(validationError, isError: true);
            return;
        }

        _settings = HubConnectionResolver.Resolve(_settings) ?? _settings;
        if (!HasValidSettings(_settings))
        {
            ShowStatus("برای ثبت برگشت و اصلاح موجودی، ابتدا اتصال به هاب را پیکربندی کنید.", isError: true);
            return;
        }

        var refundMethod = RefundCard.IsChecked
            ? ReturnRefundMethod.Card
            : RefundCredit.IsChecked
                ? ReturnRefundMethod.CreditDeduction
                : ReturnRefundMethod.Cash;

        ProductRow? linkedProduct = null;
        var selectedIdx = CatalogProductPicker.SelectedIndex;
        if (selectedIdx > 0 && selectedIdx <= _catalogProducts.Count)
        {
            linkedProduct = _catalogProducts[selectedIdx - 1];
        }

        SubmitReturnButton.IsEnabled = false;
        try
        {
            var source = new HubReturnsDataSource(_httpClient, _settings!);
            var outcome = await source.SubmitReturnAsync(new ReturnSubmissionInput(
                InvoiceNumber: normalizedInvoiceNumber,
                ReturnQuantityMillis: returnQtyMillis,
                Reason: ReasonEntry.Text!.Trim(),
                RefundMethod: refundMethod,
                ProductId: linkedProduct?.ProductId,
                ProductName: linkedProduct?.Name,
                CurrentProductStockMillis: linkedProduct?.StockMillis ?? 0,
                ProductBaseVersion: linkedProduct?.Version ?? 1));

            ShowStatus(outcome.UserMessage, isError: false);
            ReturnQuantityEntry.Text = string.Empty;
            ReasonEntry.Text = string.Empty;

            if (linkedProduct is not null)
            {
                await TryLoadCatalogProductsAsync();
            }

            RefreshLocalHistory();
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            ShowStatus($"ثبت برگشت ناموفق بود: {exception.Message}", isError: true);
        }
        finally
        {
            SubmitReturnButton.IsEnabled = true;
        }
    }

    private void RefreshLocalHistory()
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var records = HubReturnsDataSource.ReadLocalRecords(folder, _settings?.StoreId);
        RecentEventsStack.Children.Clear();
        RecentEventsCard.IsVisible = records.Count > 0;

        foreach (var rec in records.Take(5))
        {
            var productText = string.IsNullOrWhiteSpace(rec.ProductName) ? "بدون کالای انبار" : rec.ProductName;
            RecentEventsStack.Add(new Label
            {
                Text = $"فاکتور {rec.InvoiceNumber} · {productText} × {rec.QuantityDisplay} · بازپرداخت {rec.RefundDisplay} · دلیل: {rec.Reason}",
                FontFamily = "Vazirmatn",
                FontSize = 13.5,
                TextColor = StoreSidebarHelper.Ink
            });
        }
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusLabel.IsVisible = true;
        StatusLabel.Text = message;
        StatusLabel.TextColor = isError ? StoreSidebarHelper.Review : StoreSidebarHelper.Green;
    }

    private static bool HasValidSettings(HubConnectionSettings? settings) =>
        settings is not null &&
        !string.IsNullOrWhiteSpace(settings.BaseUrl) &&
        settings.StoreId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(settings.SessionToken);

    private static bool IsExpected(Exception exception) => exception is ArgumentException or
        FormatException or HttpRequestException or JsonException or InvalidOperationException or
        TaskCanceledException or CryptographicException;
}
