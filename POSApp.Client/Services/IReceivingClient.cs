using POSApp.Client.DTOs;

namespace POSApp.Client.Services;

public interface IReceivingClient
{
    Task<IReadOnlyList<ItemRow>> SearchAsync(string search, CancellationToken cancellationToken = default, int? locationId = null);
    Task<ReceivingOptions> OptionsAsync();
    Task<ItemRow> SaveProductAsync(ProductInput input);
    Task<int> AddSupplierAsync(SupplierInput input);
    Task<int> AddLocationAsync(LocationInput input);
    Task<ReceiveResult> ReceiveAsync(ReceiveCommand command);
    Task<TransferResult> TransferAsync(TransferCommand command);
}
