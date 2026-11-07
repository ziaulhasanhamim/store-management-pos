using Microsoft.AspNetCore.Antiforgery;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Services;

namespace POSApp.Endpoints;

public static class PaymentClearingEndpoints
{
    public static RouteGroupBuilder MapPaymentClearingEndpoints(this RouteGroupBuilder group)
    {
        group.RequireAuthorization(AppPolicies.MakeSales);
        group.AddEndpointFilter<StoreRequestFilter>();
        group.MapGet("/antiforgery", GetToken);
        group.MapGet("/", GetPageDataAsync);
        group.MapGet("/unsettled", GetUnsettledSalesAsync);
        group.MapPost("/payout", RecordPayoutAsync).RequireAuthorization(AppPolicies.ManageUsers);
        return group;
    }

    public static AntiforgeryResponse GetToken(HttpContext context, IAntiforgery antiforgery)
    {
        context.Response.Headers.CacheControl = "no-store";
        return new(antiforgery.GetAndStoreTokens(context).RequestToken!);
    }

    public static Task<PaymentClearingPageData> GetPageDataAsync(
        int? locationId, PaymentMethod? method, HttpContext context, PaymentClearingService clearing)
    {
        return clearing.GetPageDataAsync(context.User, locationId, method, context.RequestAborted);
    }

    public static Task<IReadOnlyList<ClearingSaleItemDto>> GetUnsettledSalesAsync(
        int locationId, PaymentMethod method, HttpContext context, PaymentClearingService clearing)
    {
        return clearing.GetUnsettledSalesAsync(context.User, locationId, method, context.RequestAborted);
    }

    public static Task<int> RecordPayoutAsync(
        RecordPayoutInput input, HttpContext context, PaymentClearingService clearing)
    {
        return clearing.RecordPayoutAsync(context.User, input, context.RequestAborted);
    }
}
