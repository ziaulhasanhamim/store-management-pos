using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Client.Security;
using POSApp.Persistence;
using POSApp.Persistence.Entities;

namespace POSApp.Services;

public sealed class TradingCalendarService(IServiceScopeFactory scopes, IConfiguration configuration)
{
    public async Task<TradingCalendarPageData> GetTradingCalendarPageDataAsync(
        ClaimsPrincipal actor,
        int? year = null,
        CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);

        var today = AccountingCalendar.Today(configuration);
        var targetYear = year ?? today.Year;
        var startOfYear = new DateOnly(targetYear, 1, 1);
        var endOfYear = new DateOnly(targetYear, 12, 31);

        var closures = await db.NonTradingDays.AsNoTracking()
            .Include(d => d.StockLocation)
            .Include(d => d.MarkedBy)
            .Where(d => d.Date >= startOfYear && d.Date <= endOfYear)
            .OrderByDescending(d => d.Date)
            .Select(d => new NonTradingDayDto(
                d.Id,
                d.Date,
                d.StockLocationId,
                d.StockLocation != null ? d.StockLocation.Name : "All Locations (Store-wide)",
                d.Reason,
                d.MarkedBy != null ? (d.MarkedBy.UserName ?? d.MarkedBy.Email ?? "Admin") : "Admin",
                d.CreatedAt))
            .ToListAsync(ct);

        var locations = await db.StockLocations.AsNoTracking()
            .Where(l => !l.IsDeleted)
            .OrderBy(l => l.Name)
            .Select(l => new StockLocationOption(l.Id, l.Name))
            .ToListAsync(ct);

        return new TradingCalendarPageData(closures, locations);
    }

    public async Task<HashSet<DateOnly>> GetClosedDatesLookupAsync(
        DateOnly from,
        DateOnly through,
        int? locationId = null,
        CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var query = db.NonTradingDays.AsNoTracking()
            .Where(d => d.Date >= from && d.Date <= through);

        if (locationId.HasValue)
        {
            query = query.Where(d => d.StockLocationId == null || d.StockLocationId == locationId.Value);
        }

        var dates = await query.Select(d => d.Date).ToListAsync(ct);
        return dates.ToHashSet();
    }

    public async Task<NonTradingDayDto> MarkNonTradingDayAsync(
        ClaimsPrincipal actor,
        MarkNonTradingDayInput input,
        CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);

        var today = AccountingCalendar.Today(configuration);
        if (input.Date > today.AddYears(1))
        {
            throw new ValidationException("Cannot mark a non-trading day more than 1 year in advance.");
        }

        var exists = await db.NonTradingDays.AsNoTracking()
            .AnyAsync(d => d.Date == input.Date && d.StockLocationId == input.StockLocationId, ct);
        if (exists)
        {
            throw new ValidationException($"{input.Date:dd MMM yyyy} is already marked as a non-trading day.");
        }

        // Complete validation: check for any active (non-deleted) operations on this date
        var fromUtc = AccountingCalendar.At(input.Date, TimeOnly.MinValue, configuration);
        var toUtc = AccountingCalendar.At(input.Date.AddDays(1), TimeOnly.MinValue, configuration);

        var hasActiveSales = await db.Sales.AsNoTracking().AnyAsync(s =>
            !s.IsDeleted && s.Status == SaleStatus.Completed
            && s.SoldAt >= fromUtc && s.SoldAt < toUtc
            && (input.StockLocationId == null || s.StockLocationId == input.StockLocationId.Value), ct);
        if (hasActiveSales)
        {
            throw new ValidationException($"Cannot mark {input.Date:dd MMM yyyy} as a non-trading day because active sales exist on this date. Void or delete the sales first.");
        }

        var hasActivePurchases = await db.Purchases.AsNoTracking().AnyAsync(p =>
            !p.IsDeleted
            && ((p.ReceivedAt.HasValue && p.ReceivedAt >= fromUtc && p.ReceivedAt < toUtc) || (p.OrderedAt >= fromUtc && p.OrderedAt < toUtc))
            && (input.StockLocationId == null || p.StockLocationId == input.StockLocationId.Value), ct);
        if (hasActivePurchases)
        {
            throw new ValidationException($"Cannot mark {input.Date:dd MMM yyyy} as a non-trading day because active inventory receipts/purchases exist on this date. Void or delete them first.");
        }

        var hasActiveExpenses = await db.Expenses.AsNoTracking().AnyAsync(e =>
            !e.IsVoided
            && e.OccurredAt >= fromUtc && e.OccurredAt < toUtc
            && (input.StockLocationId == null || e.StockLocationId == input.StockLocationId.Value), ct);
        if (hasActiveExpenses)
        {
            throw new ValidationException($"Cannot mark {input.Date:dd MMM yyyy} as a non-trading day because active expenses exist on this date. Void or delete them first.");
        }

        var hasActiveCustomerPayments = await db.Payments.AsNoTracking().AnyAsync(pay =>
            !pay.IsVoided
            && pay.PaidAt >= fromUtc && pay.PaidAt < toUtc, ct);
        if (hasActiveCustomerPayments)
        {
            throw new ValidationException($"Cannot mark {input.Date:dd MMM yyyy} as a non-trading day because customer payments exist on this date.");
        }

        var hasActiveDebtPayments = await db.DebtEntries.AsNoTracking().AnyAsync(d =>
            !d.IsVoided
            && d.OccurredAt >= fromUtc && d.OccurredAt < toUtc
            && (input.StockLocationId == null || d.CashLocationId == input.StockLocationId.Value), ct);
        if (hasActiveDebtPayments)
        {
            throw new ValidationException($"Cannot mark {input.Date:dd MMM yyyy} as a non-trading day because active debt/loan entries exist on this date.");
        }

        var hasActiveSupplierPayments = await db.SupplierPayments.AsNoTracking().AnyAsync(sp =>
            !sp.IsVoided
            && sp.PaidAt >= fromUtc && sp.PaidAt < toUtc, ct);
        if (hasActiveSupplierPayments)
        {
            throw new ValidationException($"Cannot mark {input.Date:dd MMM yyyy} as a non-trading day because active supplier payments exist on this date.");
        }

        var hasCashMovements = await db.CashMovements.AsNoTracking().AnyAsync(c =>
            c.OccurredAt >= fromUtc && c.OccurredAt < toUtc && c.ReversesMovementId == null
            && (input.StockLocationId == null || (c.CashDrawer != null && c.CashDrawer.StockLocationId == input.StockLocationId.Value)), ct);
        if (hasCashMovements)
        {
            throw new ValidationException($"Cannot mark {input.Date:dd MMM yyyy} as a non-trading day because cash drawer movements exist on this date.");
        }

        var hasBankMovements = await db.BankMovements.AsNoTracking().AnyAsync(b =>
            b.OccurredAt >= fromUtc && b.OccurredAt < toUtc && b.ReversesMovementId == null, ct);
        if (hasBankMovements)
        {
            throw new ValidationException($"Cannot mark {input.Date:dd MMM yyyy} as a non-trading day because bank movements exist on this date.");
        }

        var hasStockAdjustments = await db.StockAdjustments.AsNoTracking().AnyAsync(sa =>
            !sa.IsVoided && sa.Date == input.Date
            && (input.StockLocationId == null || sa.StockLocationId == input.StockLocationId.Value), ct);
        if (hasStockAdjustments)
        {
            throw new ValidationException($"Cannot mark {input.Date:dd MMM yyyy} as a non-trading day because stock adjustments exist on this date.");
        }

        var entity = new NonTradingDay
        {
            Date = input.Date,
            StockLocationId = input.StockLocationId,
            Reason = string.IsNullOrWhiteSpace(input.Reason) ? "Store Closed" : input.Reason.Trim(),
            MarkedById = actorId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.NonTradingDays.Add(entity);
        await db.SaveChangesAsync(ct);

        var locationName = "All Locations (Store-wide)";
        if (entity.StockLocationId.HasValue)
        {
            var loc = await db.StockLocations.FindAsync([entity.StockLocationId.Value], ct);
            if (loc is not null) locationName = loc.Name;
        }

        var user = await db.Users.FindAsync([actorId], ct);
        var markedByName = user?.UserName ?? user?.Email ?? "Admin";

        return new NonTradingDayDto(
            entity.Id,
            entity.Date,
            entity.StockLocationId,
            locationName,
            entity.Reason,
            markedByName,
            entity.CreatedAt);
    }

    public async Task RemoveNonTradingDayAsync(
        ClaimsPrincipal actor,
        int id,
        CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);

        var closure = await db.NonTradingDays.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (closure is null)
        {
            throw new ValidationException("Closure record not found.");
        }

        db.NonTradingDays.Remove(closure);
        await db.SaveChangesAsync(ct);
    }
}
