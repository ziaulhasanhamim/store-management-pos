namespace POSApp.Endpoints;

public static class StockAdjustmentEndpoints
{
    public static RouteGroupBuilder MapStockAdjustmentEndpoints(this RouteGroupBuilder group)
    {
        group.RequireAuthorization(AppPolicies.ManageStockAdjustment);
        group.MapGet("/items", SearchAsync);
        group.MapGet("/history", HistoryAsync);
        group.MapPost("", CreateAsync);
        return group;
    }

    public static Task<IReadOnlyList<StockAdjustmentProduct>> SearchAsync(
        int locationId, string? search, HttpContext context, StockAdjustmentService adjustment) =>
        adjustment.SearchAsync(context.User, locationId, search, context.RequestAborted);

    public static Task<PagedResult<StockAdjustmentHistory>> HistoryAsync(
        HttpContext context, StockAdjustmentService adjustment, int page = 1, int pageSize = 30) =>
        adjustment.HistoryPageAsync(context.User, page, pageSize, context.RequestAborted);

    public static Task<StockAdjustmentResult> CreateAsync(
        StockAdjustmentCommand input, HttpContext context, StockAdjustmentService adjustment) =>
        adjustment.CreateAsync(context.User, input, context.RequestAborted);
}
