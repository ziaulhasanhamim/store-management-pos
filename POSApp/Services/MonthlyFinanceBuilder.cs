using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;

namespace POSApp.Services;

internal static class MonthlyFinanceBuilder
{
    public const int AssetCalculationVersion = 2;

    public static async Task<MonthlyFinanceDetails> BuildAsync(
        AppDbContext db, IConfiguration configuration, ClosingTotals totals, CancellationToken ct, int? locationId = null)
    {
        var zone = AccountingCalendar.Zone(configuration);
        DateTimeOffset Utc(DateOnly date) => new(TimeZoneInfo.ConvertTimeToUtc(
            date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone), TimeSpan.Zero);
        DateOnly Date(DateTimeOffset at) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);
        var from = Utc(totals.Start);
        var through = Utc(totals.End.AddDays(1));
        var sales = await db.Sales.AsNoTracking().Where(sale => !sale.IsDeleted && sale.Status == SaleStatus.Completed
                && sale.SoldAt >= from && sale.SoldAt < through
                && (locationId == null || sale.StockLocationId == locationId.Value))
            .Select(sale => new { sale.Id, sale.SoldAt, sale.TotalAmount }).ToArrayAsync(ct);
        var lines = await db.SaleItems.AsNoTracking().Where(item => !item.Sale!.IsDeleted && item.Sale.Status == SaleStatus.Completed
                && item.Sale.SoldAt >= from && item.Sale.SoldAt < through
                && (locationId == null || item.Sale.StockLocationId == locationId.Value))
            .Select(item => new { item.SaleId, item.UnitCost, item.Quantity }).ToArrayAsync(ct);
        var expenses = await db.Expenses.AsNoTracking().Where(expense => expense.OccurredAt >= from && expense.OccurredAt < through)
            .Select(expense => new
            {
                expense.OccurredAt,
                expense.Category,
                expense.CategoryDefinitionId,
                Name = expense.CategoryDefinition == null ? null : expense.CategoryDefinition.Name,
                expense.Amount
            }).ToArrayAsync(ct);
        var payroll = await db.PayrollEntries.AsNoTracking().Where(entry => entry.OccurredAt >= from && entry.OccurredAt < through)
            .Select(entry => new
            {
                entry.StaffAccount!.EmployeeId,
                Name = entry.StaffAccount.Employee!.Name,
                entry.OccurredAt,
                entry.Kind,
                entry.PaidAmount,
                entry.AdvanceCreated
            }).ToArrayAsync(ct);
        var salaries = await db.MonthlySalaries.Where(salary => salary.Month == totals.Start)
            .Select(salary => new { salary.EmployeeId, salary.Amount }).ToArrayAsync(ct);
        var costs = lines.GroupBy(line => line.SaleId).ToDictionary(group => group.Key,
            group => group.Sum(line => decimal.Round(line.UnitCost * line.Quantity, 2, MidpointRounding.AwayFromZero)));
        var byDay = sales.GroupBy(sale => Date(sale.SoldAt)).ToDictionary(group => group.Key,
            group => (Sales: group.Sum(sale => sale.TotalAmount), Cogs: group.Sum(sale => costs.GetValueOrDefault(sale.Id))));
        var adjustments = await db.InventoryMovements.AsNoTracking()
            .Where(move => (move.StockTakeItemId != null || move.StockAdjustmentItemId != null || move.Type == InventoryMovementType.Adjustment)
                && move.OccurredAt >= from && move.OccurredAt < through
                && (locationId == null || move.StockLocationId == locationId.Value))
            .Select(move => new { move.OccurredAt, move.Quantity, move.UnitCost }).ToArrayAsync(ct);
        var dailyVariance = adjustments.GroupBy(move => Date(move.OccurredAt))
            .ToDictionary(group => group.Key, group => group.Sum(move =>
                decimal.Round(move.Quantity * move.UnitCost, 2, MidpointRounding.AwayFromZero)));
        var dailyExpense = expenses.GroupBy(expense => Date(expense.OccurredAt)).ToDictionary(group => group.Key, group => group.Sum(expense => expense.Amount));
        var dailyPayroll = payroll.GroupBy(entry => Date(entry.OccurredAt)).ToDictionary(group => group.Key, group => group.Sum(entry => entry.PaidAmount));

        var cashSupplierPayments = await db.CashMovements.AsNoTracking()
            .Where(move => move.BusinessDate >= totals.Start && move.BusinessDate <= totals.End
                && move.Kind == CashMovementKind.SupplierPayment
                && move.CashTransferId == null
                && move.ReversesMovementId == null
                && !db.CashMovements.Any(reverse => reverse.ReversesMovementId == move.Id)
                && (locationId == null || move.CashSession!.StockLocationId == locationId.Value))
            .Select(move => new { move.BusinessDate, Amount = -move.Amount })
            .ToArrayAsync(ct);

        var bankSupplierPayments = locationId == null
            ? await db.BankMovements.AsNoTracking()
                .Where(move => move.BusinessDate >= totals.Start && move.BusinessDate <= totals.End
                    && move.Kind == CashMovementKind.SupplierPayment
                    && move.ReversesMovementId == null
                    && !db.BankMovements.Any(reverse => reverse.ReversesMovementId == move.Id))
                .Select(move => new { move.BusinessDate, Amount = -move.Amount })
                .ToArrayAsync(ct)
            : [];

        var dailySupplierPaid = cashSupplierPayments.Concat(bankSupplierPayments)
            .GroupBy(move => move.BusinessDate)
            .ToDictionary(group => group.Key, group => group.Sum(move => move.Amount));

        var purchases = await db.Purchases.AsNoTracking()
            .Where(purchase => !purchase.IsDeleted && purchase.ReceivedAt >= from && purchase.ReceivedAt < through
                && (locationId == null || purchase.StockLocationId == locationId.Value))
            .Select(purchase => new { ReceivedAt = purchase.ReceivedAt!.Value, purchase.TotalAmount })
            .ToArrayAsync(ct);

        var dailyStockReceived = purchases.GroupBy(purchase => Date(purchase.ReceivedAt))
            .ToDictionary(group => group.Key, group => group.Sum(purchase => purchase.TotalAmount));

        var dayCategoriesByDate = expenses
            .GroupBy(expense => Date(expense.OccurredAt))
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ExpenseCategoryTotal>)group
                    .GroupBy(expense => new { Id = expense.CategoryDefinitionId ?? -(int)expense.Category, expense.Category })
                    .Select(subgroup => new ExpenseCategoryTotal(subgroup.Key.Category, subgroup.Sum(expense => expense.Amount))
                    {
                        Name = subgroup.Select(expense => expense.Name).FirstOrDefault(name => name is not null)
                    })
                    .OrderBy(category => category.Name ?? category.Category.ToString())
                    .ToArray());

        var workingDayDates = (await db.DayFinances.AsNoTracking()
            .Where(day => day.Date >= totals.Start && day.Date <= totals.End)
            .Select(day => day.Date)
            .ToArrayAsync(ct))
            .ToHashSet();

        var totalDays = totals.End.DayNumber - totals.Start.DayNumber + 1;
        var candidateDays = Enumerable.Range(0, totalDays).Select(offset => totals.Start.AddDays(offset)).ToList();

        var workingDates = candidateDays.Where(date =>
            workingDayDates.Contains(date)
            || byDay.ContainsKey(date)
            || dailyExpense.ContainsKey(date)
            || dailyPayroll.ContainsKey(date)
            || dailySupplierPaid.ContainsKey(date)
            || dailyStockReceived.ContainsKey(date)
            || (dailyVariance.TryGetValue(date, out var variance) && variance != 0)
        ).ToList();

        if (workingDates.Count == 0 && candidateDays.Count > 0)
        {
            workingDates.Add(totals.Start);
        }

        var days = workingDates.Select(date =>
        {
            var business = byDay.GetValueOrDefault(date);
            return new DailyBusinessResult(date, business.Sales, business.Cogs,
                dailyExpense.GetValueOrDefault(date), dailyPayroll.GetValueOrDefault(date))
            {
                StockVariance = dailyVariance.GetValueOrDefault(date),
                SupplierPaid = dailySupplierPaid.GetValueOrDefault(date),
                StockReceived = dailyStockReceived.GetValueOrDefault(date),
                ExpenseCategories = dayCategoriesByDate.GetValueOrDefault(date) ?? [],
                IsWorkingDay = true
            };
        }).ToArray();
        var categories = expenses.GroupBy(expense => new { Id = expense.CategoryDefinitionId ?? -(int)expense.Category, expense.Category })
            .Select(group => new ExpenseCategoryTotal(group.Key.Category, group.Sum(expense => expense.Amount))
            {
                Name = group.Select(expense => expense.Name).FirstOrDefault(name => name is not null)
            }).OrderBy(category => category.Name ?? category.Category.ToString()).ToArray();
        var employees = payroll.GroupBy(entry => entry.EmployeeId).OrderBy(group => group.First().Name)
            .Select(group => new EmployeeMonthlyPaid(group.Key, group.First().Name,
                salaries.Where(salary => salary.EmployeeId == group.Key).Select(salary => (decimal?)salary.Amount).SingleOrDefault(),
                group.Where(entry => entry.Kind == PayrollKind.Salary).Sum(entry => entry.PaidAmount - entry.AdvanceCreated),
                group.Where(entry => entry.Kind == PayrollKind.Bonus).Sum(entry => entry.PaidAmount),
                group.Sum(entry => entry.AdvanceCreated))).ToArray();

        // Purchases by supplier in this month
        var purchasesBySupplier = await db.Purchases.AsNoTracking()
            .Where(p => !p.IsDeleted && p.ReceivedAt >= from && p.ReceivedAt < through
                && (locationId == null || p.StockLocationId == locationId.Value))
            .GroupBy(p => p.SupplierId)
            .Select(g => new { SupplierId = g.Key, StockTotal = g.Sum(p => p.TotalAmount) })
            .ToDictionaryAsync(x => x.SupplierId, x => x.StockTotal, ct);

        // Supplier payments by supplier in this month (cash + bank)
        var cashPaymentsBySupplier = await db.CashMovements.AsNoTracking()
            .Where(m => m.BusinessDate >= totals.Start && m.BusinessDate <= totals.End
                && m.Kind == CashMovementKind.SupplierPayment
                && m.SupplierPaymentId != null
                && m.CashTransferId == null
                && m.ReversesMovementId == null
                && !db.CashMovements.Any(r => r.ReversesMovementId == m.Id)
                && (locationId == null || m.CashSession!.StockLocationId == locationId.Value))
            .Select(m => new { m.SupplierPayment!.SupplierBalance!.SupplierId, Amount = -m.Amount })
            .ToArrayAsync(ct);

        var bankPaymentsBySupplier = locationId == null
            ? await db.BankMovements.AsNoTracking()
                .Where(m => m.BusinessDate >= totals.Start && m.BusinessDate <= totals.End
                    && m.Kind == CashMovementKind.SupplierPayment
                    && m.SupplierPaymentId != null
                    && m.ReversesMovementId == null
                    && !db.BankMovements.Any(r => r.ReversesMovementId == m.Id))
                .Select(m => new { m.SupplierPayment!.SupplierBalance!.SupplierId, Amount = -m.Amount })
                .ToArrayAsync(ct)
            : [];

        var paymentsBySupplier = cashPaymentsBySupplier.Concat(bankPaymentsBySupplier)
            .GroupBy(x => x.SupplierId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        var allSupplierIds = purchasesBySupplier.Keys.Concat(paymentsBySupplier.Keys).Distinct().ToList();

        var supplierList = await db.Suppliers.AsNoTracking()
            .Where(s => !s.IsDeleted && (allSupplierIds.Contains(s.Id) || (s.Balance != null && s.Balance.CurrentBalance != 0)))
            .Select(s => new
            {
                s.Id,
                s.Name,
                CurrentBalance = s.Balance == null ? 0 : s.Balance.CurrentBalance
            })
            .OrderBy(s => s.Name)
            .ToArrayAsync(ct);

        var supplierSummaries = supplierList.Select(s => new SupplierMonthlySummary(
            s.Id,
            s.Name,
            purchasesBySupplier.GetValueOrDefault(s.Id),
            paymentsBySupplier.GetValueOrDefault(s.Id),
            s.CurrentBalance)).ToArray();

        var currentClosing = await db.FinanceClosings.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Date == totals.Start && c.Kind == ClosingKind.Month, ct);

        var previousMonthEnd = totals.Start.AddDays(-1);
        var previousMonthStart = new DateOnly(previousMonthEnd.Year, previousMonthEnd.Month, 1);
        var previousClosing = await db.FinanceClosings.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Date == previousMonthStart && c.Kind == ClosingKind.Month, ct);

        decimal openingStock, openingCash, openingBank, openingSupplierDue, openingCustomerDue, openingDebt;

        if (currentClosing is not null && (currentClosing.OpeningAssetsCustomized || currentClosing.OpeningStock > 0 || currentClosing.OpeningSupplierDue > 0))
        {
            openingStock = currentClosing.OpeningStock;
            openingCash = currentClosing.OpeningCash;
            openingBank = currentClosing.OpeningBank;
            openingSupplierDue = currentClosing.OpeningSupplierDue;
            openingCustomerDue = currentClosing.OpeningCustomerDue;
            openingDebt = currentClosing.OpeningDebt;
        }
        else if (previousClosing is not null && previousClosing.ClosedAt != null)
        {
            openingStock = previousClosing.ClosingStock > 0 ? previousClosing.ClosingStock : previousClosing.StockValue;
            openingCash = previousClosing.ClosingCash;
            openingBank = previousClosing.ClosingBank;
            openingSupplierDue = previousClosing.ClosingSupplierDue > 0 ? previousClosing.ClosingSupplierDue : previousClosing.SupplierDue;
            openingCustomerDue = previousClosing.ClosingCustomerDue > 0 ? previousClosing.ClosingCustomerDue : previousClosing.CustomerDue;
            openingDebt = previousClosing.ClosingDebt > 0 ? previousClosing.ClosingDebt : previousClosing.OutstandingDebt;
        }
        else
        {
            var (calcSupplierDue, calcCustomerDue) = await FinancePositionReader.DuesAtAsync(db, from, ct);
            openingStock = await FinancePositionReader.StockValueAtAsync(db, from, ct, locationId);
            openingCash = totals.OpeningCash;
            openingBank = totals.OpeningBank;
            openingSupplierDue = calcSupplierDue;
            openingCustomerDue = calcCustomerDue;
            openingDebt = await DebtService.OutstandingBeforeAsync(db, totals.Start, ct);
        }

        string? selectedLocationName = null;
        if (locationId != null)
        {
            selectedLocationName = await db.StockLocations.AsNoTracking()
                .Where(l => l.Id == locationId.Value)
                .Select(l => l.Name)
                .FirstOrDefaultAsync(ct);
        }

        var closingCash = await CashAtAsync(db, totals.End.AddDays(1), ct);
        return new(days, categories, employees,
            new(openingStock, openingCash, openingBank, openingSupplierDue, openingCustomerDue)
            {
                Debt = openingDebt
            },
            new(totals.StockValue, closingCash, totals.ClosingBank, totals.SupplierDue, totals.CustomerDue)
            {
                Debt = totals.OutstandingDebt
            })
        {
            AssetCalculationVersion = AssetCalculationVersion,
            Suppliers = supplierSummaries,
            SelectedLocationId = locationId,
            SelectedLocationName = selectedLocationName,
            LocationBreakdown = locationId == null ? await BuildLocationBreakdownAsync(db, from, through, ct) : []
        };
    }

    public static async Task<IReadOnlyList<LocationFinanceSummary>> BuildLocationBreakdownAsync(
        AppDbContext db, DateTimeOffset from, DateTimeOffset through, CancellationToken ct)
    {
        var locations = await db.StockLocations.AsNoTracking()
            .Where(l => !l.IsDeleted)
            .OrderBy(l => l.Name)
            .Select(l => new { l.Id, l.Name })
            .ToArrayAsync(ct);

        if (locations.Length == 0) return [];

        var salesByLoc = await db.Sales.AsNoTracking()
            .Where(s => !s.IsDeleted && s.Status == SaleStatus.Completed && s.SoldAt >= from && s.SoldAt < through)
            .GroupBy(s => s.StockLocationId)
            .Select(g => new { LocationId = g.Key, Total = g.Sum(s => s.TotalAmount) })
            .ToDictionaryAsync(x => x.LocationId, x => x.Total, ct);

        var lines = await db.SaleItems.AsNoTracking()
            .Where(i => !i.Sale!.IsDeleted && i.Sale.Status == SaleStatus.Completed && i.Sale.SoldAt >= from && i.Sale.SoldAt < through)
            .Select(i => new { i.Sale!.StockLocationId, i.UnitCost, i.Quantity })
            .ToArrayAsync(ct);

        var cogsByLoc = lines.GroupBy(l => l.StockLocationId)
            .ToDictionary(g => g.Key, g => g.Sum(l => decimal.Round(l.UnitCost * l.Quantity, 2, MidpointRounding.AwayFromZero)));

        var purchasesByLoc = await db.Purchases.AsNoTracking()
            .Where(p => !p.IsDeleted && p.ReceivedAt >= from && p.ReceivedAt < through)
            .GroupBy(p => p.StockLocationId)
            .Select(g => new { LocationId = g.Key, Total = g.Sum(p => p.TotalAmount) })
            .ToDictionaryAsync(x => x.LocationId, x => x.Total, ct);

        var stockValuesByLoc = await db.InventoryBalances.AsNoTracking()
            .GroupBy(b => b.StockLocationId)
            .Select(g => new
            {
                LocationId = g.Key,
                Value = g.Sum(b => b.QuantityOnHand * b.Product!.CostPrice)
            })
            .ToDictionaryAsync(x => x.LocationId, x => x.Value, ct);

        return locations.Select(l =>
        {
            var sales = salesByLoc.GetValueOrDefault(l.Id);
            var cogs = cogsByLoc.GetValueOrDefault(l.Id);
            var profit = sales - cogs;
            var received = purchasesByLoc.GetValueOrDefault(l.Id);
            var stockVal = stockValuesByLoc.GetValueOrDefault(l.Id);
            return new LocationFinanceSummary(l.Id, l.Name, sales, cogs, profit, received, stockVal);
        }).ToArray();
    }

    public static async Task<MonthlyFinanceDetails> CorrectLegacyAssetsAsync(
        AppDbContext db, IConfiguration configuration, ClosingTotals totals,
        MonthlyFinanceDetails details, CancellationToken ct)
    {
        var zone = AccountingCalendar.Zone(configuration);
        DateTimeOffset Utc(DateOnly date) => new(TimeZoneInfo.ConvertTimeToUtc(
            date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone), TimeSpan.Zero);

        async Task<AssetPosition> CorrectAsync(AssetPosition saved, DateOnly before)
        {
            var cutoff = Utc(before);
            var (supplierDue, customerDue) = await FinancePositionReader.DuesAtAsync(db, cutoff, ct);
            var stock = await FinancePositionReader.StockValueAtAsync(db, cutoff, ct);

            return saved with { Stock = stock, SupplierDue = supplierDue, CustomerDue = customerDue };
        }

        // Older JSON snapshots used ledger-only totals. Correct only the asset
        // fields; retain the saved cash counts, payroll, names and trading results.
        return details with
        {
            OpeningAssets = await CorrectAsync(details.OpeningAssets, totals.Start),
            ClosingAssets = await CorrectAsync(details.ClosingAssets, totals.End.AddDays(1)),
            AssetCalculationVersion = AssetCalculationVersion
        };
    }

    private static async Task<decimal> CashAtAsync(AppDbContext db, DateOnly before, CancellationToken ct)
    {
        var drawers = await db.CashDrawers.Where(drawer => drawer.OpeningDate < before)
            .Select(drawer => new { drawer.Id, drawer.OpeningBalance }).ToArrayAsync(ct);
        var latest = await db.CashSessions.Where(session => session.BusinessDate < before)
            .GroupBy(session => session.CashDrawerId).Select(group => new
            {
                DrawerId = group.Key,
                Id = group.Max(session => session.Id)
            }).ToArrayAsync(ct);
        var ids = latest.Select(session => session.Id).ToArray();
        var balances = await db.CashSessions.Where(session => ids.Contains(session.Id))
            .Select(session => new { session.CashDrawerId, Balance = session.ActualClosingCash ?? session.ExpectedCash }).ToArrayAsync(ct);
        return drawers.Sum(drawer => balances.SingleOrDefault(balance => balance.CashDrawerId == drawer.Id)?.Balance ?? drawer.OpeningBalance);
    }
}
