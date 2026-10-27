using POSApp.Client.DTOs;

namespace POSApp.Client.Services;

public interface ISalesClient
{
    Task<IReadOnlyList<SalesProduct>> SearchAsync(int locationId, string? search, CancellationToken ct = default);
    Task<SalesOptions> OptionsAsync();
    Task<SalesResult> SaveAsync(SalesCommand command, bool complete);
    Task<SalesDraft?> DraftAsync(Guid id);
    Task<int> AddCustomerAsync(CustomerInput input);
}
