using POSApp.Client.DTOs;

namespace POSApp.Client.Services;

public interface IAttributeDefinitionClient
{
    Task<IReadOnlyList<ProductAttributeDefinitionOption>> ReadAsync(CancellationToken ct = default);

    Task<int> SaveAsync(ProductAttributeDefinitionInput input, CancellationToken ct = default);
}
