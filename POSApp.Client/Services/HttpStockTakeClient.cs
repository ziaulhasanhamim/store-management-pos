using POSApp.Client.DTOs;

namespace POSApp.Client.Services;

public sealed class HttpStockTakeClient(HttpReceivingClient http) : IStockTakeClient
{
    public Task<IReadOnlyList<StockTakeLine>> SearchAsync(int id, string search, CancellationToken ct = default) =>
        http.ReadAsync<IReadOnlyList<StockTakeLine>>($"api/inventory/stock-takes/{id}/items?search={Uri.EscapeDataString(search)}", ct);

    public Task<StockTakeList> BrowseAsync(int id, string? search, string? status, int? categoryId,
        string sort, int page, int pageSize, CancellationToken ct = default)
    {
        var path = $"api/inventory/stock-takes/{id}/list?search={Uri.EscapeDataString(search ?? "")}&status={Uri.EscapeDataString(status ?? "")}&sort={Uri.EscapeDataString(sort)}&page={page}&pageSize={pageSize}";
        if (categoryId is not null)
        {
            path += $"&categoryId={categoryId}";
        }
        return http.ReadAsync<StockTakeList>(path, ct);
    }

    public Task<StockTakePage> ReadAsync(int id) => http.ReadAsync<StockTakePage>($"api/inventory/stock-takes/{id}");
    public Task<StockTakePage> SaveAsync(int id, StockTakeSave input) =>
        http.WriteAsync<StockTakeSave, StockTakePage>($"api/inventory/stock-takes/{id}/counts", input);
    public Task<StockTakePage> RefreshAsync(int id, StockTakeRefresh input) =>
        http.WriteAsync<StockTakeRefresh, StockTakePage>($"api/inventory/stock-takes/{id}/refresh", input);
    public Task<StockTakePage> ApplyAsync(int id, StockTakeApply input) =>
        http.WriteAsync<StockTakeApply, StockTakePage>($"api/inventory/stock-takes/{id}/apply", input);
}
