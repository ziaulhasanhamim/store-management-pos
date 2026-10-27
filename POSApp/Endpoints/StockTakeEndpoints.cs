namespace POSApp.Endpoints;

public static class StockTakeEndpoints
{
    public static RouteGroupBuilder MapStockTakeEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/{id:int}", ReadAsync);
        group.MapGet("/{id:int}/items", SearchAsync);
        group.MapGet("/{id:int}/list", BrowseAsync);
        group.MapPost("", StartAsync);
        group.MapPost("/{id:int}/counts", SaveAsync);
        group.MapPost("/{id:int}/refresh", RefreshAsync);
        group.MapPost("/{id:int}/apply", ApplyAsync);
        return group;
    }

    public static Task<StockTakeList> BrowseAsync(int id, string? search, string? status, int? categoryId,
        string? sort, int? page, int? pageSize, HttpContext context, StockTakeService takes) =>
        takes.BrowseAsync(context.User, id, search, status, categoryId, sort, page ?? 1, pageSize ?? 30, context.RequestAborted);

    public static Task<IReadOnlyList<StockTakeLine>> SearchAsync(int id, string? search, HttpContext context, StockTakeService takes) =>
        takes.SearchAsync(context.User, id, search, context.RequestAborted);

    public static Task<int> StartAsync(StockTakeStartInput input, HttpContext context, StockTakeService takes) =>
        takes.StartAsync(context.User, input, context.RequestAborted);

    public static Task<StockTakePage> ReadAsync(int id, HttpContext context, StockTakeService takes) =>
        takes.ReadAsync(context.User, id, context.RequestAborted);

    public static Task<StockTakePage> SaveAsync(int id, StockTakeSave input, HttpContext context, StockTakeService takes) =>
        takes.SaveAsync(context.User, id, input, context.RequestAborted);

    public static Task<StockTakePage> RefreshAsync(int id, StockTakeRefresh input, HttpContext context, StockTakeService takes) =>
        takes.RefreshAsync(context.User, id, input, context.RequestAborted);

    public static Task<StockTakePage> ApplyAsync(int id, StockTakeApply input, HttpContext context, StockTakeService takes) =>
        takes.ApplyAsync(context.User, id, input, context.RequestAborted);
}
