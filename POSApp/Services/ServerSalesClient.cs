using System.Security.Claims;
using POSApp.Client.Services;

namespace POSApp.Services;

public sealed class ServerSalesClient : ISalesClient, IDisposable
{
    private readonly SalesService sales;
    private readonly AuthenticationStateProvider authentication;
    private Task<AuthenticationState>? currentState;

    public ServerSalesClient(SalesService sales, AuthenticationStateProvider authentication)
    {
        this.sales = sales;
        this.authentication = authentication;
        authentication.AuthenticationStateChanged += StateChanged;
    }

    private void StateChanged(Task<AuthenticationState> state)
    {
        currentState = state;
    }

    public void Dispose()
    {
        authentication.AuthenticationStateChanged -= StateChanged;
    }

    private async Task<ClaimsPrincipal> ActorAsync()
    {
        return (await (currentState ?? authentication.GetAuthenticationStateAsync())).User;
    }

    public async Task<IReadOnlyList<SalesProduct>> SearchAsync(int locationId, string? search, CancellationToken ct = default)
    {
        return await sales.SearchAsync(await ActorAsync(), locationId, search, ct);
    }

    public async Task<SalesOptions> OptionsAsync()
    {
        return await sales.OptionsAsync(await ActorAsync());
    }

    public async Task<SalesResult> SaveAsync(SalesCommand command, bool complete)
    {
        return await sales.SaveAsync(await ActorAsync(), command, complete);
    }

    public async Task<SalesDraft?> DraftAsync(Guid id)
    {
        return await sales.DraftAsync(await ActorAsync(), id);
    }

    public async Task<int> AddCustomerAsync(CustomerInput input)
    {
        return await sales.AddCustomerAsync(await ActorAsync(), input);
    }
}
