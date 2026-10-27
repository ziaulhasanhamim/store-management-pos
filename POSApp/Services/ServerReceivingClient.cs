using System.Security.Claims;
using POSApp.Client.DTOs;
using POSApp.Client.Services;

namespace POSApp.Services;

// Auto's server renderer calls the same authorized service without making HTTP requests to itself.
public sealed class ServerReceivingClient : IReceivingClient, IDisposable
{
    private readonly InventoryService inventory;
    private readonly AuthenticationStateProvider authentication;
    private readonly StockTransferService transfers;
    private Task<AuthenticationState>? currentState;

    public ServerReceivingClient(InventoryService inventory, AuthenticationStateProvider authentication, StockTransferService transfers)
    {
        this.inventory = inventory;
        this.authentication = authentication;
        this.transfers = transfers;
        authentication.AuthenticationStateChanged += OnAuthenticationStateChanged;
    }

    private void OnAuthenticationStateChanged(Task<AuthenticationState> state)
    {
        currentState = state;
    }

    private async Task<ClaimsPrincipal> ActorAsync()
    {
        var state = await (currentState ?? authentication.GetAuthenticationStateAsync());
        return state.User;
    }

    public void Dispose()
    {
        authentication.AuthenticationStateChanged -= OnAuthenticationStateChanged;
    }

    public async Task<IReadOnlyList<ItemRow>> SearchAsync(string search, CancellationToken cancellationToken = default, int? locationId = null)
    {
        return await inventory.ListAsync(await ActorAsync(), search, ct: cancellationToken, locationId: locationId, stockedOnly: true);
    }

    public async Task<TransferResult> TransferAsync(TransferCommand command)
    {
        return await transfers.TransferItemsAsync(await ActorAsync(), command);
    }

    public async Task<ReceivingOptions> OptionsAsync()
    {
        return await inventory.OptionsAsync(await ActorAsync());
    }

    public async Task<ItemRow> SaveProductAsync(ProductInput input)
    {
        var actor = await ActorAsync();
        var id = await inventory.SaveProductAsync(actor, null, input);
        return (await inventory.FindAsync(actor, id))!.Item;
    }

    public async Task<int> AddSupplierAsync(SupplierInput input)
    {
        return await inventory.AddSupplierAsync(await ActorAsync(), input.Name, input.Phone, input.OpeningDue);
    }

    public async Task<int> AddLocationAsync(LocationInput input)
    {
        return await inventory.AddLocationAsync(await ActorAsync(), input.Name);
    }

    public async Task<ReceiveResult> ReceiveAsync(ReceiveCommand command)
    {
        return await inventory.ReceiveAsync(await ActorAsync(), command);
    }
}
