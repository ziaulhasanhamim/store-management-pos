using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using POSApp.Client.DTOs;
using POSApp.Client.Services;

namespace POSApp.Services;

public sealed class ServerAttributeDefinitionClient : IAttributeDefinitionClient, IDisposable
{
    private readonly InventoryService inventory;
    private readonly AuthenticationStateProvider authentication;
    private Task<AuthenticationState>? state;

    public ServerAttributeDefinitionClient(
        InventoryService inventory,
        AuthenticationStateProvider authentication)
    {
        this.inventory = inventory;
        this.authentication = authentication;
        authentication.AuthenticationStateChanged += OnAuthenticationChanged;
    }

    private void OnAuthenticationChanged(Task<AuthenticationState> updated) => state = updated;

    private async Task<ClaimsPrincipal> ActorAsync() =>
        (await (state ??= authentication.GetAuthenticationStateAsync())).User;

    public async Task<IReadOnlyList<ProductAttributeDefinitionOption>> ReadAsync(CancellationToken ct = default)
    {
        return await inventory.AttributeDefinitionsAsync(await ActorAsync(), ct);
    }

    public async Task<int> SaveAsync(ProductAttributeDefinitionInput input, CancellationToken ct = default)
    {
        return await inventory.SaveAttributeDefinitionAsync(await ActorAsync(), input, ct);
    }

    public void Dispose() => authentication.AuthenticationStateChanged -= OnAuthenticationChanged;
}
