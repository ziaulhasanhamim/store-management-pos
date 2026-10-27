namespace POSApp.Endpoints;

public static class DraftEndpoints
{
    public static RouteGroupBuilder MapDraftEndpoints(this RouteGroupBuilder group)
    {
        group.RequireAuthorization(AppPolicies.MakeSales);
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
        context.Response.Headers.CacheControl = "no-store";
        var draft = await drafts.ReadAsync(context.User, kind, context.RequestAborted);
        return draft is null ? Results.NoContent() : Results.Json(draft);
    }

    public static Task<EditorDraftDocument> SaveAsync(string kind, EditorDraftInput input, HttpContext context, DraftService drafts) =>
        drafts.SaveAsync(context.User, kind, input, context.RequestAborted);
}
