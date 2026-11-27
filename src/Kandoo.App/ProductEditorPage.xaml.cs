using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Kandoo.Core;

namespace Kandoo.App;

public partial class ProductEditorPage : ContentPage
{
    private const int MaxCustomFieldCount = 32;
    private readonly HttpClient _httpClient = new();
    private readonly List<CustomFieldRowBinding> _customFieldRows = new();
    private HubConnectionSettings? _settings;
    private IReadOnlyList<GroupOption> _groupOptions = Array.Empty<GroupOption>();
    private ProductRow? _editingProduct;
    private long _currentVersion = 1;
    private long _currentStockMillis;

    public ProductEditorPage(HubConnectionSettings? settings = null)
        : this(settings, existingProduct: null)
    {
    }

    public ProductEditorPage(HubConnectionSettings? settings, ProductRow? existingProduct)
    {
        _settings = HubConnectionResolver.Resolve(settings) ?? settings;
        _editingProduct = existingProduct;

        InitializeComponent();
        StoreSidebarHelper.AttachSidebar(
            this,
            SidebarHost,
            StoreSidebarSection.Products,
            () => _settings);

        ApplyRoundedChrome();

        if (_editingProduct is null)
        {
            ConfigureCreateMode();
        }
        else
        {
            ConfigureEditMode(_editingProduct);
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _settings = HubConnectionResolver.Resolve(_settings) ?? _settings;
        if (HasValidSettings(_settings) && _groupOptions.Count == 0)
        {
            await TryLoadGroupsAsync();
        }
    }

    private void ConfigureCreateMode()
    {
        Title = "ثبت کالای جدید";
        PageTitleLabel.Text = "ثبت کالای جدید";
        CreateModeSection.IsVisible = true;
        EditModeSection.IsVisible = false;

        // Seed one custom operator field row matching Row 4 of Product.dc.html.
        AddCustomFieldRow(initialKey: string.Empty, initialValue: string.Empty);
    }

    private void ConfigureEditMode(ProductRow product)
    {
        Title = "ویرایش کالا";
        PageTitleLabel.Text = "ویرایش کالا";
        CreateModeSection.IsVisible = false;
        EditModeSection.IsVisible = true;

        _currentVersion = Math.Max(1, product.Version);
        _currentStockMillis = product.StockMillis ?? 0;

        EditNameEntry.Text = product.Name;
        EditPriceEntry.Text = product.UnitPriceRials.ToString(CultureInfo.InvariantCulture);
        EditPurchasePriceEntry.Text = product.PurchasePriceRials?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        EditTagEntry.Text = product.ProductTagNumber?.ToString(CultureInfo.InvariantCulture) ?? product.CodeDisplay;
        EditTypeEntry.Text = product.Type ?? string.Empty;
        EditColorEntry.Text = product.Color ?? string.Empty;
        UpdateCurrentStockLabel();
    }

    private void ApplyRoundedChrome()
    {
        StoreSidebarHelper.ApplyRoundedCorners(HintCardBorder, 16);
        StoreSidebarHelper.ApplyRoundedCorners(CreateFormCardBorder, 16);
        StoreSidebarHelper.ApplyRoundedCorners(EditDetailsCardBorder, 16);
        StoreSidebarHelper.ApplyRoundedCorners(EditStockCardBorder, 16);

        Border[] inputBorders =
        {
            CreateNameBorder, CreateTypeBorder, CreateColorBorder, CreatePriceBorder,
            CreatePurchasePriceBorder, CreateTagBorder, CreateStockBorder, CreateGroupBorder,
            EditNameBorder, EditPriceBorder, EditPurchasePriceBorder, EditTagBorder,
            EditTypeBorder, EditColorBorder, EditGroupBorder, StockDeltaBorder, StockReasonBorder
        };
        foreach (var border in inputBorders)
        {
            StoreSidebarHelper.ApplyRoundedCorners(border, 8);
        }
    }

    private async Task TryLoadGroupsAsync()
    {
        try
        {
            var source = new HubProductEditorDataSource(_httpClient, _settings!);
            _groupOptions = await source.LoadGroupOptionsAsync();
            PopulateGroupPicker(CreateGroupPicker, selectedGroupKey: null);
            PopulateGroupPicker(EditGroupPicker, _editingProduct?.GroupId);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            // Group loading is non-blocking; product registration still works without groups.
            PopulateGroupPicker(CreateGroupPicker, selectedGroupKey: null);
            PopulateGroupPicker(EditGroupPicker, _editingProduct?.GroupId);
        }
    }

    private void PopulateGroupPicker(Picker picker, string? selectedGroupKey)
    {
        picker.Items.Clear();
        picker.Items.Add("بدون گروه");
        var selectedIndex = 0;
        for (var i = 0; i < _groupOptions.Count; i++)
        {
            var option = _groupOptions[i];
            picker.Items.Add(option.Name);
            if (!string.IsNullOrWhiteSpace(selectedGroupKey) &&
                (string.Equals(option.GroupId, selectedGroupKey, StringComparison.Ordinal) ||
                 string.Equals(option.Name, selectedGroupKey, StringComparison.Ordinal) ||
                 option.Name.EndsWith($" / {selectedGroupKey}", StringComparison.Ordinal)))
            {
                selectedIndex = i + 1;
            }
        }

        picker.SelectedIndex = selectedIndex;
    }

    private string? GetSelectedGroupId(Picker picker)
    {
        var idx = picker.SelectedIndex;
        if (idx <= 0 || idx > _groupOptions.Count)
        {
            return null;
        }

        return _groupOptions[idx - 1].GroupId;
    }

    private void OnAddCustomFieldClicked(object? sender, EventArgs e)
    {
        if (_customFieldRows.Count >= MaxCustomFieldCount)
        {
            ShowStatus($"حداکثر {MaxCustomFieldCount} فیلد سفارشی قابل افزودن است.", isError: true);
            return;
        }

        AddCustomFieldRow(string.Empty, string.Empty);
    }

    private void AddCustomFieldRow(string initialKey, string initialValue)
    {
        var rowGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(130)),
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(44)),
                new ColumnDefinition(new GridLength(44)),
                new ColumnDefinition(new GridLength(140)),
            },
            ColumnSpacing = 10
        };

        var keyEntry = new Entry
        {
            Text = initialKey,
            Placeholder = "[نام فیلد شما]",
            BackgroundColor = Colors.White,
            TextColor = StoreSidebarHelper.Ink,
            PlaceholderColor = StoreSidebarHelper.MutedInk,
            FontFamily = "Vazirmatn-Bold",
            FontSize = 14,
            MinimumHeightRequest = 44
        };
        var keyBorder = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = StoreSidebarHelper.BorderTan,
            StrokeThickness = 1,
            Padding = new Thickness(6, 0),
            Content = keyEntry
        };
        StoreSidebarHelper.ApplyRoundedCorners(keyBorder, 8);

        var valueEntry = new Entry
        {
            Text = initialValue,
            Placeholder = "[مقدار]",
            BackgroundColor = Colors.White,
            TextColor = StoreSidebarHelper.Ink,
            PlaceholderColor = StoreSidebarHelper.MutedInk,
            FontFamily = "Vazirmatn",
            FontSize = 16,
            MinimumHeightRequest = 44
        };
        var valueBorder = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = StoreSidebarHelper.BorderTan,
            StrokeThickness = 1,
            Padding = new Thickness(8, 0),
            Content = valueEntry
        };
        StoreSidebarHelper.ApplyRoundedCorners(valueBorder, 8);

        var plusButton = new Button
        {
            Text = "+",
            FontFamily = "Vazirmatn-Bold",
            FontSize = 18,
            BackgroundColor = StoreSidebarHelper.Wax,
            TextColor = StoreSidebarHelper.Ink,
            CornerRadius = 8,
            MinimumWidthRequest = 44,
            MinimumHeightRequest = 44,
            Padding = 0
        };
        plusButton.Clicked += OnAddCustomFieldClicked;

        var minusButton = new Button
        {
            Text = "−",
            FontFamily = "Vazirmatn-Bold",
            FontSize = 18,
            BackgroundColor = StoreSidebarHelper.Wax,
            TextColor = StoreSidebarHelper.Ink,
            CornerRadius = 8,
            MinimumWidthRequest = 44,
            MinimumHeightRequest = 44,
            Padding = 0
        };

        var tagLabel = new Label
        {
            Text = "فیلد اضافه‌شده توسط شما",
            FontFamily = "Vazirmatn",
            FontSize = 13,
            TextColor = StoreSidebarHelper.MutedInk,
            VerticalOptions = LayoutOptions.Center
        };

        rowGrid.Add(keyBorder, 0, 0);
        rowGrid.Add(valueBorder, 1, 0);
        rowGrid.Add(plusButton, 2, 0);
        rowGrid.Add(minusButton, 3, 0);
        rowGrid.Add(tagLabel, 4, 0);

        var binding = new CustomFieldRowBinding(rowGrid, keyEntry, valueEntry);
        minusButton.Clicked += (_, _) =>
        {
            _customFieldRows.Remove(binding);
            CustomFieldsContainer.Children.Remove(rowGrid);
        };

        _customFieldRows.Add(binding);
        CustomFieldsContainer.Add(rowGrid);
    }

    private void OnRemoveMandatoryRowClicked(object? sender, EventArgs e)
    {
        ShowStatus("فیلدهای «نام کالا»، «قیمت» و «تعداد در انبار» الزامی هستند و بسته نمی‌شوند.", isError: true);
    }

    private void OnHideTypeRowClicked(object? sender, EventArgs e)
    {
        CreateTypeEntry.Text = string.Empty;
        CreateTypeRow.IsVisible = false;
        UpdateRestoreHiddenButton();
    }

    private void OnHideColorRowClicked(object? sender, EventArgs e)
    {
        CreateColorEntry.Text = string.Empty;
        CreateColorRow.IsVisible = false;
        UpdateRestoreHiddenButton();
    }

    private void OnHidePurchasePriceRowClicked(object? sender, EventArgs e)
    {
        CreatePurchasePriceEntry.Text = string.Empty;
        CreatePurchasePriceRow.IsVisible = false;
        UpdateRestoreHiddenButton();
    }

    private void OnHideTagRowClicked(object? sender, EventArgs e)
    {
        CreateTagEntry.Text = string.Empty;
        CreateTagRow.IsVisible = false;
        UpdateRestoreHiddenButton();
    }

    private void OnRestoreHiddenFieldsClicked(object? sender, EventArgs e)
    {
        CreateTypeRow.IsVisible = true;
        CreateColorRow.IsVisible = true;
        CreatePurchasePriceRow.IsVisible = true;
        CreateTagRow.IsVisible = true;
        UpdateRestoreHiddenButton();
    }

    private void UpdateRestoreHiddenButton()
    {
        RestoreHiddenFieldsButton.IsVisible =
            !CreateTypeRow.IsVisible ||
            !CreateColorRow.IsVisible ||
            !CreatePurchasePriceRow.IsVisible ||
            !CreateTagRow.IsVisible;
    }

    private async void OnCreateSaveClicked(object? sender, EventArgs e)
    {
        if (!HubProductEditorDataSource.TryValidateCreateForm(
                CreateNameEntry.Text,
                CreatePriceEntry.Text,
                CreateStockEntry.Text,
                CreatePurchasePriceRow.IsVisible ? CreatePurchasePriceEntry.Text : null,
                CreateTagRow.IsVisible ? CreateTagEntry.Text : null,
                out var unitPriceRials,
                out var initialStockMillis,
                out var purchasePriceRials,
                out var productTagNumber,
                out var validationError))
        {
            ShowStatus(validationError, isError: true);
            return;
        }

        var operatorFields = new List<ProductOperatorField>();
        foreach (var custom in _customFieldRows)
        {
            var key = custom.KeyEntry.Text?.Trim();
            var val = custom.ValueEntry.Text?.Trim();
            if (string.IsNullOrEmpty(key) && string.IsNullOrEmpty(val))
            {
                continue;
            }

            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(val))
            {
                ShowStatus("برای هر فیلد اضافه‌شده، هم «نام فیلد» و هم «مقدار» باید پر شود یا هر دو خالی بماند.", isError: true);
                return;
            }

            operatorFields.Add(new ProductOperatorField(key, val));
        }

        _settings = HubConnectionResolver.Resolve(_settings) ?? _settings;
        if (!HasValidSettings(_settings))
        {
            ShowStatus("تنظیمات اتصال به هاب و توکن نشست لازم است.", isError: true);
            return;
        }

        CreateSaveButton.IsEnabled = false;
        try
        {
            var source = new HubProductEditorDataSource(_httpClient, _settings!);
            var created = await source.CreateProductDetailedAsync(new ProductEditorCreateRequest(
                Name: CreateNameEntry.Text!.Trim(),
                UnitPriceRials: unitPriceRials,
                InitialStockMillis: initialStockMillis,
                Type: CreateTypeRow.IsVisible ? CreateTypeEntry.Text : null,
                Color: CreateColorRow.IsVisible ? CreateColorEntry.Text : null,
                PurchasePriceRials: purchasePriceRials,
                ProductTagNumber: productTagNumber,
                OperatorFields: operatorFields,
                GroupId: GetSelectedGroupId(CreateGroupPicker)));

            var assignedTagText = created.ProductTagNumber is long tag
                ? $"با تگ رقمی {tag}"
                : $"با شناسه {created.ProductId}";
            ShowStatus($"کالا {assignedTagText} با موفقیت ثبت شد.", isError: false);

            CreateNameEntry.Text = string.Empty;
            CreateTypeEntry.Text = string.Empty;
            CreateColorEntry.Text = string.Empty;
            CreatePriceEntry.Text = string.Empty;
            CreatePurchasePriceEntry.Text = string.Empty;
            CreateTagEntry.Text = string.Empty;
            CreateStockEntry.Text = string.Empty;
            foreach (var custom in _customFieldRows)
            {
                custom.KeyEntry.Text = string.Empty;
                custom.ValueEntry.Text = string.Empty;
            }
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            ShowStatus($"ثبت کالا ناموفق بود: {exception.Message}", isError: true);
        }
        finally
        {
            CreateSaveButton.IsEnabled = true;
        }
    }

    private async void OnEditSaveClicked(object? sender, EventArgs e)
    {
        if (_editingProduct is null)
            return;

        if (string.IsNullOrWhiteSpace(EditNameEntry.Text))
        {
            ShowStatus("نام کالا الزامی است.", isError: true);
            return;
        }

        var normalizedPrice = HubConnectionResolver.NormalizeAsciiDigits(EditPriceEntry.Text);
        if (string.IsNullOrWhiteSpace(normalizedPrice) ||
            !long.TryParse(normalizedPrice, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unitPriceRials) ||
            unitPriceRials < 0)
        {
            ShowStatus("قیمت فروش باید عدد صحیح غیرمنفی باشد.", isError: true);
            return;
        }

        long? purchasePriceRials = null;
        var normalizedPurchase = HubConnectionResolver.NormalizeAsciiDigits(EditPurchasePriceEntry.Text);
        if (!string.IsNullOrWhiteSpace(normalizedPurchase))
        {
            if (!long.TryParse(normalizedPurchase, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPurchase) ||
                parsedPurchase < 0)
            {
                ShowStatus("قیمت خرید معتبر نیست.", isError: true);
                return;
            }

            purchasePriceRials = parsedPurchase;
        }

        _settings = HubConnectionResolver.Resolve(_settings) ?? _settings;
        if (!HasValidSettings(_settings))
        {
            ShowStatus("تنظیمات اتصال به هاب و توکن نشست لازم است.", isError: true);
            return;
        }

        EditSaveButton.IsEnabled = false;
        try
        {
            var source = new HubProductEditorDataSource(_httpClient, _settings!);
            var selectedGroupId = GetSelectedGroupId(EditGroupPicker);
            var updated = await source.UpdateProductDetailedAsync(new ProductEditorUpdateRequest(
                ProductId: _editingProduct.ProductId,
                BaseVersion: _currentVersion,
                Name: EditNameEntry.Text.Trim(),
                Type: EditTypeEntry.Text,
                IncludeType: true,
                Color: EditColorEntry.Text,
                IncludeColor: true,
                UnitPriceRials: unitPriceRials,
                PurchasePriceRials: purchasePriceRials,
                IncludePurchasePrice: true,
                GroupId: selectedGroupId,
                UnassignGroup: EditGroupPicker.SelectedIndex == 0 && _editingProduct.GroupId is not null));

            _currentVersion = updated.Version;
            if (updated.StockMillis is long stockMillis)
            {
                _currentStockMillis = stockMillis;
                UpdateCurrentStockLabel();
            }

            ShowStatus("تغییرات کالا با موفقیت در هاب ذخیره شد.", isError: false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            ShowStatus($"ذخیرهٔ کالا ناموفق بود: {exception.Message}", isError: true);
        }
        finally
        {
            EditSaveButton.IsEnabled = true;
        }
    }

    private async void OnRecordStockAdjustmentClicked(object? sender, EventArgs e)
    {
        if (_editingProduct is null)
            return;

        if (!HubProductEditorDataSource.TryValidateStockAdjustment(
                _currentStockMillis,
                StockDeltaEntry.Text,
                StockReasonEntry.Text,
                out var nextStockMillis,
                out var deltaMillis,
                out var validationError))
        {
            ShowStatus(validationError, isError: true);
            return;
        }

        _settings = HubConnectionResolver.Resolve(_settings) ?? _settings;
        if (!HasValidSettings(_settings))
        {
            ShowStatus("تنظیمات اتصال به هاب و توکن نشست لازم است.", isError: true);
            return;
        }

        RecordStockAdjustmentButton.IsEnabled = false;
        try
        {
            var source = new HubProductEditorDataSource(_httpClient, _settings!);
            var reasonNote = $"آخرین تعدیل موجودی ({HubConnectionResolver.FormatSignedStockDelta(deltaMillis)}): {StockReasonEntry.Text!.Trim()}";
            var updated = await source.UpdateProductDetailedAsync(new ProductEditorUpdateRequest(
                ProductId: _editingProduct.ProductId,
                BaseVersion: _currentVersion,
                StockMillis: nextStockMillis,
                Specification: reasonNote,
                IncludeSpecification: true));

            _currentVersion = updated.Version;
            _currentStockMillis = updated.StockMillis ?? nextStockMillis;
            UpdateCurrentStockLabel();
            StockDeltaEntry.Text = string.Empty;
            StockReasonEntry.Text = string.Empty;

            ShowStatus(
                $"رویداد تغییر موجودی ({HubConnectionResolver.FormatSignedStockDelta(deltaMillis)}) ثبت شد. موجودی جدید: {HubConnectionResolver.FormatStockMillis(_currentStockMillis)}.",
                isError: false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            ShowStatus($"ثبت تغییر موجودی ناموفق بود: {exception.Message}", isError: true);
        }
        finally
        {
            RecordStockAdjustmentButton.IsEnabled = true;
        }
    }

    private async void OnBackToCatalogClicked(object? sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    private void UpdateCurrentStockLabel()
    {
        CurrentStockBadgeLabel.Text =
            $"موجودی فعلی: {HubConnectionResolver.FormatStockMillis(_currentStockMillis)}";
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

    private sealed record CustomFieldRowBinding(Grid RowGrid, Entry KeyEntry, Entry ValueEntry);
}
