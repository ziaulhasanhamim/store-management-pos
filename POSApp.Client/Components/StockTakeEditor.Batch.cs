using System.Globalization;
using Microsoft.AspNetCore.Components;
using POSApp.Client.DTOs;

namespace POSApp.Client.Components;

public partial class StockTakeEditor
{
    private bool BatchMode;
    private List<CountRow> BatchRows = [];
    private long BackupVersion;
    private bool BatchInvalid => BatchRows.Any(row => !row.Valid || row.Count is null);
    private bool Queued(int productId) => BatchRows.Any(row => row.Item.ProductId == productId);
    private decimal? QueuedCount(int productId) => BatchRows.FirstOrDefault(row => row.Item.ProductId == productId)?.Count;

    private void CompleteSelection()
    {
        SelectedId = null;
        PendingSelection = null;
        FocusQuantity = false;
        FoundQuantity = 1;
        FoundQuantityValid = true;
        FocusSearch = true;
    }

    private async Task StageAsync(CountRow source, decimal? found)
    {
        var staged = BatchRows.FirstOrDefault(row => row.Item.ProductId == source.Item.ProductId);
        var total = found is null ? 0 : (staged?.Count ?? source.Count ?? 0) + found.Value;
        if (staged is { Valid: false } || !ValidCount(source, total))
        {
            Error = "Correct this item's queued total before adding more quantity.";
            return;
        }
        if (staged is null)
        {
            staged = new CountRow { Item = source.Item };
            BatchRows.Insert(0, staged);
        }
        else
        {
            BatchRows.Remove(staged);
            BatchRows.Insert(0, staged);
        }
        SetTotal(staged, total);
        Notice = $"{source.Item.Name} queued. Add the batch when you have finished entering items.";
        Error = null;
        CompleteSelection();
        await BatchChangedAsync();
    }

    private async Task BatchChangedAsync()
    {
        BackupVersion++;
        await BackupAsync();
    }

    private async Task SetBatchCountAsync(CountRow row, ChangeEventArgs args)
    {
        if (Busy || ReadOnly)
        {
            return;
        }
        row.Text = args.Value?.ToString()?.Trim() ?? "";
        row.Count = null;
        row.Valid = decimal.TryParse(row.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var count)
            && ValidCount(row, count);
        if (row.Valid)
        {
            row.Count = count;
        }
        await BatchChangedAsync();
    }

    private async Task RemoveBatchRowAsync(int productId)
    {
        if (Busy || ReadOnly)
        {
            return;
        }
        BatchRows.RemoveAll(row => row.Item.ProductId == productId);
        await BatchChangedAsync();
    }

    private async Task AddBatchAsync()
    {
        if (Busy || ReadOnly || HasInvalid || BatchInvalid || BatchRows.Count == 0)
        {
            return;
        }
        Busy = true;
        Error = null;
        try
        {
            // Finish earlier inline edits before submitting one atomic batch.
            if (!await PersistCountsAsync())
            {
                return;
            }
            var before = Rows.Select(CloneRow).ToList();
            var beforeVersion = ChangeVersion;
            var count = BatchRows.Count;
            foreach (var staged in BatchRows)
            {
                var row = Rows.FirstOrDefault(row => row.Item.ProductId == staged.Item.ProductId);
                if (row is null)
                {
                    row = new CountRow { Item = staged.Item };
                }
                else
                {
                    Rows.Remove(row);
                }
                Rows.Insert(0, row);
                ListRows.Remove(row);
                ListRows.Insert(0, row);
                SetTotal(row, staged.Count!.Value);
            }
            ChangeVersion++;
            BackupVersion++;
            await BackupAsync();
            if (!await PersistCountsAsync())
            {
                Rows = before;
                ChangeVersion = beforeVersion;
                if (ListData is not null)
                {
                    SetList(ListData);
                }
                await BatchChangedAsync();
                return;
            }
            BatchRows.Clear();
            await BatchChangedAsync();
            Notice = $"Added {count} item counts. Inventory has not changed.";
        }
        finally
        {
            Busy = false;
        }
    }

    private static CountRow CloneRow(CountRow row) => new()
    {
        Item = row.Item,
        Text = row.Text,
        Count = row.Count,
        Valid = row.Valid
    };
}
