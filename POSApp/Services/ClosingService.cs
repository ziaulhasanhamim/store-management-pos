using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Client.Security;
using POSApp.Persistence;
using POSApp.Persistence.Entities;

namespace POSApp.Services;

public sealed class ClosingService(IServiceScopeFactory scopes, IConfiguration configuration)
{
    public Task<DateOnly> SelectedAsync(ClaimsPrincipal actor, CancellationToken ct = default) =>
        Task.FromResult(AccountingCalendar.Today(configuration));

    public async Task<IReadOnlyList<StockLocationOption>> LocationsAsync(ClaimsPrincipal actor, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);
        return await db.StockLocations.AsNoTracking()
            .Where(l => !l.IsDeleted)
            .OrderBy(l => l.Name)
            .Select(l => new StockLocationOption(l.Id, l.Name))
            .ToArrayAsync(ct);
    }

    public async Task<DayOverview> OverviewAsync(ClaimsPrincipal actor, CancellationToken ct = default)
    {
        return await StoreOperation.RunAsync<DayOverview>(async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            var today = AccountingCalendar.Today(configuration);
            var from = Utc(today);
            var through = Utc(today.AddDays(1));
            var opening = await db.CashDrawers.Select(d => (decimal?)d.CurrentCash).SumAsync(ct) ?? 0m;
            var cash = await db.CashMovements.Where(move => move.CreatedAt >= from && move.CreatedAt < through && move.CashTransferId == null && move.ReversesMovementId == null
                    && !db.CashMovements.Any(reverse => reverse.ReversesMovementId == move.Id))
                .GroupBy(move => 1).Select(group => new
                {
                    In = group.Sum(move => move.Amount > 0 ? move.Amount : 0),
                    Out = -group.Sum(move => move.Amount < 0 ? move.Amount : 0)
                }).SingleOrDefaultAsync(ct);
            var cashIn = cash?.In ?? 0;
            var cashOut = cash?.Out ?? 0;
            return new(today, opening, cashIn, cashOut, opening + cashIn - cashOut, null, null);
        }, ct);
    }

    public Task SelectAsync(ClaimsPrincipal actor, DateOnly date, CancellationToken ct = default, decimal? openingCash = null)
    {
        // Continuous timeline: days are always continuous, no explicit day opening required.
        return Task.CompletedTask;
    }

    public Task DeleteWorkingDayAsync(ClaimsPrincipal actor, DateOnly date, CancellationToken ct = default)
    {
        // Continuous timeline: working day concept is removed.
        return Task.CompletedTask;
    }

    public async Task<ClosingTotals> ReadAsync(ClaimsPrincipal actor, DateOnly date, ClosingKind kind, CancellationToken ct = default, int? locationId = null)
    {
        return await StoreOperation.RunAsync<ClosingTotals>(async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);

            if (date == default || !Enum.IsDefined(kind))
            {
                date = AccountingCalendar.Today(configuration);
            }

            var availableLocations = await db.StockLocations.AsNoTracking()
                .Where(l => !l.IsDeleted)
                .OrderBy(l => l.Name)
                .Select(l => new StockLocationOption(l.Id, l.Name))
                .ToArrayAsync(ct);
            var selectedLocationName = availableLocations.FirstOrDefault(l => l.Id == locationId)?.Name;

            var normalized = Normalize(date, kind);
            var opening = await db.CashDrawers.Where(d => locationId == null || d.StockLocationId == locationId.Value)
                .Select(d => (decimal?)d.OpeningBalance).SumAsync(ct) ?? 0m;

            var totals = await BuildAsync(db, normalized, kind, opening, null, ct, locationId);
            var from = Utc(normalized);
            var through = Utc(End(normalized, kind).AddDays(1));

            var dayBreakdown = (kind == ClosingKind.Day && locationId == null)
                ? await MonthlyFinanceBuilder.BuildLocationBreakdownAsync(db, from, through, ct, normalized, normalized)
                : totals.Monthly?.LocationBreakdown ?? [];

            return totals with
            {
                SelectedLocationId = locationId,
                SelectedLocationName = selectedLocationName,
                AvailableLocations = availableLocations,
                LocationBreakdown = dayBreakdown,
                Locations = kind == ClosingKind.Day ? await LocationClosingsAsync(db, normalized, ct, locationId) : [],
                Daily = kind == ClosingKind.Day ? await DailyFinanceBuilder.BuildAsync(db, configuration, normalized, ct, locationId) : null,
                Monthly = kind == ClosingKind.Month ? await MonthlyFinanceBuilder.BuildAsync(db, configuration, totals, ct, locationId) : null,
                DayOpened = true,
                OpeningLocked = false,
                IsWorkingDay = true
            };
        }, ct);
    }

    public Task SaveAsync(ClaimsPrincipal actor, ClosingInput input, bool close, CancellationToken ct = default)
    {
        // Continuous timeline: manual day/month locking is eliminated.
        return Task.CompletedTask;
    }

    private async Task<ClosingTotals> BuildAsync(
        AppDbContext db, DateOnly start, ClosingKind kind, decimal openingCash, string? notes, CancellationToken ct, int? locationId = null)
    {
        var end = End(start, kind);
        var from = Utc(start);
        var through = Utc(end.AddDays(1));

        var cash = await db.CashMovements.Where(move => move.CreatedAt >= from && move.CreatedAt < through && move.CashTransferId == null
                && move.ReversesMovementId == null && !db.CashMovements.Any(reverse => reverse.ReversesMovementId == move.Id)
                && (locationId == null || move.CashSession!.StockLocationId == locationId.Value))
            .Select(move => new { move.Kind, move.Amount, IsCollection = move.Payment != null && move.Payment.SaleId == null }).ToListAsync(ct);

        var bank = locationId == null
            ? await db.BankMovements.Where(move => move.CreatedAt >= from && move.CreatedAt < through
                    && move.Kind != CashMovementKind.OpeningBalance && move.ReversesMovementId == null
                    && !db.BankMovements.Any(reverse => reverse.ReversesMovementId == move.Id))
                .Select(move => new { move.Kind, move.Amount, IsCollection = move.Payment != null && move.Payment.SaleId == null }).ToListAsync(ct)
            : [];

        var account = await db.BankAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.Code == "MAIN", ct);
        var bankOpening = (account?.OpeningDate <= end ? account.OpeningBalance : 0)
            + (await db.BankMovements.Where(move => move.CreatedAt < from && move.Kind != CashMovementKind.OpeningBalance)
                .Select(move => (decimal?)move.Amount).SumAsync(ct) ?? 0);

        var sales = db.Sales.Where(sale => !sale.IsDeleted && sale.Status == SaleStatus.Completed && sale.SoldAt >= from && sale.SoldAt < through
            && (locationId == null || sale.StockLocationId == locationId.Value));
        var revenue = await sales.Select(sale => (decimal?)sale.TotalAmount).SumAsync(ct) ?? 0;

        var costs = await db.SaleItems.Where(item => !item.Sale!.IsDeleted && item.Sale.Status == SaleStatus.Completed
                && item.Sale.SoldAt >= from && item.Sale.SoldAt < through
                && (locationId == null || item.Sale.StockLocationId == locationId.Value))
            .Select(item => new { item.UnitCost, item.Quantity }).ToListAsync(ct);
        var cogs = costs.Sum(item => decimal.Round(item.UnitCost * item.Quantity, 2, MidpointRounding.AwayFromZero));

        var expenses = await db.Expenses.Where(expense => !expense.IsVoided && expense.OccurredAt >= from && expense.OccurredAt < through
                && (locationId == null || expense.StockLocationId == locationId.Value))
            .Select(expense => (decimal?)expense.Amount).SumAsync(ct) ?? 0;

        var salaries = await db.PayrollEntries.Where(entry => !entry.IsVoided && entry.OccurredAt >= from && entry.OccurredAt < through)
            .Select(entry => (decimal?)entry.PaidAmount).SumAsync(ct) ?? 0;
        var advances = await db.PayrollEntries.Where(entry => !entry.IsVoided && entry.OccurredAt >= from && entry.OccurredAt < through && entry.AdvanceCreated > 0)
            .Select(entry => (decimal?)entry.AdvanceCreated).SumAsync(ct) ?? 0;

        var purchaseTotal = await db.Purchases.Where(purchase => !purchase.IsDeleted && purchase.ReceivedAt >= from && purchase.ReceivedAt < through
                && (locationId == null || purchase.StockLocationId == locationId.Value))
            .Select(purchase => (decimal?)purchase.TotalAmount).SumAsync(ct) ?? 0;

        var (supplierDue, customerDue) = await FinancePositionReader.DuesAtAsync(db, through, ct);
        var stockValue = await FinancePositionReader.StockValueAtAsync(db, through, ct, locationId);

        var variance = await db.CashSessions.Where(session => session.OpenedAt >= from && session.OpenedAt < through && session.ClosedAt != null
                && (locationId == null || session.StockLocationId == locationId.Value))
            .Select(session => session.Difference ?? 0).SumAsync(ct);

        var cashIn = cash.Where(move => move.Amount > 0).Sum(move => move.Amount);
        var cashOut = -cash.Where(move => move.Amount < 0).Sum(move => move.Amount);
        var bankIn = bank.Where(move => move.Amount > 0).Sum(move => move.Amount);
        var bankOut = -bank.Where(move => move.Amount < 0).Sum(move => move.Amount);

        var totals = new ClosingTotals(start, end, openingCash, bankOpening, cashIn, cashOut, bankIn, bankOut,
            openingCash + cashIn - cashOut, bankOpening + bankIn - bankOut, revenue, cogs, expenses, salaries,
            -(cash.Where(move => move.Kind == CashMovementKind.SupplierPayment).Sum(move => move.Amount)
                + bank.Where(move => move.Kind == CashMovementKind.SupplierPayment).Sum(move => move.Amount)),
            -(cash.Where(move => move.Kind == CashMovementKind.Payroll).Sum(move => move.Amount)
                + bank.Where(move => move.Kind == CashMovementKind.Payroll).Sum(move => move.Amount)),
            supplierDue, customerDue, stockValue, null, notes)
        {
            OwnerContributions = cash.Where(move => move.Kind == CashMovementKind.OwnerContribution).Sum(move => move.Amount)
                + bank.Where(move => move.Kind == CashMovementKind.OwnerContribution).Sum(move => move.Amount),
            OwnerWithdrawals = -(cash.Where(move => move.Kind == CashMovementKind.OwnerWithdrawal).Sum(move => move.Amount)
                + bank.Where(move => move.Kind == CashMovementKind.OwnerWithdrawal).Sum(move => move.Amount)),
            SalesReceipts = cash.Where(move => move.Kind == CashMovementKind.Sale && !move.IsCollection).Sum(move => move.Amount)
                + bank.Where(move => move.Kind == CashMovementKind.Sale && !move.IsCollection).Sum(move => move.Amount),
            CustomerCollections = cash.Where(move => move.IsCollection).Sum(move => move.Amount)
                + bank.Where(move => move.IsCollection).Sum(move => move.Amount),
            DebtBorrowed = cash.Where(move => move.Kind == CashMovementKind.DebtBorrowing).Sum(move => move.Amount)
                + bank.Where(move => move.Kind == CashMovementKind.DebtBorrowing).Sum(move => move.Amount),
            DebtRepaid = -(cash.Where(move => move.Kind == CashMovementKind.DebtRepayment).Sum(move => move.Amount)
                + bank.Where(move => move.Kind == CashMovementKind.DebtRepayment).Sum(move => move.Amount)),
            OutstandingDebt = await DebtService.OutstandingBeforeAsync(db, end.AddDays(1), ct),
            PurchaseTotal = purchaseTotal,
            AdvancesPaid = advances,
            CashVariance = variance,
            StockVariance = (await db.InventoryMovements.Where(move => (move.StockTakeItemId != null || move.StockAdjustmentItemId != null || move.Type == InventoryMovementType.Adjustment)
                    && (move.StockAdjustmentItem == null || !move.StockAdjustmentItem.StockAdjustment!.IsVoided)
                    && (move.StockTakeItem == null || !move.StockTakeItem.StockTake!.IsVoided)
                    && move.OccurredAt >= from && move.OccurredAt < through
                    && (locationId == null || move.StockLocationId == locationId.Value))
                .Select(move => new { move.Quantity, move.UnitCost }).ToArrayAsync(ct))
                .Sum(move => decimal.Round(move.Quantity * move.UnitCost, 2, MidpointRounding.AwayFromZero))
        };

        return totals;
    }

    private static async Task<IReadOnlyList<LocationCashClosing>> LocationClosingsAsync(
        AppDbContext db, DateOnly date, CancellationToken ct, int? locationId = null)
    {
        var drawers = await db.CashDrawers.Where(drawer => drawer.OpeningDate <= date
                && (locationId == null || drawer.StockLocationId == locationId.Value))
            .OrderBy(drawer => drawer.StockLocation!.Name)
            .Select(drawer => new { drawer.Id, Name = drawer.StockLocation == null ? "Legacy / unassigned" : drawer.StockLocation.Name, drawer.OpeningBalance, drawer.CurrentCash, drawer.CurrentSessionId })
            .ToArrayAsync(ct);

        var sessions = await db.CashSessions.Where(session => (locationId == null || session.StockLocationId == locationId.Value))
            .OrderByDescending(session => session.OpenedAt)
            .Select(session => new { session.Id, session.CashDrawerId, session.BusinessDate, session.OpenedAt, session.OpeningCash, session.ExpectedCash, session.ActualClosingCash, session.Difference, session.ClosedAt })
            .Take(100)
            .ToArrayAsync(ct);

        return drawers.Select(drawer =>
        {
            var session = sessions.Where(session => session.CashDrawerId == drawer.Id).FirstOrDefault();
            if (session != null && session.BusinessDate == date)
            {
                return new LocationCashClosing(drawer.Name, session.Id, session.ClosedAt == null ? "Open" : "Counted and closed",
                    session.OpeningCash, session.ExpectedCash, session.ActualClosingCash, session.Difference);
            }
            var held = drawer.CurrentCash;
            return new LocationCashClosing(drawer.Name, null, "Continuous register · active balance", held, held, held, 0);
        }).ToArray();
    }

    private DateTimeOffset Utc(DateOnly date)
    {
        var zone = AccountingCalendar.Zone(configuration);
        return new(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone), TimeSpan.Zero);
    }

    private static DateOnly Normalize(DateOnly date, ClosingKind kind) => kind == ClosingKind.Month ? new(date.Year, date.Month, 1) : date;
    private static DateOnly End(DateOnly date, ClosingKind kind) => kind == ClosingKind.Month ? date.AddMonths(1).AddDays(-1) : date;
}
