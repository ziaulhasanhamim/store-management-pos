using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Services;

namespace POSApp.Endpoints;

public static class FinanceEndpoints
{
    public static RouteGroupBuilder MapFinanceEndpoints(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter<StoreRequestFilter>();
        group.MapGet("/closing.xlsx", ExportClosingExcelAsync).RequireAuthorization(AppPolicies.ManageUsers);
        group.MapGet("/cash-drawer/expected-at", GetHistoricalExpectedCashAsync).RequireAuthorization(AppPolicies.ManageDrawer);
        return group;
    }

    public static async Task<IResult> GetHistoricalExpectedCashAsync(
        int? locationId, string asOf, HttpContext context, CashDrawerService drawerService, IConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(asOf))
        {
            return Results.BadRequest(new { error = "asOf timestamp is required." });
        }

        DateTimeOffset targetTime;
        var zone = AccountingCalendar.Zone(config);

        if (DateTimeOffset.TryParse(asOf, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedOffset))
        {
            if (!asOf.Contains("+") && !asOf.EndsWith("Z", StringComparison.OrdinalIgnoreCase) && !(asOf.Length > 19 && (asOf[19] == '-' || asOf[19] == '+')))
            {
                var dt = DateTime.Parse(asOf, System.Globalization.CultureInfo.InvariantCulture);
                var offset = zone.GetUtcOffset(dt);
                targetTime = new DateTimeOffset(dt, offset);
            }
            else
            {
                targetTime = parsedOffset;
            }
        }
        else if (DateTime.TryParse(asOf, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedDt))
        {
            var offset = zone.GetUtcOffset(parsedDt);
            targetTime = new DateTimeOffset(parsedDt, offset);
        }
        else
        {
            return Results.BadRequest(new { error = "Invalid date/time format." });
        }

        var expected = await drawerService.GetExpectedAtAsync(context.User, locationId ?? 0, targetTime, context.RequestAborted);
        return Results.Ok(new { expectedCash = expected });
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
