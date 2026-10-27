using System.Security.Claims;
using POSApp.Client.Services;

namespace POSApp.Services;

public sealed class ServerStockAdjustmentClient : IStockAdjustmentClient, IDisposable
{
    private readonly StockAdjustmentService adjustment;
    private readonly AuthenticationStateProvider authentication;
    private Task<AuthenticationState>? state;

    public ServerStockAdjustmentClient(StockAdjustmentService adjustment, AuthenticationStateProvider authentication)
    {
        this.adjustment = adjustment;
        this.authentication = authentication;
        authentication.AuthenticationStateChanged += OnAuthenticationChanged;
    }

    private void OnAuthenticationChanged(Task<AuthenticationState> updated) => state = updated;

    private async Task<ClaimsPrincipal> ActorAsync() =>
        (await (state ??= authentication.GetAuthenticationStateAsync())).User;

    public async Task<IReadOnlyList<StockAdjustmentProduct>> SearchAsync(int locationId, string search, CancellationToken ct = default) =>
        await adjustment.SearchAsync(await ActorAsync(), locationId, search, ct);

    public async Task<PagedResult<StockAdjustmentHistory>> HistoryAsync(int page = 1, int pageSize = 30) =>
        await adjustment.HistoryPageAsync(await ActorAsync(), page, pageSize);

    public async Task<StockAdjustmentResult> CreateAsync(StockAdjustmentCommand input) =>
        await adjustment.CreateAsync(await ActorAsync(), input);

    public void Dispose() => authentication.AuthenticationStateChanged -= OnAuthenticationChanged;
}
