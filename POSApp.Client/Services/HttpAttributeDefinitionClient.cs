using POSApp.Client.DTOs;

namespace POSApp.Client.Services;

public sealed class HttpAttributeDefinitionClient(HttpReceivingClient http) : IAttributeDefinitionClient
{
    public Task<IReadOnlyList<ProductAttributeDefinitionOption>> ReadAsync(CancellationToken ct = default) =>
        http.ReadAsync<IReadOnlyList<ProductAttributeDefinitionOption>>("api/inventory/items/attributes", ct);

    public Task<int> SaveAsync(ProductAttributeDefinitionInput input, CancellationToken ct = default) =>
        http.WriteAsync<ProductAttributeDefinitionInput, int>("api/inventory/items/attributes", input);
}
