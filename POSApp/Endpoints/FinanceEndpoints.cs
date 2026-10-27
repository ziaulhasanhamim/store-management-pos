using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Services;

namespace POSApp.Endpoints;

public static class FinanceEndpoints
{
    public static RouteGroupBuilder MapFinanceEndpoints(this RouteGroupBuilder group)
    {
        group.RequireAuthorization(AppPolicies.ManageUsers);
        group.AddEndpointFilter<StoreRequestFilter>();
        group.MapGet("/closing.xlsx", ExportClosingExcelAsync);
        return group;
    }

    public static async Task<IResult> ExportClosingExcelAsync(
        DateOnly date, string? period, int? locationId, HttpContext context, ClosingService closings, IConfiguration configuration)
    {
        var kind = period == "month" ? ClosingKind.Month : ClosingKind.Day;
        var data = await closings.ReadAsync(context.User, date, kind, context.RequestAborted, locationId);
        var currency = configuration["CurrencyCode"] ?? "BDT";
        var storeName = configuration["StoreName"] ?? "RetailPOS";
        var bytes = kind == ClosingKind.Month
            ? FinanceExportService.GenerateMonthExcel(data, currency, storeName)
            : FinanceExportService.GenerateDayExcel(data, currency, storeName);
        var locationSuffix = data.SelectedLocationName != null
            ? $"_{NameNormalization.Normalize(data.SelectedLocationName).Replace(' ', '_')}"
            : "";
        var storePrefix = storeName.Replace(" ", "");
        var filename = $"{storePrefix}_Finance_{kind}_{date:yyyy-MM-dd}{locationSuffix}.xlsx";
        return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", filename);
    }
}
