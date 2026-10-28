using JASIntStoreManagement.Client.DTOs;

namespace JASIntStoreManagement.Client.Services;

public interface IAttributeDefinitionClient
{
    Task<IReadOnlyList<ProductAttributeDefinitionOption>> ReadAsync(CancellationToken ct = default);

    Task<int> SaveAsync(ProductAttributeDefinitionInput input, CancellationToken ct = default);
}
