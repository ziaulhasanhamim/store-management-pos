using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;
using POSApp.Client.DTOs;

namespace POSApp.Client.Components;

public partial class StockTakeEditor
{
    [Parameter]
    public StockTakeList? InitialList { get; set; }

    private StockTakeList? ListData;
    private List<CountRow> ListRows = [];
    private string Sort = "name";
    private int? CategoryId;
    private int PageSize = 30;
    private bool ListLoading;
    private long ListVersion;
    private IEnumerable<CountRow> HiddenInvalid => Rows.Where(row => (!row.Valid || row.Item.ProductId == EditingId) && !ListRows.Contains(row));
    private int MatchingCount => ListData?.Count ?? Filtered.Count;

    private void SetList(StockTakeList data)
    {
        ListData = data;
        Page = data.Page;
        ListRows = data.Rows.Select(row => Rows.FirstOrDefault(added => added.Item.ProductId == row.Item.ProductId)
            ?? new CountRow { Item = row.Item, Count = row.Item.Counted, Text = row.Item.Counted?.ToString("0.###") ?? "" }).ToList();
    }

    private async Task LoadListAsync()
    {
        var version = ++ListVersion;
        ListLoading = true;
        await RenderAsync();
        try
        {
            var data = await Takes.BrowseAsync(Data.Id, Search, View, CategoryId, Sort, Page, PageSize);
            if (version == ListVersion)
            {
                SetList(data);
                if (Error == "Unable to load the product list. Retry the search; your counts are kept.")
                {
                    Error = null;
                }
            }
        }
        catch (Exception)
        {
            Error = "Unable to load the product list. Retry the search; your counts are kept.";
        }
        finally
        {
            if (version == ListVersion)
            {
                ListLoading = false;
                await RenderAsync();
            }
        }
    }

    private async Task FilterListAsync()
    {
        Page = 1;
        await RememberFiltersAsync();
        if (HasInvalid || Unsaved && !await PersistCountsAsync())
        {
            return;
        }
        await LoadListAsync();
    }

    private async Task SortListAsync(string sort)
    {
        Sort = sort;
        await FilterListAsync();
    }

    private async Task ChangePageAsync(int change)
    {
        if (HasInvalid || Unsaved && !await PersistCountsAsync())
        {
            return;
        }
        Page += change;
        await RememberFiltersAsync();
        await LoadListAsync();
    }

    private async Task RememberFiltersAsync()
    {
        if (DraftModule is null)
        {
            return;
        }
        try
        {
            await DraftModule.InvokeVoidAsync("save", DraftKey + ":filters", new StockTakeFilters(
                Search, View, CategoryId, Sort, Page, PageSize, ProductSearch));
        }
        catch (Microsoft.JSInterop.JSException)
        {
            StorageWarning = "Unable to remember filters on this browser.";
        }
    }

    private async Task RestoreFiltersAsync()
    {
        if (DraftModule is null)
        {
            return;
        }
        try
        {
            var filters = await DraftModule.InvokeAsync<StockTakeFilters?>("read", DraftKey + ":filters");
            if (filters is null)
            {
                return;
            }
            Search = filters.Search;
            View = filters.View;
            CategoryId = filters.CategoryId;
            Sort = filters.Sort;
            Page = Math.Max(1, filters.Page);
            PageSize = filters.PageSize is 30 or 50 or 100 ? filters.PageSize : 30;
            ProductSearch = filters.ProductSearch;
            Searching = !string.IsNullOrWhiteSpace(ProductSearch);
            await LoadListAsync();
        }
        catch (JSException)
        {
            StorageWarning = "Unable to restore filters on this browser.";
        }
    }

    private bool Added(CountRow row) => Rows.Any(added => added.Item.ProductId == row.Item.ProductId);

    private string RowStatus(CountRow row) => !row.Valid ? "Check count" : row.Difference switch
    {
        < 0 => "Shortage",
        > 0 => "Extra",
        0 => "Matched",
        _ => Added(row) ? "Recount needed" : "Not counted"
    };
}
