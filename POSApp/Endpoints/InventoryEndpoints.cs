using Microsoft.AspNetCore.Antiforgery;

namespace POSApp.Endpoints;

public static class InventoryEndpoints
{
    public static RouteGroupBuilder MapInventoryEndpoints(this RouteGroupBuilder group)
    {
        group.RequireAuthorization(AppPolicies.ManageInventory);
        group.AddEndpointFilter<StoreRequestFilter>();

        group.MapGet("/antiforgery", GetAntiforgeryToken);
        group.MapGroup("/items").MapItemEndpoints();
        group.MapGroup("/suppliers").MapSupplierEndpoints();
        group.MapGroup("/locations").MapStockLocationEndpoints();
        group.MapReceivingEndpoints();
        group.MapGroup("/transfers").MapStockTransferEndpoints();
        group.MapGroup("/stock-takes").MapStockTakeEndpoints();
        group.MapGroup("/adjustments").MapStockAdjustmentEndpoints();

        return group;
    }

    public static AntiforgeryResponse GetAntiforgeryToken(HttpContext context, IAntiforgery antiforgery)
    {
        context.Response.Headers.CacheControl = "no-store";
        var token = antiforgery.GetAndStoreTokens(context).RequestToken!;

        return new AntiforgeryResponse(token);
    }
}
