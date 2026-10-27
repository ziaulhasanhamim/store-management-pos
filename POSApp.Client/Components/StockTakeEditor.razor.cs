using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using POSApp.Client.DTOs;
using POSApp.Client.Services;

namespace POSApp.Client.Components;

public partial class StockTakeEditor : IAsyncDisposable
{
    [Parameter, EditorRequired]
    public StockTakePage InitialData { get; set; } = default!;

    [Parameter]
    public string Currency { get; set; } = "BDT";

    private StockTakePage Data = default!;
    private List<CountRow> Rows = [];
    private string Reason = "";
    private string Search = "";
    private string View = "";
    private string ProductSearch = "";
    private int? SelectedId;
    private int? EditingId;
    private decimal FoundQuantity = 1;
    private bool FoundQuantityValid = true;
    private ElementReference ProductSearchField;
    private DecimalField? FoundQuantityField;
    private bool FocusSearch = true;
    private bool FocusQuantity;
    private int Page = 1;
    private bool Busy;
    private bool Review;
    private bool Confirmed;
    private bool ConfirmRefresh;
    private string? Error;
    private string? Notice;
    private readonly Guid CompletionId = Guid.NewGuid();
    private bool ReadOnly => !Data.CanEdit || Review || ConfirmRefresh;
    private bool HasInvalid => Rows.Any(row => !row.Valid);
    private CountRow? Selected => Rows.FirstOrDefault(row => row.Item.ProductId == SelectedId) ?? PendingSelection;
    private bool CanAdd => !ReadOnly && !Busy && Selected is { Valid: true } && FoundQuantityValid && FoundQuantity > 0;
    private List<CountRow> SearchResults = [];
    private CountRow? PendingSelection;
    private CancellationTokenSource? SearchCancellation;
    private bool Searching;
    private long SearchVersion;
    private Task SearchTask = Task.CompletedTask;
    private List<CountRow> Filtered => Rows.Where(row => row.Item.ProductId == EditingId || (
        (View != "differences" || !row.Valid || row.Difference is { } difference && difference != 0)
        && (string.IsNullOrWhiteSpace(Search) || row.Item.Name.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase)
            || row.Item.Barcode?.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase) == true
            || row.Item.ProductId.ToString() == Search.Trim().TrimStart('#')))).ToList();

    protected override void OnParametersSet()
    {
        if (Data is null || Data.Id != InitialData.Id)
        {
            Load(InitialData);
            if (InitialList is not null)
            {
                SetList(InitialList);
            }
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (RendererInfo.IsInteractive && !Connected)
        {
            await InitializeRecoveryAsync();
        }
        if (!RendererInfo.IsInteractive || ReadOnly || Busy)
        {
            return;
        }
        if (FocusQuantity && Selected is not null && FoundQuantityField is not null)
        {
            FocusQuantity = false;
            await FoundQuantityField.FocusAsync();
        }
        else if (FocusSearch)
        {
            FocusSearch = false;
            await ProductSearchField.FocusAsync(preventScroll: true);
        }
    }

    private Task ProductSearchChanged() => SearchTask = SearchProductsAsync();

    private async Task SearchProductsAsync()
    {
        SearchCancellation?.Cancel();
        SearchCancellation?.Dispose();
        SearchCancellation = new CancellationTokenSource();
        var token = SearchCancellation.Token;
        var version = ++SearchVersion;
        SelectedId = null;
        PendingSelection = null;
        FocusQuantity = false;
        FoundQuantity = 1;
        FoundQuantityValid = true;
        SearchResults = [];
        Searching = !string.IsNullOrWhiteSpace(ProductSearch);
        if (!Searching)
        {
            return;
        }
        try
        {
            await RememberFiltersAsync();
            await Task.Delay(250, token);
            var matches = await Takes.SearchAsync(Data.Id, ProductSearch.Trim(), token);
            if (version != SearchVersion || token.IsCancellationRequested)
            {
                return;
            }
            SearchResults = matches.Select(item => Rows.FirstOrDefault(row => row.Item.ProductId == item.ProductId)
                ?? new CountRow { Item = item }).ToList();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (version == SearchVersion)
            {
                Error = "Unable to search products. Try again; your counts are kept.";
            }
        }
        finally
        {
            if (version == SearchVersion)
            {
                Searching = false;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Disposed = true;
        SaveCancellation?.Cancel();
        SaveCancellation?.Dispose();
        SearchVersion++;
        SearchCancellation?.Cancel();
        SearchCancellation?.Dispose();
        if (DraftModule is not null)
        {
            try
            {
                await DraftModule.DisposeAsync();
            }
            catch (Microsoft.JSInterop.JSDisconnectedException)
            {
            }
        }
    }

    private void SelectProduct(int productId)
    {
        if (ReadOnly || Busy || (Rows.Concat(SearchResults).FirstOrDefault(row => row.Item.ProductId == productId)) is not { } row)
        {
            return;
        }
        PendingSelection = row;
        SelectedId = productId;
        FoundQuantity = 1;
        FoundQuantityValid = true;
        FocusQuantity = true;
        FocusSearch = false;
    }

    private async Task SearchKeyDown(KeyboardEventArgs args)
    {
        if (args.Key != "Enter" || args.Repeat || ReadOnly || Busy)
        {
            return;
        }
        var term = ProductSearch;
        await SearchTask;
        if (term != ProductSearch || ReadOnly || Busy)
        {
            return;
        }
        var exact = SearchResults.Where(row =>
            string.Equals(row.Item.Barcode, ProductSearch.Trim(), StringComparison.OrdinalIgnoreCase)
            || row.Item.ProductId.ToString(CultureInfo.InvariantCulture) == ProductSearch.Trim().TrimStart('#')).ToArray();
        var match = exact.Length == 1 ? exact[0] : SearchResults.Count == 1 ? SearchResults[0] : null;
        if (match is not null)
        {
            SelectProduct(match.Item.ProductId);
        }
    }

    private async Task QuantityKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Enter" && !args.Repeat)
        {
            await AddSelected();
        }
    }

    private async Task ClearSelection()
    {
        SelectedId = null;
        PendingSelection = null;
        SearchCancellation?.Cancel();
        SearchVersion++;
        Searching = false;
        SearchResults = [];
        FocusQuantity = false;
        ProductSearch = "";
        FoundQuantity = 1;
        FoundQuantityValid = true;
        FocusSearch = true;
        await RememberFiltersAsync();
    }

    private async Task AddSelected()
    {
        if (!CanAdd || Selected is not { } row)
        {
            return;
        }
        if (BatchMode)
        {
            await StageAsync(row, FoundQuantity);
            return;
        }
        var total = (row.Count ?? 0) + FoundQuantity;
        if (!ValidCount(row, total))
        {
            Error = "The counted total exceeds the supported quantity or value for this item.";
            return;
        }
        IncludeRow(row);
        SetTotal(row, total);
        Notice = $"{row.Item.Name}: added {FoundQuantity:0.###}. Total counted: {total:0.###} {row.Item.Unit}.";
        Error = null;
        CompleteSelection();
        await ChangedAsync(immediate: true);
    }

    private async Task MarkAllUncountedAsExpectedAsync()
    {
        if (ReadOnly || Busy) return;
        var uncounted = Rows.Where(r => !r.Count.HasValue).ToList();
        if (uncounted.Count == 0) return;

        foreach (var row in uncounted)
        {
            SetTotal(row, row.Item.Expected);
            row.Valid = true;
        }
        Notice = $"{uncounted.Count} items marked with their expected quantities.";
        await ChangedAsync(immediate: true);
    }

    private async Task MarkNoneFound()
    {
        if (ReadOnly || Busy || Selected is not { } row)
        {
            return;
        }
        if (BatchMode)
        {
            await StageAsync(row, null);
            return;
        }
        IncludeRow(row);
        SetTotal(row, 0);
        Notice = $"{row.Item.Name}: counted total set to 0.";
        Error = null;
        CompleteSelection();
        await ChangedAsync(immediate: true);
    }

    private async Task RemoveCount(int productId)
    {
        if (ReadOnly || Busy || Rows.FirstOrDefault(row => row.Item.ProductId == productId) is not { } row)
        {
            return;
        }
        row.Count = null;
        row.Text = "";
        row.Valid = true;
        Rows.Remove(row);
        Notice = $"{row.Item.Name} removed from the counted list. Its stock will stay unchanged.";
        Error = null;
        await ChangedAsync(immediate: true);
    }

    private void IncludeRow(CountRow row)
    {
        Rows.Remove(row);
        Rows.Insert(0, row);
        ListRows.Remove(row);
        ListRows.Insert(0, row);
    }

    private static void SetTotal(CountRow row, decimal total)
    {
        row.Count = total;
        row.Text = total.ToString("0.###", CultureInfo.InvariantCulture);
        row.Valid = true;
    }

    private static bool ValidCount(CountRow row, decimal count) =>
        count >= 0 && count <= 999999999999999.999m && decimal.Round(count, 3) == count
        && (row.Item.AllowFractionUnit || decimal.Truncate(count) == count)
        && (row.Item.UnitCost == 0 || Math.Abs(count - row.Item.Expected) <= 9999999999999999.99m / row.Item.UnitCost);

    private void Load(StockTakePage data)
    {
        Data = data;
        Reason = data.Reason;
        Rows = data.Items.Select(item => new CountRow
        {
            Item = item,
            Count = item.Counted,
            Text = item.Counted?.ToString("0.###", CultureInfo.InvariantCulture) ?? ""
        }).ToList();
        Page = Math.Clamp(Page, 1, Math.Max(1, (Filtered.Count + 29) / 30));
    }

    private async Task SetCount(CountRow row, ChangeEventArgs args)
    {
        if (ReadOnly || Busy || Queued(row.Item.ProductId))
        {
            return;
        }
        row.Text = args.Value?.ToString()?.Trim() ?? "";
        row.Count = null;
        row.Valid = row.Text.Length == 0;
        if (decimal.TryParse(row.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var count)
            && ValidCount(row, count))
        {
            row.Valid = true;
            row.Count = count;
        }
        IncludeRow(row);
        Notice = null;
        await ChangedAsync();
    }

    private void ResetPage() => Page = 1;

    private async Task FinishEditing()
    {
        EditingId = null;
        if (!HasInvalid && await PersistCountsAsync())
        {
            await LoadListAsync();
        }
    }
    private async Task BackToCounting()
    {
        Review = false;
        Confirmed = false;
        View = "added";
        Page = 1;
        await LoadListAsync();
    }

    private Task SaveDraftAsync() => RunAsync(async () =>
    {
        await SaveCountsAsync();
        Notice = BatchRows.Count == 0 ? "Draft saved. Inventory has not changed."
            : "Counts saved. The queued batch is kept on this browser until you add it.";
    });

    private Task ReviewAsync() => RunAsync(async () =>
    {
        if (BatchRows.Count > 0)
        {
            throw new System.ComponentModel.DataAnnotations.ValidationException("Add or remove the queued batch before reviewing adjustments.");
        }
        await SaveCountsAsync();
        View = "differences";
        Search = "";
        CategoryId = null;
        Page = 1;
        Review = true;
        Confirmed = false;
        await LoadListAsync();
    });

    private Task RefreshAsync() => RunAsync(async () =>
    {
        await SaveCountsAsync();
        Load(await Takes.RefreshAsync(Data.Id, new(Data.Revision)));
        ConfirmRefresh = false;
        await LoadListAsync();
        Notice = "Expected stock refreshed. Recount any cleared quantities before reviewing.";
    });

    private Task ApplyAsync() => RunAsync(async () =>
    {
        Load(await Takes.ApplyAsync(Data.Id, new(CompletionId, Data.Revision, Confirmed)));
        Review = false;
        await LoadListAsync();
        Notice = "Stock take applied. Every adjustment is recorded in the item's stock history.";
    });

    private async Task SaveCountsAsync()
    {
        if (!await PersistCountsAsync(force: true))
        {
            throw new System.ComponentModel.DataAnnotations.ValidationException(Error ?? "Correct the draft before continuing.");
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (Busy || HasInvalid)
        {
            return;
        }
        Busy = true;
        Error = null;
        Notice = null;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            Error = exception is InventoryRequestException or System.ComponentModel.DataAnnotations.ValidationException
                ? exception.Message : "Unable to save. Your counts are kept; retry the same action safely.";
        }
        finally
        {
            Busy = false;
        }
    }

    private string Money(decimal amount) => $"{amount:N2} {Currency}";

    private sealed class CountRow
    {
        public required StockTakeLine Item { get; set; }
        public string Text { get; set; } = "";
        public decimal? Count { get; set; }
        public bool Valid { get; set; } = true;
        public decimal? Difference => Count - Item.Expected;
        public decimal Value => Difference is { } difference
            ? decimal.Round(difference * Item.UnitCost, 2, MidpointRounding.AwayFromZero) : 0;
    }
}
