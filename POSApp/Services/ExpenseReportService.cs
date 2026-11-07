using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Client.Security;
using POSApp.Persistence;

namespace POSApp.Services;

public sealed class ExpenseReportService(IServiceScopeFactory scopes, IConfiguration configuration)
{
    public async Task<ExpenseReportDto> GetExpenseReportAsync(
        ClaimsPrincipal actor, string? period, DateOnly? start, DateOnly? end,
        int? locationId = null, int? categoryId = null, string? search = null, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);

        var zone = AccountingCalendar.Zone(configuration);
        var range = ReportService.ResolveRange(period, start, end, zone, DateTimeOffset.UtcNow);

        var query = db.Expenses.AsNoTracking()
            .Include(e => e.StockLocation)
            .Include(e => e.CategoryDefinition)
            .Where(e => e.OccurredAt >= range.FromUtc && e.OccurredAt < range.ToUtc);

        if (locationId.HasValue)
        {
            query = query.Where(e => e.StockLocationId == locationId.Value);
        }

        if (categoryId.HasValue)
        {
            query = query.Where(e => e.CategoryDefinitionId == categoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(e => e.Notes.ToLower().Contains(term) || (e.ReceiptReference != null && e.ReceiptReference.ToLower().Contains(term)));
        }

        var expenses = await query.OrderByDescending(e => e.OccurredAt).ToListAsync(ct);

        var userIds = expenses.Select(e => e.CreatedByUserId).Distinct().ToList();
        var userNames = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.UserName ?? "User", ct);

        var allLocations = await db.StockLocations.AsNoTracking().Where(l => !l.IsDeleted).OrderBy(l => l.Name).ToListAsync(ct);

        // Sales revenue per location for comparison in this period
        var salesByLocation = await db.Sales.AsNoTracking()
            .Where(s => !s.IsDeleted && s.Status == SaleStatus.Completed && s.SoldAt >= range.FromUtc && s.SoldAt < range.ToUtc)
            .GroupBy(s => s.StockLocationId)
            .Select(g => new { LocationId = g.Key, Total = g.Sum(s => s.TotalAmount) })
            .ToDictionaryAsync(g => g.LocationId, g => g.Total, ct);

        var totalExpenses = expenses.Sum(e => e.Amount);

        // Location summaries
        var locationSummaries = new List<LocationExpenseSummaryDto>();
        foreach (var loc in allLocations)
        {
            var locExpenses = expenses.Where(e => e.StockLocationId == loc.Id).ToList();
            var locTotal = locExpenses.Sum(e => e.Amount);
            var sharePct = totalExpenses > 0 ? Math.Round((locTotal / totalExpenses) * 100m, 2) : 0m;

            var topCat = locExpenses.GroupBy(e => e.CategoryDefinition?.Name ?? "Uncategorized")
                .OrderByDescending(g => g.Sum(e => e.Amount))
                .FirstOrDefault();

            var processingFees = locExpenses
                .Where(e => e.CategoryDefinition != null && e.CategoryDefinition.Name == "Payment Processing Fees")
                .Sum(e => e.Amount);

            var revenue = salesByLocation.GetValueOrDefault(loc.Id, 0m);
            var netImpact = revenue - locTotal;

            locationSummaries.Add(new LocationExpenseSummaryDto(
                loc.Id,
                loc.Name,
                locTotal,
                sharePct,
                topCat?.Key,
                topCat?.Sum(e => e.Amount) ?? 0m,
                processingFees,
                revenue,
                netImpact));
        }

        // Add corporate/unassigned location if any expenses exist without location
        var unassignedExpenses = expenses.Where(e => e.StockLocationId == null).ToList();
        if (unassignedExpenses.Count > 0)
        {
            var unassignedTotal = unassignedExpenses.Sum(e => e.Amount);
            var sharePct = totalExpenses > 0 ? Math.Round((unassignedTotal / totalExpenses) * 100m, 2) : 0m;
            var topCat = unassignedExpenses.GroupBy(e => e.CategoryDefinition?.Name ?? "Uncategorized")
                .OrderByDescending(g => g.Sum(e => e.Amount))
                .FirstOrDefault();

            locationSummaries.Add(new LocationExpenseSummaryDto(
                0,
                "Corporate / Unassigned",
                unassignedTotal,
                sharePct,
                topCat?.Key,
                topCat?.Sum(e => e.Amount) ?? 0m,
                0m,
                0m,
                -unassignedTotal));
        }

        // Category summaries
        var categorySummaries = expenses
            .GroupBy(e => new { Id = e.CategoryDefinitionId ?? 0, Name = e.CategoryDefinition?.Name ?? "Uncategorized" })
            .Select(g =>
            {
                var catTotal = g.Sum(e => e.Amount);
                var sharePct = totalExpenses > 0 ? Math.Round((catTotal / totalExpenses) * 100m, 2) : 0m;
                return new CategoryExpenseSummaryDto(g.Key.Id, g.Key.Name, catTotal, sharePct);
            })
            .OrderByDescending(c => c.TotalAmount)
            .ToList();

        // Top Stats
        var topLoc = locationSummaries.OrderByDescending(l => l.TotalExpense).FirstOrDefault();
        var topCategory = categorySummaries.FirstOrDefault();
        var processingFeesTotal = expenses
            .Where(e => e.CategoryDefinition != null && e.CategoryDefinition.Name == "Payment Processing Fees")
            .Sum(e => e.Amount);

        // Itemized rows
        var items = expenses.Select(e => new ExpenseReportItemDto(
            e.Id,
            e.OccurredAt,
            e.StockLocationId,
            e.StockLocation?.Name ?? "Corporate / General",
            e.CategoryDefinitionId ?? 0,
            e.CategoryDefinition?.Name ?? "Uncategorized",
            e.Amount,
            e.Method,
            e.ReceiptReference,
            e.Notes,
            (e.CreatedByUserId.HasValue && userNames.ContainsKey(e.CreatedByUserId.Value)) ? userNames[e.CreatedByUserId.Value] : "User")).ToList();

        return new ExpenseReportDto(
            range,
            totalExpenses,
            topLoc?.LocationName,
            topLoc?.TotalExpense ?? 0m,
            topLoc?.SharePercentage ?? 0m,
            topCategory?.CategoryName,
            topCategory?.TotalAmount ?? 0m,
            topCategory?.SharePercentage ?? 0m,
            processingFeesTotal,
            locationSummaries,
            categorySummaries,
            items);
    }

    public async Task<string> ExportCsvAsync(
        ClaimsPrincipal actor, string? period, DateOnly? start, DateOnly? end,
        int? locationId = null, int? categoryId = null, string? search = null, CancellationToken ct = default)
    {
        var data = await GetExpenseReportAsync(actor, period, start, end, locationId, categoryId, search, ct);
        var sb = new StringBuilder();
        sb.AppendLine("ID,Date,Store Location,Category,Amount,Payment Method,Receipt Reference,Notes,Created By");

        foreach (var item in data.Items)
        {
            var notesEscaped = $"\"{item.Notes.Replace("\"", "\"\"")}\"";
            var receiptEscaped = item.ReceiptReference != null ? $"\"{item.ReceiptReference.Replace("\"", "\"\"")}\"" : "";
            sb.AppendLine($"{item.Id},{item.OccurredAt:yyyy-MM-dd HH:mm},{item.LocationName},{item.CategoryName},{item.Amount:F2},{item.Method},{receiptEscaped},{notesEscaped},{item.CreatedByUserName}");
        }

        return sb.ToString();
    }
}
