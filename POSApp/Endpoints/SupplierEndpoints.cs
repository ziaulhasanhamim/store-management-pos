namespace POSApp.Endpoints;

public static class SupplierEndpoints
{
    public static RouteGroupBuilder MapSupplierEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("", CreateAsync);

        return group;
    }

    public static Task<int> CreateAsync(
        SupplierInput input,
        HttpContext context,
        InventoryService inventory)
    {
        return inventory.AddSupplierAsync(
            context.User,
            input.Name,
            input.Phone,
            input.OpeningDue,
            context.RequestAborted);
    }
}
