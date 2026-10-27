using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using POSApp.Client.DTOs;
using POSApp.Client.Services;

namespace POSApp.Client.Components;

public partial class StockAdjustmentEditor
{
    [Inject]
    private IStockAdjustmentClient Client { get; set; } = default!;

    [Inject]
    private IReceivingClient Inventory { get; set; } = default!;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    [Parameter, EditorRequired]
    public StockAdjustmentOptions Options { get; set; } = default!;

    [Parameter, EditorRequired]
    public string DraftOwner { get; set; } = "";

    [Parameter]
    public string Currency { get; set; } = "BDT";

    [Parameter]
    public StockAdjustmentHistory[] InitialHistory { get; set; } = [];

    [Parameter]
    public PageInfo? InitialHistoryPagination { get; set; }

    private PageInfo? HistoryPagination;
    private bool HistoryLoading;
    private List<StockAdjustmentHistory> History = [];
    private Completion? Completed;
    private List<EntryRow> Rows = [];
    private IReadOnlyList<StockAdjustmentProduct> Matches = [];
    private int? LocationId;
    private DateOnly Date;
    private string Notes = "";
    private string Search = "";
    private string? Error;
    private string? StorageWarning;
    private EditForm? AdjustmentForm;
    private bool Ready;
    private bool ServerRecoveryReady = true;
    private bool Restored;
    private bool Busy;
    private bool Searching;
    private bool Disposed;
    private bool DialogOpen;
    private bool DialogBusy;
    private bool FocusDialog;
    private string? DialogError;
    private ProductInput NewItem = new();
    private ElementReference DialogElement;
    private ElementReference DialogCloseButton;
    private ElementReference DialogSubmitButton;
    private int Page = 1;
    private int Pages => Math.Max(1, (Rows.Count + 24) / 25);
    private Guid RequestId = Guid.NewGuid();
    private string Version = Guid.NewGuid().ToString("N");
    private string DraftKey => $"pos:stock-adjustment:{DraftOwner}";
    private IJSObjectReference? DraftModule;
    private CancellationTokenSource? SearchCancellation;

    protected override void OnInitialized()
    {
        LocationId = Options.LocationId;
        Date = Options.Date;
        History = InitialHistory.ToList();
        HistoryPagination = InitialHistoryPagination;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (FocusDialog && DialogOpen)
        {
            FocusDialog = false;
            await FocusDialogFirstAsync();
        }
        if (!firstRender)
        {
            return;
        }
        try
        {
            // The shared browser-draft helper stores JSON and clears only matching versions.
            DraftModule = await JS.InvokeAsync<IJSObjectReference>("import", "./js/stock-take-draft.js");
            var draft = await DraftModule.InvokeAsync<StockAdjustmentDraft?>("read", DraftKey);
            if (draft is null)
            {
                var oldKey = $"pos:opening-inventory:{DraftOwner}";
                draft = await DraftModule.InvokeAsync<StockAdjustmentDraft?>("read", oldKey);
                if (draft is not null)
                {
                    await DraftModule.InvokeVoidAsync("save", DraftKey, draft);
                    await DraftModule.InvokeVoidAsync("clear", oldKey, draft.Version);
                }
            }
            if (draft is not null && draft.RequestId != Guid.Empty && draft.Items.Count <= 1000)
            {
                RequestId = draft.RequestId;
                Version = draft.Version;
                LocationId = draft.LocationId;
                Date = draft.Date;
                Notes = draft.Notes;
                Rows = draft.Items.Select(item => new EntryRow(item.Product, item.Quantity, item.UnitCost)).ToList();
                Restored = true;
                if (!Options.Locations.Any(location => location.Id == LocationId))
                {
                    Error = "The saved location is no longer available. Select an active location before adding stock.";
                }
            }
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException)
        {
            StorageWarning = "Browser draft recovery is unavailable. Keep this page open until the stock adjustment is posted.";
        }
        finally
        {
            Ready = true;
            if (!Disposed)
            {
                StateHasChanged();
            }
        }
    }

    private async Task ChangeHistoryPageAsync(int page)
    {
        if (HistoryLoading || Busy)
        {
            return;
        }
        HistoryLoading = true;
        try
        {
            var result = await Client.HistoryAsync(page, HistoryPagination?.Size ?? 30);
            History = result.Items.ToList();
            HistoryPagination = result.Pagination;
        }
        catch (Exception exception)
        {
            Error = Message(exception, "Unable to load adjustment history.");
        }
        finally
        {
            HistoryLoading = false;
        }
    }

    private StockAdjustmentDraft ServerDraftState => new(Version, RequestId, LocationId, Date, Notes,
        Rows.Select(row => new StockAdjustmentLine(row.Product, row.Quantity, row.UnitCost)).ToArray());

    private void RestoreServerDraft(StockAdjustmentDraft draft)
    {
        if (draft.RequestId == Guid.Empty || draft.Items is null || draft.Items.Count > 1000
            || draft.Items.Any(item => item is null || item.Product is null))
        {
            throw new InvalidOperationException("This saved adjustment draft is incompatible. Your copy has been kept.");
        }
        RequestId = draft.RequestId;
        Version = draft.Version;
        LocationId = draft.LocationId;
        Date = draft.Date;
        Notes = draft.Notes;
        Rows = draft.Items.Select(item => new EntryRow(item.Product, item.Quantity, item.UnitCost)).ToList();
        Restored = true;
    }

    private async Task RememberAsync()
    {
        if (DraftModule is null || !Ready)
        {
            return;
        }
        Version = Guid.NewGuid().ToString("N");
        var draft = new StockAdjustmentDraft(Version, RequestId, LocationId, Date, Notes,
            Rows.Select(row => new StockAdjustmentLine(row.Product, row.Quantity, row.UnitCost)).ToArray());
        try
        {
            await DraftModule.InvokeVoidAsync("save", DraftKey, draft);
        }
        catch (JSException)
        {
            StorageWarning = "Your changes could not be backed up in this browser. Keep this page open until posting succeeds.";
        }
    }

    private async Task LocationChangedAsync()
    {
        SearchCancellation?.Cancel();
        Search = "";
        Searching = false;
        Matches = [];
        await RememberAsync();
    }

    private async Task SearchChangedAsync(ChangeEventArgs args)
    {
        Search = args.Value?.ToString() ?? "";
        SearchCancellation?.Cancel();
        SearchCancellation?.Dispose();
        var cancellation = SearchCancellation = new();
        Matches = [];
        Searching = false;
        if (string.IsNullOrWhiteSpace(Search) || LocationId is not { } locationId)
        {
            return;
        }
        Searching = true;
        try
        {
            await Task.Delay(250, cancellation.Token);
            var results = await Client.SearchAsync(locationId, Search, cancellation.Token);
            if (!cancellation.IsCancellationRequested && !Disposed)
            {
                Matches = results;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (!Disposed)
        {
            Error = Message(exception, "Unable to search products. Try again.");
        }
        finally
        {
            if (SearchCancellation == cancellation)
            {
                Searching = false;
            }
        }
    }

    private async Task AddAsync(StockAdjustmentProduct product)
    {
        Error = null;
        var existing = Rows.FirstOrDefault(row => row.Product.Id == product.Id);
        if (existing is not null)
        {
            existing.Quantity += 1;
        }
        else if (Rows.Count < 1000)
        {
            Rows.Add(new(product, 1, product.Cost));
        }
        else
        {
            Error = "Save this adjustment before adding more than 1,000 products.";
            return;
        }
        SearchCancellation?.Cancel();
        Search = "";
        Matches = [];
        Searching = false;
        Page = Pages;
        await RememberAsync();
    }

    private async Task RemoveAsync(EntryRow row)
    {
        Rows.Remove(row);
        Page = Math.Min(Page, Pages);
        await RememberAsync();
    }

    private async Task ClearAsync()
    {
        SearchCancellation?.Cancel();
        Searching = false;
        if (DraftModule is not null)
        {
            try
            {
                await DraftModule.InvokeVoidAsync("clear", DraftKey, Version);
            }
            catch (JSException)
            {
                throw new InvalidOperationException("Unable to clear the browser backup. Retry before leaving this page.");
            }
        }
        Completed = null;
        Rows.Clear();
        RequestId = Guid.NewGuid();
        LocationId = Options.LocationId;
        Date = Options.Date;
        Notes = "";
        Search = "";
        Matches = [];
        Error = null;
        Restored = false;
        Page = 1;
        await RememberAsync();
    }

    private StockAdjustmentEntry[] Entries() =>
        Rows.Select(row => new StockAdjustmentEntry(row.Product.Id, row.Quantity, row.UnitCost)).ToArray();

    private async Task SubmitAsync()
    {
        if (!Ready || Busy || LocationId is not { } locationId)
        {
            return;
        }
        Error = null;
        Completed = null;
        Busy = true;
        try
        {
            var result = await Client.CreateAsync(new(RequestId, locationId, Date, Notes, Entries()));
            RecordPost(result);
            Rows.Clear();
            RequestId = Guid.NewGuid();
            Restored = false;
                Page = 1;
            await RememberAsync();
        }
        catch (Exception exception)
        {
            Error = Message(exception, "Unable to add stock. Your entries have been kept. Retry safely.");
        }
        finally
        {
            Busy = false;
        }
    }

    private void RecordPost(StockAdjustmentResult result)
    {
        Completed = new(result);
        if (!History.Any(row => row.Id == result.Id))
        {
            var location = Options.Locations.FirstOrDefault(item => item.Id == LocationId)?.Name ?? "";
            if (HistoryPagination is null || HistoryPagination.Number == 1)
            {
                History.Insert(0, new(result.Id, location, Date, result.ItemCount, result.Value));
                History = History.Take(HistoryPagination?.Size ?? 30).ToList();
            }
            if (HistoryPagination is not null)
            {
                HistoryPagination = HistoryPagination with { Total = HistoryPagination.Total + 1 };
            }
        }
    }

    private sealed record Completion(StockAdjustmentResult Result);

    private static string Message(Exception exception, string fallback) => exception switch
    {
        ValidationException or InventoryRequestException => exception.Message,
        _ => fallback
    };

    private static decimal Value(EntryRow row) => row.Quantity is > 0 and <= 999999999999999.999m
        && row.UnitCost is >= 0 and <= 9999999999999999.99m
        && (row.UnitCost == 0 || row.Quantity <= 9999999999999999.99m / row.UnitCost)
            ? decimal.Round(row.Quantity * row.UnitCost, 2, MidpointRounding.AwayFromZero) : 0;

    private string Money(decimal value) => $"{value:N2} {Currency}";

    private void OpenItem()
    {
        NewItem = new ProductInput
        {
            Name = Search,
            SupplierId = Options.Suppliers.FirstOrDefault()?.Id ?? 0
        };
        DialogError = null;
        DialogOpen = true;
        FocusDialog = true;
    }

    private void CloseDialog()
    {
        if (!DialogBusy)
        {
            DialogOpen = false;
        }
    }

    private void DialogKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            CloseDialog();
        }
    }

    private Task FocusDialogFirstAsync() =>
        (DialogBusy ? DialogElement : DialogCloseButton).FocusAsync(preventScroll: true).AsTask();

    private Task FocusDialogLastAsync() =>
        (DialogBusy ? DialogElement : DialogSubmitButton).FocusAsync(preventScroll: true).AsTask();

    private async Task SaveItemAsync()
    {
        if (DialogBusy)
        {
            return;
        }
        DialogBusy = true;
        DialogError = null;
        try
        {
            var item = await Inventory.SaveProductAsync(NewItem);
            await AddAsync(new(item.Id, item.Name, item.Barcode, item.Unit, item.AllowFractionUnit, item.Cost ?? 0));
            DialogOpen = false;
        }
        catch (Exception exception)
        {
            DialogError = Message(exception, "Unable to create the item. Your entries have been kept.");
        }
        finally
        {
            DialogBusy = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Disposed = true;
        SearchCancellation?.Cancel();
        SearchCancellation?.Dispose();
        if (DraftModule is not null)
        {
            try
            {
                await DraftModule.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }

    private sealed class EntryRow(StockAdjustmentProduct product, decimal quantity, decimal cost)
    {
        public StockAdjustmentProduct Product { get; } = product;
        public decimal Quantity { get; set; } = quantity;
        public decimal UnitCost { get; set; } = cost;
    }
}
