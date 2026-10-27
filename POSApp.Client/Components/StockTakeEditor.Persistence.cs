using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using POSApp.Client.DTOs;
using POSApp.Client.Services;

namespace POSApp.Client.Components;

public partial class StockTakeEditor
{
    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    [Parameter]
    public string? DraftOwner { get; set; }

    private readonly SemaphoreSlim SaveGate = new(1, 1);
    private readonly string SessionId = Guid.NewGuid().ToString("N");
    private long ChangeVersion;
    private long SavedVersion;
    private CancellationTokenSource? SaveCancellation;
    private IJSObjectReference? DraftModule;
    private StockTakeLocalDraft? Recovery;
    private bool Connected;
    private bool RecoveryReady;
    private bool ServerRecoveryReady;
    private bool Disposed;
    private bool Saving;
    private string? StorageWarning;
    private bool CountsDirty => ChangeVersion != SavedVersion || HasInvalid;
    private bool Unsaved => CountsDirty || BatchRows.Count > 0;
    private string DraftKey => $"pos:stock-take:{DraftOwner}:{Data.Id}";
    private string DraftVersion => $"{SessionId}:{BackupVersion}";
    private string SaveStatus => Saving ? "Saving changes…" : HasInvalid ? "Fix invalid counts · draft kept on this browser"
        : CountsDirty ? Error is null ? "Changes pending…" : "Not saved · retry saving"
        : BatchRows.Count > 0 ? $"Batch draft · {BatchRows.Count} items queued on this browser" : "All changes saved";

    private async Task InitializeRecoveryAsync()
    {
        Connected = true;
        if (!Data.CanEdit || string.IsNullOrEmpty(DraftOwner))
        {
            RecoveryReady = true;
            await RenderAsync();
            return;
        }
        try
        {
            DraftModule = await JS.InvokeAsync<IJSObjectReference>("import", "./js/stock-take-draft.js");
            await RestoreFiltersAsync();
            var draft = await DraftModule.InvokeAsync<StockTakeLocalDraft?>("read", DraftKey);
            if (draft is null)
            {
                return;
            }
            if (MatchesSaved(draft) && draft.Batch.Count == 0)
            {
                await DraftModule.InvokeVoidAsync("clear", DraftKey, draft.Version);
            }
            else if (draft.Revision == Data.Revision || MatchesSaved(draft))
            {
                await RestoreDraftAsync(draft);
            }
            else
            {
                Recovery = draft;
            }
        }
        catch (JSException)
        {
            StorageWarning = "Browser recovery is unavailable. Wait for “All changes saved” before leaving.";
        }
        finally
        {
            RecoveryReady = true;
            if (!Disposed && !string.IsNullOrWhiteSpace(ProductSearch))
            {
                await ProductSearchChanged();
            }
            await RenderAsync();
        }
    }

    private bool MatchesSaved(StockTakeLocalDraft draft) => draft.Reason.Trim() == Data.Reason
        && draft.Rows.Count == Data.Items.Count
        && draft.Rows.All(row => row.Valid && Data.Items.Any(item => item.ProductId == row.Item.ProductId
            && item.Counted == row.Count));

    private async Task RestoreDraftAsync(StockTakeLocalDraft draft)
    {
        Reason = draft.Reason;
        Rows = draft.Rows.Select(saved =>
        {
            var server = Data.Items.FirstOrDefault(item => item.ProductId == saved.Item.ProductId);
            var changed = server is not null && (server.Expected != saved.Item.Expected
                || server.LastMovementId != saved.Item.LastMovementId || server.UnitCost != saved.Item.UnitCost);
            return new CountRow
            {
                Item = server ?? saved.Item,
                Count = changed ? null : saved.Count,
                Text = saved.Text,
                Valid = !changed && saved.Valid
            };
        }).ToList();
        if (ListData is not null)
        {
            SetList(ListData);
        }
        BatchMode = draft.BatchMode || draft.Batch.Count > 0;
        BatchRows = draft.Batch.Select(row => new CountRow
        {
            Item = row.Item,
            Count = row.Count,
            Text = row.Text,
            Valid = row.Valid
        }).ToList();
        Recovery = null;
        Notice = "Recovered your unsaved draft. Check any invalid quantities before continuing.";
        await ChangedAsync(immediate: true);
    }

    private Task RestoreRecoveryAsync() => Recovery is { } draft ? RestoreDraftAsync(draft) : Task.CompletedTask;

    private async Task DiscardRecoveryAsync()
    {
        if (Recovery is { } draft && DraftModule is not null)
        {
            try
            {
                await DraftModule.InvokeVoidAsync("clear", DraftKey, draft.Version);
            }
            catch (JSException)
            {
                StorageWarning = "Unable to clear browser recovery. The server version has been kept.";
            }
        }
        Recovery = null;
    }

    private StockTakeLocalDraft ServerRecoveryState => new(DraftVersion, Data.Revision, Reason,
        Rows.Select(row => new StockTakeLocalRow(row.Item, row.Text, row.Count, row.Valid)).ToArray())
    {
        BatchMode = BatchMode,
        Batch = BatchRows.Select(row => new StockTakeLocalRow(row.Item, row.Text, row.Count, row.Valid)).ToArray()
    };

    private async Task RestoreServerRecoveryAsync(StockTakeLocalDraft draft)
    {
        if (draft.Reason is null || draft.Rows is null || draft.Batch is null
            || draft.Rows.Any(row => row is null || row.Item is null)
            || draft.Batch.Any(row => row is null || row.Item is null))
        {
            throw new InvalidOperationException("This saved count draft is incompatible. Your copy has been kept.");
        }
        if (draft.Revision != Data.Revision && !MatchesSaved(draft))
        {
            Recovery = draft;
            return;
        }
        await RestoreDraftAsync(draft);
    }

    private async Task UseSavedCountsAsync()
    {
        SaveCancellation?.Cancel();
        await SaveGate.WaitAsync();
        try
        {
            Load(await Takes.ReadAsync(Data.Id));
            BatchRows = [];
            Recovery = null;
            SavedVersion = ChangeVersion;
            await BackupAsync();
            await LoadListAsync();
        }
        finally
        {
            SaveGate.Release();
        }
    }

    private async Task BackupAsync()
    {
        if (DraftModule is null)
        {
            return;
        }
        try
        {
            await DraftModule.InvokeVoidAsync("save", DraftKey, new StockTakeLocalDraft(DraftVersion, Data.Revision,
                Reason, Rows.Select(row => new StockTakeLocalRow(row.Item, row.Text, row.Count, row.Valid)).ToArray())
            {
                BatchMode = BatchMode,
                Batch = BatchRows.Select(row => new StockTakeLocalRow(row.Item, row.Text, row.Count, row.Valid)).ToArray()
            });
        }
        catch (JSException)
        {
            StorageWarning = "Browser recovery is unavailable. Wait for “All changes saved” before leaving.";
        }
    }

    private async Task ChangedAsync(bool immediate = false)
    {
        ChangeVersion++;
        BackupVersion++;
        SaveCancellation?.Cancel();
        SaveCancellation?.Dispose();
        SaveCancellation = new CancellationTokenSource();
        var token = SaveCancellation.Token;
        await BackupAsync();
        if (HasInvalid || Recovery is not null)
        {
            return;
        }
        try
        {
            if (!immediate)
            {
                await Task.Delay(450, token);
            }
            if (!token.IsCancellationRequested)
            {
                await PersistCountsAsync();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private async Task<bool> PersistCountsAsync(bool force = false)
    {
        await SaveGate.WaitAsync();
        try
        {
            if (HasInvalid || Recovery is not null)
            {
                return false;
            }
            if (!force && !CountsDirty)
            {
                return true;
            }
            Saving = true;
            await RenderAsync();
            var version = ChangeVersion;
            var backupVersion = DraftVersion;
            var counts = Rows.Select(row => new StockTakeCount(row.Item.ProductId, row.Count)
            {
                Snapshot = row.Item
            }).ToArray();
            var saved = await Takes.SaveAsync(Data.Id, new(Data.Revision, Reason, counts));
            Data = saved;
            foreach (var row in Rows)
            {
                if (saved.Items.FirstOrDefault(item => item.ProductId == row.Item.ProductId) is { } item)
                {
                    row.Item = item;
                }
            }
            SearchResults = SearchResults.Select(result => Rows.FirstOrDefault(row => row.Item.ProductId == result.Item.ProductId)
                ?? result).ToList();
            SavedVersion = version;
            Error = null;
            if (version == ChangeVersion && backupVersion == DraftVersion && BatchRows.Count == 0)
            {
                if (DraftModule is not null)
                {
                    try
                    {
                        await DraftModule.InvokeVoidAsync("clear", DraftKey, backupVersion);
                    }
                    catch (JSException)
                    {
                        StorageWarning = "Changes are saved, but browser recovery could not be cleared.";
                    }
                }
            }
            else
            {
                await BackupAsync();
            }
            if (!CountsDirty && EditingId is null)
            {
                await LoadListAsync();
            }
            return true;
        }
        catch (Exception exception)
        {
            Error = exception is InventoryRequestException or ValidationException
                ? exception.Message : "Unable to save changes. Your draft is kept; use Save draft to retry.";
            return false;
        }
        finally
        {
            Saving = false;
            SaveGate.Release();
            await RenderAsync();
        }
    }

    private Task RenderAsync() => Connected && !Disposed ? InvokeAsync(StateHasChanged) : Task.CompletedTask;

    private async Task BeforeNavigationAsync(LocationChangingContext context)
    {
        if (Unsaved && !await PersistCountsAsync())
        {
            context.PreventNavigation();
        }
    }
}
