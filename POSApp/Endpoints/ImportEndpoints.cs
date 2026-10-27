namespace POSApp.Endpoints;

public static class ImportEndpoints
{
    public static RouteGroupBuilder MapImportEndpoints(this RouteGroupBuilder group)
    {
        group.RequireAuthorization(AppPolicies.MakeSales);
        group.AddEndpointFilter<StoreRequestFilter>();
        group.MapGet("/{kind}", ReadAsync);
        group.MapPost("/{kind}", EnqueueAsync);
        return group;
    }

    public static Task<IReadOnlyList<ImportStatus>> ReadAsync(string kind, HttpContext context, ImportService imports)
    {
        context.Response.Headers.CacheControl = "no-store";
        return imports.ReadAsync(context.User, kind, context.RequestAborted);
    }

    public static async Task<IResult> EnqueueAsync(string kind, ImportInput input, HttpContext context, ImportService imports)
    {
        var job = await imports.EnqueueAsync(context.User, kind, input, context.RequestAborted);
        return Results.Accepted($"/api/imports/{kind}", job);
    }
}
