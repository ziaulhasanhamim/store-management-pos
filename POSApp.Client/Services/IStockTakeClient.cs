using POSApp.Client.DTOs;

namespace POSApp.Client.Services;

public interface IStockTakeClient
{
    Task<IReadOnlyList<StockTakeLine>> SearchAsync(int id, string search, CancellationToken ct = default);
    Task<StockTakeList> BrowseAsync(int id, string? search, string? status, int? categoryId, string sort, int page, int pageSize, CancellationToken ct = default);
    Task<StockTakePage> ReadAsync(int id);
    Task<StockTakePage> SaveAsync(int id, StockTakeSave input);
    Task<StockTakePage> RefreshAsync(int id, StockTakeRefresh input);
    Task<StockTakePage> ApplyAsync(int id, StockTakeApply input);
}
