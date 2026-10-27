using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using POSApp.Client.DTOs;

namespace POSApp.Client.Components;

public partial class EditorDraft<TState>
{
    [Parameter, EditorRequired]
    public string Kind { get; set; } = "";

    [Parameter, EditorRequired]
    public TState State { get; set; } = default!;

    [Parameter]
    public EventCallback<bool> ReadyChanged { get; set; }

    [Parameter]
    public bool Empty { get; set; }

    [Parameter]
    public bool Paused { get; set; }

    [Parameter]
    public EventCallback<TState> Restore { get; set; }

    [Parameter]
    public EventCallback Clear { get; set; }

    [Parameter]
    public bool ShowResetAction { get; set; } = true;

    [Parameter]
    public string ResetLabel { get; set; } = "New entry";

    [Parameter]
    public string ResetPrompt { get; set; } = "Discard the current entries and clear this form’s saved draft? Submitted records remain unchanged.";

    private bool ConfirmReset;
    private bool Resetting;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private IJSObjectReference? Module;
    private string Key = "";
    private string? LastPayload;
    private string? PendingPayload;
    private long Revision;
    private bool Ready;
    private bool Disposed;
    private bool Conflict;
    private string Status = "Draft ready";
    private string? Warning;
    private EditorDraftDocument? Server;
    private CancellationTokenSource? Delay;
    private readonly SemaphoreSlim SaveLock = new(1, 1);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!RendererInfo.IsInteractive || Disposed)
        {
            return;
        }
        if (firstRender)
        {
            try
            {
                await InitializeAsync();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Warning = "Draft recovery is unavailable. Your existing server draft has not been changed. " + exception.Message;
                Conflict = true;
                Ready = true;
                await ReadyChanged.InvokeAsync(true);
                await InvokeAsync(StateHasChanged);
            }
            return;
        }
        if (!Ready || Paused || Resetting)
        {
            return;
        }
        var payload = Empty ? "null" : JsonSerializer.Serialize(State, JsonOptions);
        if (payload == LastPayload)
        {
            return;
        }
        LastPayload = payload;
        PendingPayload = payload;
        try
        {
            if (Module is not null)
            {
                await Module.InvokeVoidAsync("save", Key,
                    new EditorDraftDocument(Revision, payload, DateTimeOffset.UtcNow, Guid.NewGuid()) { Pending = true });
            }
            Status = "Saved on this browser · syncing…";
        }
        catch (JSException)
        {
            Warning = "Browser recovery unavailable; keep this page open until server save completes.";
        }
        if (Conflict)
        {
            Status = "Local draft kept · resolve the server conflict before syncing";
            await InvokeAsync(StateHasChanged);
            return;
        }
        Delay?.Cancel();
        Delay?.Dispose();
        Delay = new CancellationTokenSource();
        _ = SaveLaterAsync(Delay.Token);
        await InvokeAsync(StateHasChanged);
    }

    private async Task InitializeAsync()
    {
        var owner = await Drafts.OwnerAsync();
        if (string.IsNullOrWhiteSpace(owner))
        {
            Warning = "Draft recovery is unavailable until you sign in.";
            return;
        }
        Key = $"pos:editor:{owner}:{Kind}";
        EditorDraftDocument? local = null;
        try
        {
            Module = await JS.InvokeAsync<IJSObjectReference>("import", "./js/stock-take-draft.js");
            Key += ":" + await Module.InvokeAsync<string>("tabId");
            local = await Module.InvokeAsync<EditorDraftDocument?>("read", Key);
        }
        catch (Exception exception) when (exception is JSException or JsonException)
        {
            Warning = "Browser recovery unavailable.";
        }
        try
        {
            Server = await Drafts.ReadAsync(Kind);
            Revision = Server?.Revision ?? 0;
        }
        catch (Exception)
        {
            Warning = "Server drafts unavailable. Local recovery is retained; refresh to reconnect.";
            // Never overwrite a server draft whose revision could not be read.
            Conflict = true;
        }
        var recover = local is not null && (Server is null || local.Pending || local.UpdatedAt > Server.UpdatedAt) ? local : Server;
        if (local is not null && Server is not null && local.Revision != Server.Revision
            && local.Payload != Server.Payload && (local.Pending || local.UpdatedAt > Server.UpdatedAt))
        {
            Conflict = true;
            Warning = "Another tab changed the server draft. Your browser copy has been recovered without overwriting it.";
        }
        if (recover is { Payload: not "null" })
        {
            try
            {
                var state = JsonSerializer.Deserialize<TState>(recover.Payload, JsonOptions);
                if (state is not null)
                {
                    await Restore.InvokeAsync(state);
                    Status = "Draft recovered";
                }
            }
            catch (JsonException)
            {
                Warning = "This saved draft cannot be restored. It has been kept for recovery.";
                Conflict = true;
            }
        }
        LastPayload = Server?.Payload ?? "null";
        Ready = true;
        await ReadyChanged.InvokeAsync(true);
        await InvokeAsync(StateHasChanged);
    }

    private async Task SaveLaterAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(800, ct);
            await InvokeAsync(() => SavePendingAsync(ct));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SavePendingAsync(CancellationToken ct)
    {
        await SaveLock.WaitAsync(ct);
        try
        {
            if (Disposed || Conflict || PendingPayload is not { } payload)
            {
                return;
            }
            var saved = await Drafts.SaveAsync(Kind, new(Revision, payload, Guid.NewGuid()));
            Revision = saved.Revision;
            Server = saved;
            if (PendingPayload == payload)
            {
                PendingPayload = null;
                Status = payload == "null" ? "Ready for a new entry" : "Draft saved";
                if (Module is not null)
                {
                    await Module.InvokeVoidAsync("save", Key, saved);
                }
            }
            Warning = null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Warning = exception.Message;
            Status = "Local draft kept · server save failed";
            Conflict = true;
        }
        finally
        {
            SaveLock.Release();
        }
        if (!Disposed)
        {
            await InvokeAsync(StateHasChanged);
        }
    }

    public async Task RequestResetAsync()
    {
        if (!Ready || Paused || Resetting)
        {
            return;
        }
        if (!Empty || Conflict || LastPayload is not (null or "null"))
        {
            ConfirmReset = true;
        }
        else
        {
            await ResetConfirmedAsync();
        }
        await InvokeAsync(StateHasChanged);
    }

    private async Task ResetConfirmedAsync()
    {
        if (!Ready || Paused || Resetting)
        {
            return;
        }
        Resetting = true;
        Status = "Clearing saved draft…";
        Delay?.Cancel();
        await ReadyChanged.InvokeAsync(false);
        await SaveLock.WaitAsync();
        try
        {
            if (Conflict)
            {
                // Explicit discard confirmation also resolves recovery conflicts.
                Server = await Drafts.ReadAsync(Kind);
                Revision = Server?.Revision ?? 0;
            }
            var cleared = await Drafts.SaveAsync(Kind, new(Revision, "null", Guid.NewGuid()));
            Revision = cleared.Revision;
            Server = cleared;
            if (Module is not null)
            {
                await Module.InvokeVoidAsync("save", Key, cleared);
            }
            await Clear.InvokeAsync();
            LastPayload = "null";
            PendingPayload = null;
            Conflict = false;
            Warning = null;
            ConfirmReset = false;
            Status = "Ready for a new entry";
        }
        catch (Exception exception)
        {
            Warning = "Unable to finish clearing the saved draft. Your current entries are kept. " + exception.Message;
            Status = "Draft kept · reset failed";
            Conflict = true;
        }
        finally
        {
            SaveLock.Release();
            Resetting = false;
            await ReadyChanged.InvokeAsync(true);
        }
    }

    public async Task ClearAsync()
    {
        Delay?.Cancel();
        if (!Ready)
        {
            return;
        }
        await SaveLock.WaitAsync();
        try
        {
            if (Conflict)
            {
                try
                {
                    Server = await Drafts.ReadAsync(Kind);
                    Revision = Server?.Revision ?? 0;
                }
                catch
                {
                    // Server read failed; proceed to attempt clear with current revision
                }
            }
            EditorDraftDocument? cleared = null;
            try
            {
                cleared = await Drafts.SaveAsync(Kind, new(Revision, "null", Guid.NewGuid()));
            }
            catch (Exception)
            {
                try
                {
                    Server = await Drafts.ReadAsync(Kind);
                    Revision = Server?.Revision ?? 0;
                    cleared = await Drafts.SaveAsync(Kind, new(Revision, "null", Guid.NewGuid()));
                }
                catch (Exception exception)
                {
                    Warning = "Unable to finish clearing the saved draft. " + exception.Message;
                }
            }

            if (cleared is not null)
            {
                Revision = cleared.Revision;
                Server = cleared;
            }

            LastPayload = "null";
            PendingPayload = null;
            Conflict = false;
            Warning = null;
            Status = "Ready for a new entry";

            if (Module is not null)
            {
                try
                {
                    await Module.InvokeVoidAsync("save", Key,
                        cleared ?? new EditorDraftDocument(Revision, "null", DateTimeOffset.UtcNow, Guid.NewGuid()));
                }
                catch (JSException)
                {
                    Warning = "Unable to clear browser recovery; this transaction is already saved.";
                }
            }
        }
        finally
        {
            SaveLock.Release();
        }
    }

    private async Task KeepLocalAsync()
    {
        try
        {
            Server = await Drafts.ReadAsync(Kind);
            Revision = Server?.Revision ?? 0;
            if (Module is not null && Server is not null)
            {
                await Module.InvokeVoidAsync("save", Key + ":server-backup", Server);
            }
            Conflict = false;
            Warning = null;
            LastPayload = null;
        }
        catch (Exception exception)
        {
            Warning = exception.Message;
        }
        await InvokeAsync(StateHasChanged);
    }

    private async Task RestoreServerAsync()
    {
        try
        {
            // Review is explicit: keep the user's local payload available in a backup key.
            if (Module is not null && LastPayload is not null)
            {
                try
                {
                    await Module.InvokeVoidAsync("save", Key + ":recovery",
                        new EditorDraftDocument(Revision, LastPayload, DateTimeOffset.UtcNow, Guid.NewGuid()));
                }
                catch (JSException)
                {
                    Warning = "Unable to back up the local copy. It has not been replaced.";
                    return;
                }
            }
            Delay?.Cancel();
            Server = await Drafts.ReadAsync(Kind);
            Revision = Server?.Revision ?? 0;
            if (Server is { Payload: not "null" })
            {
                var state = JsonSerializer.Deserialize<TState>(Server.Payload, JsonOptions);
                if (state is not null)
                {
                    await Restore.InvokeAsync(state);
                }
            }
            else
            {
                await Clear.InvokeAsync();
            }
            Conflict = false;
            Warning = null;
            LastPayload = Server?.Payload ?? "null";
            PendingPayload = null;
            if (Module is not null)
            {
                try
                {
                    await Module.InvokeVoidAsync("save", Key, Server ?? new EditorDraftDocument(0, "null", DateTimeOffset.UtcNow, Guid.NewGuid()));
                }
                catch (JSException)
                {
                    Warning = "Unable to update browser recovery.";
                }
            }
            await InvokeAsync(StateHasChanged);
        
        }
        catch (Exception exception)
        {
            Warning = exception.Message;
            Conflict = true;
            await InvokeAsync(StateHasChanged);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Disposed = true;
        Delay?.Cancel();
        Delay?.Dispose();
        if (Module is not null)
        {
            try
            {
                await Module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }
}
