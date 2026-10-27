using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace POSApp.Client.Components;

public partial class LocalDraft<TState>
{
    [Parameter, EditorRequired]
    public string Kind { get; set; } = "";

    [Parameter]
    public string? Owner { get; set; }

    [Parameter, EditorRequired]
    public TState State { get; set; } = default!;

    [Parameter]
    public bool Empty { get; set; }

    [Parameter]
    public bool Paused { get; set; }

    [Parameter]
    public EventCallback<TState> Restore { get; set; }

    [Parameter]
    public EventCallback Clear { get; set; }

    [Parameter]
    public EventCallback<bool> ReadyChanged { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private IJSObjectReference? Module;
    private string Key = "";
    private string? LastPayload;
    private bool Ready;
    private bool Recovered;
    private bool Disposed;
    private CancellationTokenSource? SaveDebounce;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!RendererInfo.IsInteractive || Disposed)
        {
            return;
        }

        if (firstRender)
        {
            await InitializeAsync();
            return;
        }

        if (!Ready || Paused)
        {
            return;
        }

        var payload = Empty ? null : JsonSerializer.Serialize(State, JsonOptions);
        if (payload == LastPayload)
        {
            return;
        }
        LastPayload = payload;

        SaveDebounce?.Cancel();
        SaveDebounce?.Dispose();
        SaveDebounce = new CancellationTokenSource();
        _ = DebouncedSaveAsync(payload, SaveDebounce.Token);
    }

    private async Task InitializeAsync()
    {
        try
        {
            Key = string.IsNullOrWhiteSpace(Owner) ? $"pos:draft:{Kind}" : $"pos:draft:{Owner}:{Kind}";
            Module = await JS.InvokeAsync<IJSObjectReference>("import", "./js/local-draft.js");
            var stored = await Module.InvokeAsync<string?>("get", Key);
            if (!string.IsNullOrWhiteSpace(stored) && stored != "null")
            {
                try
                {
                    var state = JsonSerializer.Deserialize<TState>(stored, JsonOptions);
                    if (state is not null)
                    {
                        LastPayload = stored;
                        await Restore.InvokeAsync(state);
                        Recovered = true;
                    }
                }
                catch (JsonException)
                {
                    if (Module is not null)
                    {
                        await Module.InvokeVoidAsync("remove", Key);
                    }
                }
            }
        }
        catch (Exception)
        {
            // LocalStorage might be restricted or unavailable; continue smoothly without blocking
        }
        finally
        {
            Ready = true;
            await ReadyChanged.InvokeAsync(true);
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task DebouncedSaveAsync(string? payload, CancellationToken ct)
    {
        try
        {
            await Task.Delay(300, ct);
            if (Disposed || Module is null || string.IsNullOrWhiteSpace(Key))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(payload))
            {
                await Module.InvokeVoidAsync("remove", Key);
            }
            else
            {
                await Module.InvokeVoidAsync("set", Key, payload);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
        }
    }

    public async Task ClearAsync()
    {
        SaveDebounce?.Cancel();
        LastPayload = null;
        Recovered = false;
        if (Module is not null && !string.IsNullOrWhiteSpace(Key))
        {
            try
            {
                await Module.InvokeVoidAsync("remove", Key);
            }
            catch
            {
            }
        }
        await InvokeAsync(StateHasChanged);
    }

    public async Task ResetAsync()
    {
        await ClearAsync();
        await Clear.InvokeAsync();
    }

    public Task RequestResetAsync() => ResetAsync();

    private async Task DiscardAsync()
    {
        await ClearAsync();
        await Clear.InvokeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        Disposed = true;
        SaveDebounce?.Cancel();
        SaveDebounce?.Dispose();
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
