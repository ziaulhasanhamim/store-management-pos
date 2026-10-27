using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;

namespace POSApp.Services;

public sealed class ClosingService(IServiceScopeFactory scopes, IConfiguration configuration)
{
    public async Task<DateOnly> SelectedAsync(ClaimsPrincipal actor, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);
        return await AccountingCalendar.SelectedAsync(db, id, configuration, ct);
    }

    public async Task<IReadOnlyList<StockLocationOption>> LocationsAsync(ClaimsPrincipal actor, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
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
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var date = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
            var day = await db.DayFinances.AsNoTracking().SingleOrDefaultAsync(day => day.Date == date, ct);
            var opening = day?.OpeningCash ?? await db.DayFinances.Where(day => day.Date < date && day.ClosedAt != null)
                .OrderByDescending(day => day.Date).Select(day => day.CountedCash ?? (decimal?)day.ExpectedCash).FirstOrDefaultAsync(ct) ?? 0;
            var cash = await db.CashMovements.Where(move => move.BusinessDate == date && move.CashTransferId == null && move.ReversesMovementId == null
                    && !db.CashMovements.Any(reverse => reverse.ReversesMovementId == move.Id))
                .GroupBy(move => 1).Select(group => new
                {
                    In = group.Sum(move => move.Amount > 0 ? move.Amount : 0),
                    Out = -group.Sum(move => move.Amount < 0 ? move.Amount : 0)
                }).SingleOrDefaultAsync(ct);
            return new(date, opening, cash?.In ?? 0, cash?.Out ?? 0,
                day?.ClosedAt is not null ? day.ExpectedCash : opening + (cash?.In ?? 0) - (cash?.Out ?? 0),
                day?.CountedCash, day?.ClosedAt);
        }, ct);
    }

    public async Task SelectAsync(ClaimsPrincipal actor, DateOnly date, CancellationToken ct = default, decimal? openingCash = null)
    {
        await StoreOperation.RunAsync(async () =>
        {
            if (date == default || date > AccountingCalendar.Today(configuration))
            {
                throw new ValidationException("Choose today or an earlier accounting date.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await AccountingCalendar.EnsureOpenAsync(db, date, ct);
            var selection = await db.WorkingDays.SingleOrDefaultAsync(day => day.Id == 1, ct);
            if (selection is not null && selection.Date != date)
            {
                var current = await db.DayFinances.SingleOrDefaultAsync(day => day.Date == selection.Date, ct);
                if (current is { ClosedAt: null })
                {
                    throw new ValidationException("Close the current working day before opening another day.");
                }
                if (date <= selection.Date)
                {
                    throw new ValidationException("Open a day after the previous working day.");
                }
            }
            if (!await db.DayFinances.AnyAsync(day => day.Date == date, ct))
            {
                if (openingCash is { } amount)
                {
                    StoreAccess.Money(amount, "Opening cash");
                }
                if (openingCash is > 0)
                {
                    throw new ValidationException("Set initial cash on each location's drawer. Business opening cash is calculated automatically.");
                }
                var opening = await db.CashDrawers.Select(drawer => (decimal?)drawer.CurrentCash).SumAsync(ct) ?? 0;
                db.DayFinances.Add(new DayFinance
                {
                    Date = date,
                    OpeningCash = opening,
                    ExpectedCash = opening,
                    CreatedByUserId = actorId
                });
            }
            if (selection is null)
            {
                db.WorkingDays.Add(new WorkingDay { Id = 1, Date = date });
            }
            else
            {
                selection.Date = date;
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }, ct);
    }

    public async Task<ClosingTotals> ReadAsync(ClaimsPrincipal actor, DateOnly date, ClosingKind kind, CancellationToken ct = default, int? locationId = null)
    {
        return await StoreOperation.RunAsync<ClosingTotals>(async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            if (date == default || date > AccountingCalendar.Today(configuration) || !Enum.IsDefined(kind))
            {
                throw new ValidationException("Choose today or an earlier accounting date.");
            }
            var availableLocations = await db.StockLocations.AsNoTracking()
                .Where(l => !l.IsDeleted)
                .OrderBy(l => l.Name)
                .Select(l => new StockLocationOption(l.Id, l.Name))
                .ToArrayAsync(ct);
            var selectedLocationName = availableLocations.FirstOrDefault(l => l.Id == locationId)?.Name;

            var normalized = Normalize(date, kind);
            var saved = await db.FinanceClosings.AsNoTracking().SingleOrDefaultAsync(closing => closing.Date == normalized && closing.Kind == kind, ct);
            if (saved?.ClosedAt is not null)
            {
                var closedDay = kind == ClosingKind.Day
                    ? await db.DayFinances.AsNoTracking().SingleOrDefaultAsync(day => day.Date == normalized, ct)
                    : null;
                var snapshot = Snapshot(saved);
                if (kind == ClosingKind.Month)
                {
                    var details = saved.MonthlyDetailsJson is null || locationId != null
                        ? await MonthlyFinanceBuilder.BuildAsync(db, configuration, snapshot, ct, locationId)
                        : JsonSerializer.Deserialize<MonthlyFinanceDetails>(saved.MonthlyDetailsJson)!;

                    if (saved.MonthlyDetailsJson is null
                        || details.AssetCalculationVersion < MonthlyFinanceBuilder.AssetCalculationVersion)
                    {
                        details = await MonthlyFinanceBuilder.CorrectLegacyAssetsAsync(db, configuration, snapshot, details, ct);
                    }

                    snapshot = snapshot with
                    {
                        Revenue = locationId != null ? details.TotalSales : snapshot.Revenue,
                        Cogs = locationId != null ? details.Days.Sum(d => d.Cogs) : snapshot.Cogs,
                        StockValue = details.ClosingAssets.Stock,
                        SupplierDue = details.ClosingAssets.SupplierDue,
                        CustomerDue = details.ClosingAssets.CustomerDue,
                        Monthly = details
                    };
                }
                else if (kind == ClosingKind.Day && locationId != null)
                {
                    var locTotals = await BuildAsync(db, normalized, kind, saved.OpeningCash, saved.Notes, ct, locationId);
                    snapshot = locTotals with
                    {
                        ClosedAt = saved.ClosedAt,
                        Notes = saved.Notes
                    };
                }

                var dayBreakdown = (kind == ClosingKind.Day && locationId == null)
                    ? await MonthlyFinanceBuilder.BuildLocationBreakdownAsync(db, Utc(normalized), Utc(normalized.AddDays(1)), ct)
                    : snapshot.Monthly?.LocationBreakdown ?? [];

                return snapshot with
                {
                    SelectedLocationId = locationId,
                    SelectedLocationName = selectedLocationName,
                    AvailableLocations = availableLocations,
                    LocationBreakdown = dayBreakdown,
                    Locations = kind == ClosingKind.Day ? await LocationClosingsAsync(db, normalized, ct, locationId) : [],
                    Daily = kind == ClosingKind.Day ? await DailyFinanceBuilder.BuildAsync(db, configuration, normalized, ct, locationId) : null,
                    CountedCash = closedDay?.CountedCash,
                    Difference = closedDay?.Difference,
                    DayOpened = closedDay is not null,
                    IsWorkingDay = normalized == await AccountingCalendar.SelectedAsync(db, 0, configuration, ct)
                };
            }
            var dayFinance = kind == ClosingKind.Day
                ? await db.DayFinances.AsNoTracking().SingleOrDefaultAsync(day => day.Date == normalized, ct)
                : null;
            var opening = dayFinance?.OpeningCash ?? saved?.OpeningCash;
            if (opening is null && kind == ClosingKind.Month)
            {
                opening = await db.DayFinances.Where(day => day.Date >= normalized && day.Date <= End(normalized, kind))
                    .OrderBy(day => day.Date).Select(day => (decimal?)day.OpeningCash).FirstOrDefaultAsync(ct);
                opening ??= await db.FinanceClosings.Where(closing => closing.Kind == ClosingKind.Day
                    && closing.Date >= normalized && closing.Date <= End(normalized, kind))
                    .OrderBy(closing => closing.Date).Select(closing => (decimal?)closing.OpeningCash).FirstOrDefaultAsync(ct);
            }
            if (opening is null)
            {
                opening = await db.DayFinances.Where(day => day.ClosedAt != null && day.Date < normalized)
                    .OrderByDescending(day => day.Date).Select(day => day.CountedCash ?? (decimal?)day.ExpectedCash).FirstOrDefaultAsync(ct);
                opening ??= await db.FinanceClosings.Where(closing => closing.ClosedAt != null && closing.Date < normalized)
                    .OrderByDescending(closing => closing.Date).Select(closing => (decimal?)closing.ClosingCash).FirstOrDefaultAsync(ct) ?? 0;
            }
            var totals = await BuildAsync(db, normalized, kind, opening.Value, saved?.Notes, ct, locationId);
            return totals with
            {
                SelectedLocationId = locationId,
                SelectedLocationName = selectedLocationName,
                AvailableLocations = availableLocations,
                Locations = kind == ClosingKind.Day ? await LocationClosingsAsync(db, normalized, ct, locationId) : [],
                Daily = kind == ClosingKind.Day ? await DailyFinanceBuilder.BuildAsync(db, configuration, normalized, ct, locationId) : null,
                DayOpened = dayFinance is not null,
                OpeningLocked = await db.DayFinances.AnyAsync(day => kind == ClosingKind.Day
                    ? day.Date < normalized && day.ClosedAt != null
                    : day.Date >= normalized && day.Date <= End(normalized, kind), ct),
                IsWorkingDay = normalized == await AccountingCalendar.SelectedAsync(db, 0, configuration, ct)
            };
        }, ct);
    }

    public async Task SaveOpeningAssetsAsync(ClaimsPrincipal actor, OpeningAssetsInput input, CancellationToken ct = default)
    {
        await StoreOperation.RunAsync(async () =>
        {
            var date = new DateOnly(input.Month.Year, input.Month.Month, 1);
            if (date > AccountingCalendar.Today(configuration))
            {
                throw new ValidationException("Choose the current or an earlier month.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            var saved = await db.FinanceClosings.SingleOrDefaultAsync(c => c.Date == date && c.Kind == ClosingKind.Month, ct);
            if (saved?.ClosedAt is not null)
            {
                throw new ValidationException("Cannot adjust opening assets of a closed month. Reopen the month first.");
            }

            saved ??= new FinanceClosing
            {
                Kind = ClosingKind.Month,
                Date = date,
                CreatedByUserId = actorId
            };
            if (saved.Id == 0)
            {
                db.FinanceClosings.Add(saved);
            }

            saved.OpeningStock = input.OpeningStock;
            saved.OpeningCash = input.OpeningCash;
            saved.OpeningBank = input.OpeningBank;
            saved.OpeningSupplierDue = input.OpeningSupplierDue;
            saved.OpeningCustomerDue = input.OpeningCustomerDue;
            saved.OpeningDebt = input.OpeningDebt;
            saved.OpeningNetAssets = input.OpeningStock + input.OpeningCash + input.OpeningBank - input.OpeningSupplierDue + input.OpeningCustomerDue - input.OpeningDebt;
            saved.OpeningAssetsCustomized = true;
            saved.UpdatedAt = DateTimeOffset.UtcNow;
            saved.UpdatedByUserId = actorId;

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }, ct);
    }

    public async Task SaveAsync(ClaimsPrincipal actor, ClosingInput input, bool close, CancellationToken ct = default)
    {
        await StoreOperation.RunAsync(async () =>
        {
            StoreAccess.Money(input.OpeningCash, "Opening cash");
            if (input.Date == default || !Enum.IsDefined(input.Kind) || input.Notes?.Length > 2000)
            {
                throw new ValidationException("Choose a date and enter notes up to 2,000 characters.");
            }
            var date = Normalize(DateOnly.FromDateTime(input.Date), input.Kind);
            if (date > AccountingCalendar.Today(configuration))
            {
                throw new ValidationException("Choose today or an earlier accounting date.");
            }
            var end = End(date, input.Kind);
            if (date > AccountingCalendar.Today(configuration) || (close && end > AccountingCalendar.Today(configuration)))
            {
                throw new ValidationException("Only completed days or months can be closed.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var saved = await db.FinanceClosings.SingleOrDefaultAsync(closing => closing.Date == date && closing.Kind == input.Kind, ct);
            if (saved?.ClosedAt is not null)
            {
                throw new ValidationException("This period has already been closed.");
            }
            if (input.Kind == ClosingKind.Day)
            {
                await AccountingCalendar.EnsureOpenAsync(db, date, ct);
            }
            if (close && input.Kind == ClosingKind.Month
                && await db.DayFinances.AnyAsync(day => day.Date >= date && day.Date <= end && day.ClosedAt == null, ct))
            {
                throw new ValidationException("Close the saved daily records in this month before closing the month.");
            }
            DayFinance? dayFinance = null;
            if (input.Kind == ClosingKind.Day)
            {
                var working = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
                AccountingCalendar.ValidateExpected(working, WorkingDayRequest.Date);
                if (date != working)
                {
                    throw new ValidationException("Only the current working day can be closed.");
                }
                if (close && await db.CashSessions.AnyAsync(session => session.ClosedAt == null, ct))
                {
                    throw new ValidationException("Count and close every location's drawer before closing the business day.");
                }
                if (close && await db.CashDrawers.AnyAsync(drawer => drawer.StockLocationId == null && drawer.CurrentCash != 0, ct))
                {
                    throw new ValidationException("Assign the legacy cash balances to selling locations before closing the day.");
                }
                dayFinance = await db.DayFinances.SingleOrDefaultAsync(day => day.Date == date, ct);
                dayFinance ??= new DayFinance
                {
                    Date = date,
                    OpeningCash = await db.CashDrawers.Select(drawer => (decimal?)drawer.CurrentCash).SumAsync(ct) ?? 0,
                    CreatedByUserId = actorId
                };
                if (dayFinance.Id == 0)
                {
                    db.DayFinances.Add(dayFinance);
                }
                input.OpeningCash = dayFinance.OpeningCash;
                if (!await db.WorkingDays.AnyAsync(day => day.Id == 1, ct))
                {
                    db.WorkingDays.Add(new WorkingDay { Id = 1, Date = date });
                }
            }
            else
            {
                input.OpeningCash = await db.DayFinances.Where(day => day.Date >= date && day.Date <= end)
                    .OrderBy(day => day.Date).Select(day => (decimal?)day.OpeningCash).FirstOrDefaultAsync(ct)
                    ?? await db.FinanceClosings.Where(closing => closing.Kind == ClosingKind.Day && closing.Date >= date && closing.Date <= end)
                        .OrderBy(closing => closing.Date).Select(closing => (decimal?)closing.OpeningCash).FirstOrDefaultAsync(ct)
                    ?? await db.DayFinances.Where(day => day.Date < date && day.ClosedAt != null)
                        .OrderByDescending(day => day.Date).Select(day => day.CountedCash ?? (decimal?)day.ExpectedCash).FirstOrDefaultAsync(ct)
                    ?? await db.FinanceClosings.Where(closing => closing.Date < date && closing.ClosedAt != null)
                        .OrderByDescending(closing => closing.Date).Select(closing => (decimal?)closing.ClosingCash).FirstOrDefaultAsync(ct)
                    ?? 0;
            }
            var totals = await BuildAsync(db, date, input.Kind, input.OpeningCash, input.Notes, ct);
            if (dayFinance is not null)
            {
                var counted = close
                    ? await db.CashDrawers.Select(drawer => (decimal?)drawer.CurrentCash).SumAsync(ct) ?? 0
                    : (decimal?)null;
                dayFinance.ExpectedCash = totals.ClosingCash;
                dayFinance.CountedCash = counted;
                dayFinance.Difference = counted - totals.ClosingCash;
                dayFinance.ClosedAt = close ? DateTimeOffset.UtcNow : null;
                dayFinance.UpdatedAt = DateTimeOffset.UtcNow;
                dayFinance.UpdatedByUserId = actorId;
            }
            if (close)
            {
                StoreAccess.Money(totals.ClosingCash, "Closing physical cash");
            }
            if (close && input.ExpectedRevision is not null && input.ExpectedRevision != ClosureService.Revision(totals))
            {
                throw new ValidationException("Financial activity changed after review. Review the summary again before closing.");
            }
            var beforeClosing = saved is null ? "{}" : ClosureService.PeriodSnapshot(saved);
            saved ??= new FinanceClosing { Kind = input.Kind, Date = date, CreatedByUserId = actorId };
            if (saved.Id == 0)
            {
                db.FinanceClosings.Add(saved);
            }
            saved.OwnerContributions = totals.OwnerContributions;
            saved.OwnerWithdrawals = totals.OwnerWithdrawals;
            saved.OpeningCash = totals.OpeningCash;
            saved.OpeningBank = totals.OpeningBank;
            saved.CashIn = totals.CashIn;
            saved.CashOut = totals.CashOut;
            saved.BankIn = totals.BankIn;
            saved.BankOut = totals.BankOut;
            saved.ClosingCash = totals.ClosingCash;
            saved.ClosingBank = totals.ClosingBank;
            saved.Revenue = totals.Revenue;
            saved.Cogs = totals.Cogs;
            saved.StockVariance = totals.StockVariance;
            saved.Expenses = totals.Expenses;
            saved.SalaryExpense = totals.Salaries;
            saved.SupplierPaid = totals.SupplierPaid;
            saved.PayrollPaid = totals.PayrollPaid;
            saved.DebtBorrowed = totals.DebtBorrowed;
            saved.DebtRepaid = totals.DebtRepaid;
            saved.OutstandingDebt = totals.OutstandingDebt;
            saved.SupplierDue = totals.SupplierDue;
            saved.CustomerDue = totals.CustomerDue;
            saved.StockValue = totals.StockValue;
            saved.SalesReceipts = totals.SalesReceipts;
            saved.CustomerCollections = totals.CustomerCollections;
            saved.PurchaseTotal = totals.PurchaseTotal;
            saved.AdvancesPaid = totals.AdvancesPaid;
            saved.CashVariance = totals.CashVariance;
            if (totals.Monthly is { } monthlyDetails)
            {
                saved.OpeningStock = monthlyDetails.OpeningAssets.Stock;
                saved.OpeningCash = monthlyDetails.OpeningAssets.Cash;
                saved.OpeningBank = monthlyDetails.OpeningAssets.Bank;
                saved.OpeningSupplierDue = monthlyDetails.OpeningAssets.SupplierDue;
                saved.OpeningCustomerDue = monthlyDetails.OpeningAssets.CustomerDue;
                saved.OpeningDebt = monthlyDetails.OpeningAssets.Debt;
                saved.OpeningNetAssets = monthlyDetails.OpeningAssets.NetAssets;

                saved.ClosingStock = monthlyDetails.ClosingAssets.Stock;
                saved.ClosingSupplierDue = monthlyDetails.ClosingAssets.SupplierDue;
                saved.ClosingCustomerDue = monthlyDetails.ClosingAssets.CustomerDue;
                saved.ClosingDebt = monthlyDetails.ClosingAssets.Debt;
                saved.ClosingNetAssets = monthlyDetails.ClosingAssets.NetAssets;
                saved.AssetBuilt = monthlyDetails.AssetGrowth;
            }
            saved.MonthlyDetailsJson = totals.Monthly is null ? null : JsonSerializer.Serialize(totals.Monthly);
            saved.Notes = input.Notes?.Trim();
            saved.ClosedAt = close ? DateTimeOffset.UtcNow : null;
            saved.UpdatedAt = DateTimeOffset.UtcNow;
            saved.UpdatedByUserId = actorId;
            if (dayFinance is not null)
            {
                dayFinance.FinanceClosing = saved;
            }
            await db.SaveChangesAsync(ct);
            if (close)
            {
                db.RecordChanges.Add(new RecordChange
                {
                    RequestId = Guid.NewGuid(),
                    RecordType = input.Kind == ClosingKind.Month ? "month" : "day",
                    RecordId = saved.Id,
                    Action = "Close",
                    Reason = input.Notes?.Trim() ?? "Period reviewed and closed",
                    Before = beforeClosing,
                    After = ClosureService.PeriodSnapshot(saved),
                    CreatedByUserId = actorId
                });
                await db.SaveChangesAsync(ct);
            }
            await transaction.CommitAsync(ct);
        }, ct);
    }

    private async Task<ClosingTotals> BuildAsync(
        AppDbContext db, DateOnly start, ClosingKind kind, decimal openingCash, string? notes, CancellationToken ct, int? locationId = null)
    {
        var end = End(start, kind);
        var from = Utc(start);
        var through = Utc(end.AddDays(1));
        var cash = await db.CashMovements.Where(move => move.BusinessDate >= start && move.BusinessDate <= end && move.CashTransferId == null
                && move.ReversesMovementId == null && !db.CashMovements.Any(reverse => reverse.ReversesMovementId == move.Id))
            .Select(move => new { move.Kind, move.Amount, IsCollection = move.Payment != null && move.Payment.SaleId == null }).ToListAsync(ct);
        var bank = await db.BankMovements.Where(move => move.BusinessDate >= start && move.BusinessDate <= end
                && move.Kind != CashMovementKind.OpeningBalance && move.ReversesMovementId == null
                && !db.BankMovements.Any(reverse => reverse.ReversesMovementId == move.Id))
            .Select(move => new { move.Kind, move.Amount, IsCollection = move.Payment != null && move.Payment.SaleId == null }).ToListAsync(ct);
        var account = await db.BankAccounts.AsNoTracking().SingleOrDefaultAsync(account => account.Code == "MAIN", ct);
        var bankOpening = (account?.OpeningDate <= end ? account.OpeningBalance : 0)
            + (await db.BankMovements.Where(move => move.BusinessDate < start && move.Kind != CashMovementKind.OpeningBalance)
                .Select(move => (decimal?)move.Amount).SumAsync(ct) ?? 0);
        var sales = db.Sales.Where(sale => !sale.IsDeleted && sale.Status == SaleStatus.Completed && sale.SoldAt >= from && sale.SoldAt < through
            && (locationId == null || sale.StockLocationId == locationId.Value));
        var revenue = await sales.Select(sale => (decimal?)sale.TotalAmount).SumAsync(ct) ?? 0;
        var costs = await db.SaleItems.Where(item => !item.Sale!.IsDeleted && item.Sale.Status == SaleStatus.Completed
                && item.Sale.SoldAt >= from && item.Sale.SoldAt < through
                && (locationId == null || item.Sale.StockLocationId == locationId.Value))
            .Select(item => new { item.UnitCost, item.Quantity }).ToListAsync(ct);
        var cogs = costs.Sum(item => decimal.Round(item.UnitCost * item.Quantity, 2, MidpointRounding.AwayFromZero));
        var expenses = await db.Expenses.Where(expense => expense.OccurredAt >= from && expense.OccurredAt < through)
            .Select(expense => (decimal?)expense.Amount).SumAsync(ct) ?? 0;
        var salaries = await db.PayrollEntries.Where(entry => entry.OccurredAt >= from && entry.OccurredAt < through)
            .Select(entry => (decimal?)entry.PaidAmount).SumAsync(ct) ?? 0;
        var advances = await db.PayrollEntries.Where(entry => entry.OccurredAt >= from && entry.OccurredAt < through && entry.AdvanceCreated > 0)
            .Select(entry => (decimal?)entry.AdvanceCreated).SumAsync(ct) ?? 0;
        var purchaseTotal = await db.Purchases.Where(purchase => !purchase.IsDeleted && purchase.ReceivedAt >= from && purchase.ReceivedAt < through
                && (locationId == null || purchase.StockLocationId == locationId.Value))
            .Select(purchase => (decimal?)purchase.TotalAmount).SumAsync(ct) ?? 0;
        var (supplierDue, customerDue) = await FinancePositionReader.DuesAtAsync(db, through, ct);
        var stockValue = await FinancePositionReader.StockValueAtAsync(db, through, ct, locationId);
        var variance = kind == ClosingKind.Month
            ? await db.DayFinances.Where(day => day.Date >= start && day.Date <= end && day.ClosedAt != null)
                .Select(day => day.Difference ?? 0).SumAsync(ct)
            : await db.CashSessions.Where(session => session.BusinessDate == start && session.ClosedAt != null)
                .Select(session => session.Difference ?? 0).SumAsync(ct);
        var cashIn = cash.Where(move => move.Amount > 0).Sum(move => move.Amount);
        var cashOut = -cash.Where(move => move.Amount < 0).Sum(move => move.Amount);
        var bankIn = bank.Where(move => move.Amount > 0).Sum(move => move.Amount);
        var bankOut = -bank.Where(move => move.Amount < 0).Sum(move => move.Amount);
        var totals = new ClosingTotals(start, end, openingCash, bankOpening, cashIn, cashOut, bankIn, bankOut,
            openingCash + cashIn - cashOut + (kind == ClosingKind.Month ? variance : 0), bankOpening + bankIn - bankOut, revenue, cogs, expenses, salaries,
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
            StockVariance = (await db.InventoryMovements.Where(move => move.StockTakeItemId != null
                    && move.OccurredAt >= from && move.OccurredAt < through
                    && (locationId == null || move.StockLocationId == locationId.Value))
                .Select(move => new { move.Quantity, move.UnitCost }).ToArrayAsync(ct))
                .Sum(move => decimal.Round(move.Quantity * move.UnitCost, 2, MidpointRounding.AwayFromZero))
        };
        var locationBreakdown = locationId == null
            ? await MonthlyFinanceBuilder.BuildLocationBreakdownAsync(db, from, through, ct)
            : [];
        return kind == ClosingKind.Month
            ? totals with
            {
                Monthly = await MonthlyFinanceBuilder.BuildAsync(db, configuration, totals, ct, locationId),
                LocationBreakdown = locationBreakdown
            }
            : totals with
            {
                LocationBreakdown = locationBreakdown
            };
    }

    public async Task<IReadOnlyList<ClosingDay>> DailyAsync(ClaimsPrincipal actor, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
        var cash = await db.CashMovements.Where(move => move.BusinessDate >= start && move.BusinessDate <= end && move.CashTransferId == null
                && move.ReversesMovementId == null && !db.CashMovements.Any(reverse => reverse.ReversesMovementId == move.Id))
            .GroupBy(move => move.BusinessDate).Select(group => new
            {
                Date = group.Key,
                In = group.Sum(move => move.Amount > 0 ? move.Amount : 0),
                Out = -group.Sum(move => move.Amount < 0 ? move.Amount : 0)
            }).ToListAsync(ct);
        var bank = await db.BankMovements.Where(move => move.BusinessDate >= start && move.BusinessDate <= end
                && move.Kind != CashMovementKind.OpeningBalance && move.ReversesMovementId == null
                && !db.BankMovements.Any(reverse => reverse.ReversesMovementId == move.Id))
            .GroupBy(move => move.BusinessDate).Select(group => new
            {
                Date = group.Key,
                In = group.Sum(move => move.Amount > 0 ? move.Amount : 0),
                Out = -group.Sum(move => move.Amount < 0 ? move.Amount : 0)
            }).ToListAsync(ct);
        return cash.Select(row => row.Date).Concat(bank.Select(row => row.Date)).Distinct().Order()
            .Select(date => new ClosingDay(date, cash.SingleOrDefault(row => row.Date == date)?.In ?? 0,
                cash.SingleOrDefault(row => row.Date == date)?.Out ?? 0, bank.SingleOrDefault(row => row.Date == date)?.In ?? 0,
                bank.SingleOrDefault(row => row.Date == date)?.Out ?? 0)).ToArray();
    }

    private static async Task<IReadOnlyList<LocationCashClosing>> LocationClosingsAsync(
        AppDbContext db, DateOnly date, CancellationToken ct, int? locationId = null)
    {
        var drawers = await db.CashDrawers.Where(drawer => drawer.OpeningDate <= date
                && (locationId == null || drawer.StockLocationId == locationId.Value))
            .OrderBy(drawer => drawer.StockLocation!.Name)
            .Select(drawer => new { drawer.Id, Name = drawer.StockLocation == null ? "Legacy / unassigned" : drawer.StockLocation.Name, drawer.OpeningBalance })
            .ToArrayAsync(ct);
        var sessions = await db.CashSessions.Where(session => session.BusinessDate <= date
                && (locationId == null || session.StockLocationId == locationId.Value))
            .Select(session => new { session.Id, session.CashDrawerId, session.BusinessDate, session.OpeningCash, session.ExpectedCash, session.ActualClosingCash, session.Difference, session.ClosedAt })
            .ToArrayAsync(ct);
        return drawers.Select(drawer =>
        {
            var session = sessions.Where(session => session.CashDrawerId == drawer.Id).OrderByDescending(session => session.BusinessDate).ThenByDescending(session => session.Id).FirstOrDefault();
            if (session?.BusinessDate == date)
            {
                return new LocationCashClosing(drawer.Name, session.Id, session.ClosedAt == null ? "Open" : "Counted and closed",
                    session.OpeningCash, session.ExpectedCash, session.ActualClosingCash, session.Difference);
            }
            var held = session?.ActualClosingCash ?? session?.ExpectedCash ?? drawer.OpeningBalance;
            return new LocationCashClosing(drawer.Name, null, "Not operated · cash retained", held, held, held, 0);
        }).ToArray();
    }

    private DateTimeOffset Utc(DateOnly date)
    {
        var zone = AccountingCalendar.Zone(configuration);
        return new(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone), TimeSpan.Zero);
    }

    private static DateOnly Normalize(DateOnly date, ClosingKind kind) => kind == ClosingKind.Month ? new(date.Year, date.Month, 1) : date;
    private static DateOnly End(DateOnly date, ClosingKind kind) => kind == ClosingKind.Month ? date.AddMonths(1).AddDays(-1) : date;
    private static ClosingTotals Snapshot(FinanceClosing saved) => new(saved.Date, End(saved.Date, saved.Kind), saved.OpeningCash,
        saved.OpeningBank, saved.CashIn, saved.CashOut, saved.BankIn, saved.BankOut, saved.ClosingCash, saved.ClosingBank,
        saved.Revenue, saved.Cogs, saved.Expenses, saved.SalaryExpense, saved.SupplierPaid, saved.PayrollPaid,
        saved.SupplierDue, saved.CustomerDue, saved.StockValue, saved.ClosedAt, saved.Notes)
    {
        OwnerContributions = saved.OwnerContributions,
        OwnerWithdrawals = saved.OwnerWithdrawals,
        StockVariance = saved.StockVariance,
        DebtBorrowed = saved.DebtBorrowed,
        DebtRepaid = saved.DebtRepaid,
        OutstandingDebt = saved.OutstandingDebt,
        SalesReceipts = saved.SalesReceipts,
        CustomerCollections = saved.CustomerCollections,
        PurchaseTotal = saved.PurchaseTotal,
        AdvancesPaid = saved.AdvancesPaid,
        CashVariance = saved.CashVariance
    };
}
