using Microsoft.AspNetCore.Antiforgery;

namespace POSApp.Endpoints;

public static class MoneyEntryEndpoints
{
    public static RouteGroupBuilder MapMoneyEntryEndpoints(this RouteGroupBuilder group)
    {
        group.RequireAuthorization(AppPolicies.MakeSales);
        group.AddEndpointFilter<StoreRequestFilter>();
        group.MapGet("/antiforgery", GetToken);
        group.MapGet("/{kind}", ReadAsync);
        group.MapPost("/{kind}", SaveAsync);
        group.MapPost("/categories", SaveCategoryAsync).RequireAuthorization(AppPolicies.ManageUsers);
        return group;
    }

    public static AntiforgeryResponse GetToken(HttpContext context, IAntiforgery antiforgery)
    {
        context.Response.Headers.CacheControl = "no-store";
        return new(antiforgery.GetAndStoreTokens(context).RequestToken!);
    }

    public static Task<MoneyEntryPage> ReadAsync(string kind, DateOnly? date, HttpContext context, MoneyEntryService entries, int page = 1, int pageSize = 30)
    {
        return entries.ReadAsync(context.User, kind, date, context.RequestAborted, page, pageSize);
    }

    public static Task<MoneyEntryResult> SaveAsync(string kind, MoneyEntryCommand input, HttpContext context, MoneyEntryService entries)
    {
        return entries.SaveAsync(context.User, kind, input, context.RequestAborted);
    }

    public static Task<int> SaveCategoryAsync(ExpenseCategoryInput input, HttpContext context, MoneyEntryService entries)
    {
        return entries.SaveCategoryAsync(context.User, input, context.RequestAborted);
    }
}
