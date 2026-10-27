using POSApp.Client.DTOs;

namespace POSApp.Client.Services;

public sealed class HttpStockAdjustmentClient(HttpReceivingClient http) : IStockAdjustmentClient
{
    public Task<IReadOnlyList<StockAdjustmentProduct>> SearchAsync(int locationId, string search, CancellationToken ct = default) =>
        http.ReadAsync<IReadOnlyList<StockAdjustmentProduct>>(
            $"api/inventory/adjustments/items?locationId={locationId}&search={Uri.EscapeDataString(search)}", ct);

    public Task<PagedResult<StockAdjustmentHistory>> HistoryAsync(int page = 1, int pageSize = 30) =>
        http.ReadAsync<PagedResult<StockAdjustmentHistory>>($"api/inventory/adjustments/history?page={page}&pageSize={pageSize}");

    public Task<StockAdjustmentResult> CreateAsync(StockAdjustmentCommand input) =>
        http.WriteAsync<StockAdjustmentCommand, StockAdjustmentResult>("api/inventory/adjustments", input);
}
