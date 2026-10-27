namespace POSApp.Endpoints;

public static class StockTransferEndpoints
{
    public static RouteGroupBuilder MapStockTransferEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("", CreateAsync);
        return group;
    }

    public static Task<TransferResult> CreateAsync(
        TransferCommand command, HttpContext context, StockTransferService transfers)
    {
        return transfers.TransferItemsAsync(context.User, command, context.RequestAborted);
    }
}
