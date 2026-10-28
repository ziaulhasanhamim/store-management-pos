using JASIntStoreManagement.Client.DTOs;

namespace JASIntStoreManagement.Client.Services;

public interface IStockAdjustmentClient
{
    Task<IReadOnlyList<StockAdjustmentProduct>> SearchAsync(int locationId, string search, CancellationToken ct = default);
    Task<PagedResult<StockAdjustmentHistory>> HistoryAsync(int page = 1, int pageSize = 30);
    Task<StockAdjustmentResult> CreateAsync(StockAdjustmentCommand input);
}
