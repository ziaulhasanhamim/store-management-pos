using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Client.Services;

namespace POSApp.Client.Components;

public partial class SalesEditor
{
    private LocalDraft<SalesEditorState>? Recovery;
    private SalesEditorState DraftState => new(RequestId, SaleRevision, LocationId, CustomerId, Options.WorkingDate,
        Discount, DiscountValid, Notes, Payments.ToArray(), Lines.Select(line => new SalesEditorLine(
            line.Product, line.Quantity, line.Price, line.QuantityValid, line.PriceValid)).ToArray());

    private void RestoreDraft(SalesEditorState draft)
    {
        if (draft.RequestId == Guid.Empty || draft.Lines is null || draft.Lines.Length > 100 || draft.Payments is null
            || draft.Lines.Any(line => line is null || line.Product is null))
        {
            throw new InvalidOperationException("This saved draft is incompatible. It has been kept; start a new draft or use the server copy.");
        }
        SaleRevision = draft.Revision;
        RequestId = draft.RequestId;
        if (draft.Lines.Length > 0 && draft.LocationId > 0 && Options.Locations.Any(l => l.Id == draft.LocationId))
        {
            LocationId = draft.LocationId;
        }
        else if (Options.DefaultLocationId.HasValue && Options.Locations.Any(l => l.Id == Options.DefaultLocationId.Value))
        {
            LocationId = Options.DefaultLocationId.Value;
        }
        CustomerId = draft.CustomerId;
        if (draft.WorkingDate != Options.WorkingDate)
        {
            Error = "Draft recovered from a different working day. Review the current day before submitting.";
        }
        Discount = draft.Discount;
        DiscountValid = draft.DiscountValid;
        DiscountRevision++;
        Notes = draft.Notes;
        Payments = draft.Payments;
        Lines.Clear();
        Lines.AddRange(draft.Lines.Select(line => new CartLine
        {
            Product = line.Product,
            Quantity = line.Quantity,
            Price = line.Price,
            QuantityValid = line.QuantityValid,
            PriceValid = line.PriceValid
        }));
    }

    [Parameter]
    public SalesOptions InitialOptions { get; set; } = default!;

    [Parameter]
    public string Currency { get; set; } = "BDT";

    private SalesOptions Options = default!;
    private IReadOnlyList<SalesProduct> Products = [];
    private readonly List<CartLine> Lines = [];
    private Guid RequestId = Guid.NewGuid();
    private long SaleRevision;
    private int LocationId;
    private int? CustomerId;
    private string Search = "";
    private int SelectedProductIndex = -1;
    private string? Notes;
    private decimal Discount;
    private int DiscountRevision;
    private IReadOnlyList<PaymentInput> Payments = [];
    private bool ReturnMode;
    private int? OriginalSaleId;
    private string ReturnReceiptSearch = "";
    private bool LookingUpReceipt;
    private string? ReturnReceiptError;
    private SaleBill? LoadedReturnBill;
    private bool IsCurrentDraft => Options.Drafts.Any(draft => draft.RequestId == RequestId);
    private bool Busy;
    private bool DraftReady = true;
    private bool Searching;
    private bool CustomerDialog;
    private bool CameraDialog;
    private bool CameraPending;
    private bool ShowShortcuts;
    private bool DiscountValid = true;
    private string CustomerName = "";
    private string? CustomerPhone;
    private decimal CustomerOpening;
    private DateTime? CustomTransactionTime;
    private string? Error;
    private string? Notice;
    private string? CameraError;
    private string? CustomerError;
    private ElementReference SearchField;
    private ElementReference Video;
    private CancellationTokenSource? SearchCancellation;
    private IJSObjectReference? Scanner;
    private DotNetObjectReference<SalesEditor>? Callback;

    private decimal Subtotal => Lines.Sum(line => line.Total);
    private decimal Total => decimal.Round(Subtotal - Discount, 2, MidpointRounding.AwayFromZero);
    private decimal DiscountLimit => Lines.Where(line => line.Quantity > 0).Sum(line => line.Total);
    private decimal Confirmed => Payments.Sum(payment => payment.Amount);
    private bool PaymentsValid => Payments.Count == 0 || (Total != 0
        && Payments.All(payment => Math.Sign(payment.Amount) == Math.Sign(Total))
        && Payments.Take(Payments.Count - 1).Sum(payment => Math.Abs(payment.Amount)) < Math.Abs(Total)
        && (Math.Abs(Confirmed) <= Math.Abs(Total) || (Total > 0 && Payments[^1].Method == PaymentMethod.Cash)));
    private bool WalkInUnsettled => CustomerId is null && (Total >= 0 ? Confirmed < Total : Confirmed != Total);
    private bool Invalid => Searching || LocationId == 0 || Lines.Count == 0 || !DiscountValid || !PaymentsValid
        || Math.Abs(Subtotal) > 9999999999999999.99m || Discount > DiscountLimit
        || Lines.Any(line => !line.QuantityValid || !line.PriceValid || !line.AmountValid);

    private bool IsDrawerOpen => Options.OpenLocationIds.Contains(LocationId);
    private string SelectedLocationName => Options.Locations.FirstOrDefault(l => l.Id == LocationId)?.Name ?? "this location";

    protected override void OnInitialized()
    {
        Options = InitialOptions;
        if (Options.DefaultLocationId.HasValue && Options.Locations.Any(l => l.Id == Options.DefaultLocationId.Value))
        {
            LocationId = Options.DefaultLocationId.Value;
        }
        else
        {
            LocationId = Options.Locations.FirstOrDefault()?.Id ?? 0;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && RendererInfo.IsInteractive)
        {
            await SearchField.FocusAsync();
            try
            {
                Scanner = await JS.InvokeAsync<IJSObjectReference>("import", "/barcode-scanner.js");
                Callback = DotNetObjectReference.Create(this);
                await Scanner.InvokeVoidAsync("attachKeyboard", SearchField, Callback);
            }
            catch (JSException)
            {
                // Exact barcode matching still works when scanner timing detection is unavailable.
            }
        }
        if (CameraPending)
        {
            CameraPending = false;
            try
            {
                Scanner ??= await JS.InvokeAsync<IJSObjectReference>("import", "/barcode-scanner.js");
                Callback ??= DotNetObjectReference.Create(this);
                await Scanner.InvokeVoidAsync("start", Video, Callback);
            }
            catch (JSException exception)
            {
                CameraError = exception.Message;
                await InvokeAsync(StateHasChanged);
            }
        }
    }

    private async Task HandleShortcutAsync(KeyboardEventArgs args)
    {
        if (Busy || !DraftReady || CustomerDialog || CameraDialog)
        {
            return;
        }
        if (args.Key == "F1" || args.Key == "?" || (args.AltKey && args.Code == "Slash") || ((args.CtrlKey || args.MetaKey) && args.Code == "Slash"))
        {
            ShowShortcuts = !ShowShortcuts;
        }
        else if (args.Key == "Escape" && ShowShortcuts)
        {
            ShowShortcuts = false;
        }
        else if (args.Key == "F2" || (args.AltKey && (args.Code == "KeyS" || args.Key == "s" || args.Key == "S" || args.Key == "ß")) || ((args.CtrlKey || args.MetaKey) && args.ShiftKey && args.Code == "KeyS"))
        {
            await SearchField.FocusAsync();
        }
        else if (args.Key == "F4" && !Invalid)
        {
            await SaveAsync(false);
        }
        else if ((args.Key == "F9" || ((args.CtrlKey || args.MetaKey) && args.Key == "Enter")) && !Invalid && !WalkInUnsettled)
        {
            await SaveAsync(true);
        }
    }

    private string Money(decimal value) => $"{Currency} {value:N2}";

    private Guid? LastScannedLineKey;

    private bool Add(SalesProduct product)
    {
        var line = Lines.FirstOrDefault(line => line.Product.Id == product.Id && (line.Quantity < 0) == ReturnMode);
        bool isIncrement = line is not null;
        if (line is null)
        {
            line = new CartLine { Product = product, Quantity = ReturnMode ? -1 : 1, Price = product.Price };
            Lines.Insert(0, line);
        }
        else
        {
            line.Quantity += ReturnMode ? -1 : 1;
            Lines.Remove(line);
            Lines.Insert(0, line);
        }
        LastScannedLineKey = line.Key;
        Error = null;
        return isIncrement;
    }

    private readonly Dictionary<string, IReadOnlyList<SalesProduct>> SearchCache = new(StringComparer.OrdinalIgnoreCase);

    private async Task SelectProductAsync(SalesProduct product)
    {
        SearchCancellation?.Cancel();
        Add(product);
        Search = "";
        Products = [];
        SelectedProductIndex = -1;
        Searching = false;
        await SearchField.FocusAsync();
    }

    private async Task SearchChangedAsync()
    {
        SearchCancellation?.Cancel();
        var query = Search.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            Products = [];
            Searching = false;
            return;
        }

        var cacheKey = $"{LocationId}:{query}";
        if (SearchCache.TryGetValue(cacheKey, out var cached))
        {
            Products = cached;
            Searching = false;
            var cachedExact = cached.FirstOrDefault(product => !string.IsNullOrEmpty(product.Barcode)
                && string.Equals(product.Barcode, query, StringComparison.OrdinalIgnoreCase));
            if (cachedExact is not null)
            {
                Add(cachedExact);
                Search = "";
                Products = [];
                await SearchField.FocusAsync();
            }
            return;
        }

        Searching = true;
        SearchCancellation?.Dispose();
        SearchCancellation = new CancellationTokenSource();
        var token = SearchCancellation.Token;
        try
        {
            await Task.Delay(200, token);
            var found = await Sales.SearchAsync(LocationId, query, token);
            if (token.IsCancellationRequested)
            {
                return;
            }
            if (SearchCache.Count > 100)
            {
                SearchCache.Clear();
            }
            SearchCache[cacheKey] = found;
            Products = found;
            SelectedProductIndex = found.Count > 0 ? 0 : -1;
            var exact = found.FirstOrDefault(product => !string.IsNullOrEmpty(product.Barcode)
                && string.Equals(product.Barcode, query, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                Add(exact);
                Search = "";
                Products = [];
                SelectedProductIndex = -1;
                await SearchField.FocusAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Error = exception.Message;
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                Searching = false;
            }
        }
    }

    private async Task SearchKeyAsync(KeyboardEventArgs args)
    {
        if (args.Key == "ArrowDown")
        {
            if (Products.Count > 0)
            {
                SelectedProductIndex = Math.Min(SelectedProductIndex + 1, Products.Count - 1);
                try { await JS.InvokeVoidAsync("scrollSelectedIntoView", $"#sales-search-result-{SelectedProductIndex}"); } catch { }
            }
            return;
        }

        if (args.Key == "ArrowUp")
        {
            if (Products.Count > 0)
            {
                SelectedProductIndex = Math.Max(SelectedProductIndex - 1, 0);
                try { await JS.InvokeVoidAsync("scrollSelectedIntoView", $"#sales-search-result-{SelectedProductIndex}"); } catch { }
            }
            return;
        }

        if (args.Key == "Escape")
        {
            SearchCancellation?.Cancel();
            Products = [];
            Search = "";
            SelectedProductIndex = -1;
            return;
        }

        if (args.Key == "Enter")
        {
            if (SelectedProductIndex >= 0 && SelectedProductIndex < Products.Count)
            {
                var selected = Products[SelectedProductIndex];
                await SelectProductAsync(selected);
            }
            else
            {
                SelectedProductIndex = -1;
                await ScanAsync(Search.Trim());
            }
        }
    }

    private static BarcodeScanOutcome EvaluateScan(string code, IReadOnlyList<SalesProduct> results)
    {
        var exact = results.FirstOrDefault(product => !string.IsNullOrEmpty(product.Barcode)
            && string.Equals(product.Barcode, code, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return new ProductFound(exact);
        }
        if (results.Count == 1)
        {
            return new ProductFound(results[0]);
        }
        if (results.Count > 1)
        {
            List<SalesProduct> matches = results.ToList();
            return new AmbiguousMatches(matches);
        }
        return new BarcodeNotFound(code);
    }

    private async Task ScanAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }
        SearchCancellation?.Cancel();
        Search = "";
        Products = [];
        Searching = true;
        try
        {
            var results = await Sales.SearchAsync(LocationId, code);
            var outcome = EvaluateScan(code, results);
            switch (outcome)
            {
                case ProductFound(var product):
                    Add(product);
                    await SearchField.FocusAsync();
                    break;
                case AmbiguousMatches(var matches):
                    Products = matches;
                    SelectedProductIndex = 0;
                    Error = null;
                    if (string.IsNullOrEmpty(Search))
                    {
                        Search = code;
                    }
                    break;
                case BarcodeNotFound:
                    Error = $"Item with barcode \"{code}\" was not found.";
                    Products = [];
                    break;
            }
        }
        catch (Exception exception)
        {
            Error = exception.Message;
        }
        finally
        {
            Searching = false;
        }
    }

    private async Task ChangeLocationAsync(ChangeEventArgs args)
    {
        if (Lines.Count > 0 || !int.TryParse(args.Value?.ToString(), out var location))
        {
            return;
        }
        LocationId = location;
        try
        {
            Products = await Sales.SearchAsync(LocationId, Search);
        }
        catch (Exception exception)
        {
            Error = exception.Message;
        }
    }

    private async Task SaveAsync(bool complete)
    {
        Busy = true;
        Error = null;
        try
        {
            DateTimeOffset? transactionTime = null;
            if (CustomTransactionTime.HasValue)
            {
                var unspecified = DateTime.SpecifyKind(CustomTransactionTime.Value, DateTimeKind.Unspecified);
                var zone = UserTimeZone.ResolveZone(null, "Asia/Dhaka");
                var utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
                transactionTime = new DateTimeOffset(utc, TimeSpan.Zero);
            }

            var command = new SalesCommand(RequestId, LocationId, CustomerId,
                Lines.Select(line => new SalesLine(line.Product.Id, line.Quantity, line.Price)).ToArray(),
                Discount, 0, PaymentMethod.Cash, Notes)
            {
                Revision = SaleRevision,
                WorkingDate = Options.WorkingDate,
                Payments = Payments.ToArray(),
                OriginalSaleId = OriginalSaleId,
                TransactionTime = transactionTime
            };
            var result = await Sales.SaveAsync(command, complete);
            SaleRevision = result.Revision;
            if (result.Completed)
            {
                if (Recovery is not null)
                {
                    await Recovery.ClearAsync();
                }
                Navigation.NavigateTo($"/sales/{result.Id}/bill");
            }
            else
            {
                Options = await Sales.OptionsAsync();
                Notice = "Draft saved. Stock and balances will update when you complete the sale.";
            }
        }
        catch (Exception exception)
        {
            Error = exception.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task ResumeAsync(ChangeEventArgs args)
    {
        if (!Guid.TryParse(args.Value?.ToString(), out var id))
        {
            return;
        }
        Busy = true;
        try
        {
            var draft = await Sales.DraftAsync(id);
            if (draft is null)
            {
                Error = "This draft is no longer available.";
                return;
            }
            SaleRevision = draft.Revision;
            RequestId = draft.RequestId;
            OriginalSaleId = draft.OriginalSaleId;
            DiscountRevision++;
            LocationId = draft.LocationId;
            CustomerId = draft.CustomerId;
            Lines.Clear();
            Lines.AddRange(draft.Lines.Select(line => new CartLine
            {
                Product = line.Product,
                Quantity = line.Quantity,
                Price = line.UnitPrice
            }));
            Discount = draft.Discount;
            Payments = draft.Payments ?? (draft.Payment > 0 ? [new PaymentInput(draft.Method, draft.Payment)] : []);
            Notes = draft.Notes;
            Notice = "Draft resumed.";
            Error = null;
            Products = await Sales.SearchAsync(LocationId, null);
        }
        catch (Exception exception)
        {
            Error = exception.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    private void NewSale()
    {
        SearchCancellation?.Cancel();
        Searching = false;
        Search = "";
        DiscountRevision++;
        SaleRevision = 0;
        RequestId = Guid.NewGuid();
        if (Options.DefaultLocationId.HasValue && Options.Locations.Any(l => l.Id == Options.DefaultLocationId.Value))
        {
            LocationId = Options.DefaultLocationId.Value;
        }
        Lines.Clear();
        ReturnMode = false;
        OriginalSaleId = null;
        LoadedReturnBill = null;
        ReturnReceiptSearch = "";
        ReturnReceiptError = null;
        CustomerId = null;
        Discount = 0;
        Payments = [];
        DiscountValid = true;
        Notes = null;
        Notice = null;
        Error = null;
    }

    private async Task DiscardActiveDraftAsync()
    {
        if (!IsCurrentDraft) return;
        Busy = true;
        try
        {
            var targetId = RequestId;
            await Sales.DiscardDraftAsync(targetId);
            Options = Options with { Drafts = Options.Drafts.Where(d => d.RequestId != targetId).ToArray() };
            if (Recovery is not null)
            {
                await Recovery.RequestResetAsync();
            }
            else
            {
                NewSale();
            }
            Notice = "Draft discarded.";
            Error = null;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task ReturnReceiptKeyAsync(KeyboardEventArgs args)
    {
        if (args.Key == "Enter")
        {
            await LookupReturnReceiptAsync();
        }
    }

    private async Task LookupReturnReceiptAsync()
    {
        if (string.IsNullOrWhiteSpace(ReturnReceiptSearch)) return;
        LookingUpReceipt = true;
        ReturnReceiptError = null;
        LoadedReturnBill = null;
        try
        {
            var bill = await Sales.FindReceiptAsync(ReturnReceiptSearch.Trim());
            if (bill is null)
            {
                ReturnReceiptError = $"Receipt '{ReturnReceiptSearch.Trim()}' was not found or is voided.";
            }
            else
            {
                LoadedReturnBill = bill;
            }
        }
        catch (Exception ex)
        {
            ReturnReceiptError = ex.Message;
        }
        finally
        {
            LookingUpReceipt = false;
        }
    }

    private async Task LoadReturnLinesAsync()
    {
        if (LoadedReturnBill is null) return;
        Busy = true;
        try
        {
            OriginalSaleId = LoadedReturnBill.Id;
            if (!string.IsNullOrWhiteSpace(LoadedReturnBill.Customer))
            {
                var matched = Options.Customers.FirstOrDefault(c => string.Equals(c.Name, LoadedReturnBill.Customer, StringComparison.OrdinalIgnoreCase));
                if (matched is not null)
                {
                    CustomerId = matched.Id;
                }
            }

            var locationProducts = await Sales.SearchAsync(LocationId, null);
            foreach (var billLine in LoadedReturnBill.Lines)
            {
                var product = locationProducts.FirstOrDefault(p => p.Id == billLine.ProductId)
                    ?? new SalesProduct(billLine.ProductId, billLine.Name, billLine.Barcode, billLine.Unit, billLine.UnitPrice, 0, true);

                Lines.Add(new CartLine
                {
                    Product = product,
                    Quantity = -Math.Abs(billLine.Quantity),
                    Price = billLine.UnitPrice
                });
            }

            Notice = $"Loaded {LoadedReturnBill.Lines.Length} items from receipt {LoadedReturnBill.Number} as returns.";
            LoadedReturnBill = null;
            ReturnReceiptSearch = "";
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    private void OpenCustomer()
    {
        CustomerError = null;
        CustomerDialog = true;
    }

    private async Task AddCustomerAsync()
    {
        Busy = true;
        try
        {
            CustomerId = await Sales.AddCustomerAsync(new CustomerInput(CustomerName, CustomerPhone, CustomerOpening));
            Options = await Sales.OptionsAsync();
            CustomerDialog = false;
            CustomerName = "";
            CustomerPhone = null;
            CustomerOpening = 0;
        }
        catch (Exception exception)
        {
            CustomerError = exception.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    private void OpenCamera()
    {
        CameraError = null;
        CameraDialog = true;
        CameraPending = true;
    }

    private async Task CloseDialogsAsync()
    {
        if (Scanner is not null)
        {
            try
            {
                await Scanner.InvokeVoidAsync("stop");
            }
            catch (JSDisconnectedException)
            {
            }
        }
        CameraDialog = false;
        CustomerDialog = false;
    }

    [JSInvokable]
    public Task KeyboardScanned(string code) => InvokeAsync(async () =>
    {
        if (!Busy && !CustomerDialog && !CameraDialog)
        {
            await ScanAsync(code);
            StateHasChanged();
        }
    });

    [JSInvokable]
    public Task Scanned(string code) => InvokeAsync(async () =>
    {
        await CloseDialogsAsync();
        await ScanAsync(code);
        StateHasChanged();
    });

    [JSInvokable]
    public Task ScanFailed(string message) => InvokeAsync(() =>
    {
        if (CameraDialog)
        {
            CameraError = message;
        }
        else
        {
            Error = message;
        }
        StateHasChanged();
    });

    private async Task ScrollToCheckoutAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("scrollSelectedIntoView", "#checkout-section");
        }
        catch (JSDisconnectedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        SearchCancellation?.Cancel();
        SearchCancellation?.Dispose();
        if (Scanner is not null)
        {
            try
            {
                await Scanner.InvokeVoidAsync("detachKeyboard", SearchField);
                await Scanner.InvokeVoidAsync("stop");
                await Scanner.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }
        Callback?.Dispose();
    }

    private sealed class CartLine
    {
        public Guid Key { get; } = Guid.NewGuid();
        public required SalesProduct Product { get; init; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public bool QuantityValid { get; set; } = true;
        public bool PriceValid { get; set; } = true;
        public bool AmountValid => Price >= 0 && Quantity != 0
            && (Price == 0 || Math.Abs(Quantity) <= 9999999999999999.99m / Price);
        public decimal Total => AmountValid
            ? decimal.Round(Quantity * Price, 2, MidpointRounding.AwayFromZero)
            : 0;
    }
}
