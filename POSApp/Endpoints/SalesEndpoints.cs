namespace POSApp.Endpoints;

public static class SalesEndpoints
{
    public static RouteGroupBuilder MapSalesEndpoints(this RouteGroupBuilder group)
    {
        group.RequireAuthorization(AppPolicies.MakeSales);
        group.AddEndpointFilter<StoreRequestFilter>();
        group.MapGet("/antiforgery", InventoryEndpoints.GetAntiforgeryToken);
        group.MapGet("/options", OptionsAsync);
        group.MapGet("/items", SearchAsync);
        group.MapPost("/drafts", SaveDraftAsync);
        group.MapGet("/drafts/{id:guid}", DraftAsync);
        group.MapDelete("/drafts/{id:guid}", DiscardDraftAsync);
        group.MapGet("/receipt/{number}", FindReceiptAsync);
        group.MapPost("/complete", CompleteAsync);
        group.MapPost("/customers", AddCustomerAsync);
        return group;
    }

    public static Task<SalesOptions> OptionsAsync(HttpContext context, SalesService sales)
    {
        return sales.OptionsAsync(context.User, context.RequestAborted);
    }

    public static Task<IReadOnlyList<SalesProduct>> SearchAsync(int locationId, string? search, HttpContext context, SalesService sales)
    {
        return sales.SearchAsync(context.User, locationId, search, context.RequestAborted);
    }

    public static Task<SalesResult> SaveDraftAsync(SalesCommand command, HttpContext context, SalesService sales)
    {
        return sales.SaveAsync(context.User, command, false, context.RequestAborted);
    }

    public static Task<SalesResult> CompleteAsync(SalesCommand command, HttpContext context, SalesService sales)
    {
        return sales.SaveAsync(context.User, command, true, context.RequestAborted);
    }

    public static async Task<IResult> DraftAsync(Guid id, HttpContext context, SalesService sales)
    {
        var draft = await sales.DraftAsync(context.User, id, context.RequestAborted);
        return draft is null ? Results.NotFound(new InventoryApiError("Draft not found.")) : Results.Ok(draft);
    }

    public static async Task<IResult> FindReceiptAsync(string number, HttpContext context, SalesService sales)
    {
        var bill = await sales.FindReceiptAsync(context.User, number, context.RequestAborted);
        return bill is null ? Results.NotFound(new InventoryApiError("Receipt not found.")) : Results.Ok(bill);
    }

    public static async Task<IResult> DiscardDraftAsync(Guid id, HttpContext context, SalesService sales)
    {
        await sales.DiscardDraftAsync(context.User, id, context.RequestAborted);
        return Results.NoContent();
    }

    public static Task<int> AddCustomerAsync(CustomerInput input, HttpContext context, SalesService sales)
    {
        return sales.AddCustomerAsync(context.User, input, context.RequestAborted);
    }
}
