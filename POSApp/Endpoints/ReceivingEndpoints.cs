namespace POSApp.Endpoints;

public static class ReceivingEndpoints
{
    public static RouteGroupBuilder MapReceivingEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/options", GetOptionsAsync);
        group.MapPost("/receipts", ReceiveAsync);

        return group;
    }

    public static Task<ReceivingOptions> GetOptionsAsync(HttpContext context, InventoryService inventory)
    {
        return inventory.OptionsAsync(context.User, context.RequestAborted);
    }

    public static Task<ReceiveResult> ReceiveAsync(
        ReceiveCommand command,
        HttpContext context,
        InventoryService inventory)
    {
        return inventory.ReceiveAsync(context.User, command, context.RequestAborted);
    }
}
