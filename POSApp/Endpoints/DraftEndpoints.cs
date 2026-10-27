namespace POSApp.Endpoints;

public static class DraftEndpoints
{
    public static RouteGroupBuilder MapDraftEndpoints(this RouteGroupBuilder group)
    {
        group.RequireAuthorization(AppPolicies.ManageInventory);
        group.AddEndpointFilter<StoreRequestFilter>();
        group.MapGet("/identity", GetIdentity);
        group.MapGet("/{kind}", ReadAsync);
        group.MapPost("/{kind}", SaveAsync);
        return group;
    }

    public static IResult GetIdentity(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        return Results.Json(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "");
    }

    public static async Task<IResult> ReadAsync(string kind, HttpContext context, DraftService drafts)
    {
        if (!kind.StartsWith("stock-take", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new InventoryApiError("Server drafts are only supported for stock takes."));
        }
        context.Response.Headers.CacheControl = "no-store";
        var draft = await drafts.ReadAsync(context.User, kind, context.RequestAborted);
        return draft is null ? Results.NoContent() : Results.Json(draft);
    }

    public static async Task<IResult> SaveAsync(string kind, EditorDraftInput input, HttpContext context, DraftService drafts)
    {
        if (!kind.StartsWith("stock-take", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new InventoryApiError("Server drafts are only supported for stock takes."));
        }
        var saved = await drafts.SaveAsync(context.User, kind, input, context.RequestAborted);
        return Results.Ok(saved);
    }
}
