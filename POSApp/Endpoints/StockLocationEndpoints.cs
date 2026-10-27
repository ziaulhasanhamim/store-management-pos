namespace POSApp.Endpoints;

public static class StockLocationEndpoints
{
    public static RouteGroupBuilder MapStockLocationEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("", CreateAsync);

        return group;
    }

    public static Task<int> CreateAsync(
        LocationInput input,
        HttpContext context,
        InventoryService inventory)
    {
        return inventory.AddLocationAsync(context.User, input.Name, context.RequestAborted, input.CanSell);
    }
}
