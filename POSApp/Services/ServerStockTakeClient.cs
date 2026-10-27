using POSApp.Client.Services;

namespace POSApp.Services;

public sealed class ServerStockTakeClient : IStockTakeClient, IDisposable
{
    private readonly StockTakeService takes;
    private readonly AuthenticationStateProvider authentication;
    private Task<AuthenticationState>? state;

    public ServerStockTakeClient(StockTakeService takes, AuthenticationStateProvider authentication)
    {
        this.takes = takes;
        this.authentication = authentication;
        authentication.AuthenticationStateChanged += OnAuthenticationChanged;
    }

    private void OnAuthenticationChanged(Task<AuthenticationState> updated) => state = updated;

    private async Task<System.Security.Claims.ClaimsPrincipal> ActorAsync() =>
        (await (state ??= authentication.GetAuthenticationStateAsync())).User;

    public async Task<IReadOnlyList<StockTakeLine>> SearchAsync(int id, string search, CancellationToken ct = default) =>
        await takes.SearchAsync(await ActorAsync(), id, search, ct);

    public async Task<StockTakeList> BrowseAsync(int id, string? search, string? status, int? categoryId,
        string sort, int page, int pageSize, CancellationToken ct = default) =>
        await takes.BrowseAsync(await ActorAsync(), id, search, status, categoryId, sort, page, pageSize, ct);

    public async Task<StockTakePage> ReadAsync(int id) =>
        await takes.ReadAsync(await ActorAsync(), id);
    public async Task<StockTakePage> SaveAsync(int id, StockTakeSave input) =>
        await takes.SaveAsync(await ActorAsync(), id, input);
    public async Task<StockTakePage> RefreshAsync(int id, StockTakeRefresh input) =>
        await takes.RefreshAsync(await ActorAsync(), id, input);
    public async Task<StockTakePage> ApplyAsync(int id, StockTakeApply input) =>
        await takes.ApplyAsync(await ActorAsync(), id, input);

    public void Dispose() => authentication.AuthenticationStateChanged -= OnAuthenticationChanged;
}
