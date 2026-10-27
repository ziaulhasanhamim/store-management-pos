namespace POSApp.Endpoints;

public static class ItemEndpoints
{
    public static RouteGroupBuilder MapItemEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("", SearchAsync);
        group.MapPost("", CreateAsync);
        group.MapGet("/categories", CategoriesAsync);
        group.MapPost("/categories", SaveCategoryAsync);
        group.MapGet("/attributes", AttributesAsync);
        group.MapPost("/attributes", SaveAttributeAsync);

        return group;
    }

    public static Task<IReadOnlyList<ItemRow>> SearchAsync(
        string? search,
        int? locationId,
        HttpContext context,
        InventoryService inventory)
    {
        return inventory.ListAsync(context.User, search, ct: context.RequestAborted, locationId: locationId, stockedOnly: true);
    }

    public static async Task<ItemRow> CreateAsync(
        ProductInput input,
        HttpContext context,
        InventoryService inventory)
    {
        var id = await inventory.SaveProductAsync(context.User, null, input, context.RequestAborted);
        var item = await inventory.FindAsync(context.User, id, context.RequestAborted);

        return item!.Item;
    }

    public static Task<IReadOnlyList<Lookup>> CategoriesAsync(HttpContext context, InventoryService inventory) =>
        inventory.CategoriesAsync(context.User, context.RequestAborted);

    public static Task<int> SaveCategoryAsync(CategoryInput input, HttpContext context, InventoryService inventory) =>
        inventory.SaveCategoryAsync(context.User, input, context.RequestAborted);

    public static Task<IReadOnlyList<ProductAttributeDefinitionOption>> AttributesAsync(HttpContext context, InventoryService inventory) =>
        inventory.AttributeDefinitionsAsync(context.User, context.RequestAborted);

    public static Task<int> SaveAttributeAsync(ProductAttributeDefinitionInput input, HttpContext context, InventoryService inventory) =>
        inventory.SaveAttributeDefinitionAsync(context.User, input, context.RequestAborted);
}
