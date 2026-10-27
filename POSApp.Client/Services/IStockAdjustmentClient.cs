using POSApp.Client.DTOs;

namespace POSApp.Client.Services;

public interface IStockAdjustmentClient
{
    Task<IReadOnlyList<StockAdjustmentProduct>> SearchAsync(int locationId, string search, CancellationToken ct = default);
    Task<PagedResult<StockAdjustmentHistory>> HistoryAsync(int page = 1, int pageSize = 30);
    Task<StockAdjustmentResult> CreateAsync(StockAdjustmentCommand input);
}
