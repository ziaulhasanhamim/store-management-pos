using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;

namespace POSApp.Services;

public sealed class FinanceService(IServiceScopeFactory scopes, IConfiguration configuration)
{
    public TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(configuration["BusinessTimeZone"] ?? "Asia/Dhaka");
    public DateTime Today => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zone).Date;

    public DateTimeOffset DateToUtc(DateTime date)
    {
        if (date == default || date.Date > Today)
        {
            throw new ValidationException("Choose today or an earlier date.");
        }
        var local = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, Zone), TimeSpan.Zero);
    }

    public async Task<int> AddExpenseAsync(ClaimsPrincipal actor, ExpenseInput input, CancellationToken ct = default)
    {
        return await StoreOperation.RunAsync<int>(async () =>
        {
            StoreAccess.Money(input.Amount, "Expense amount");
            CashDrawerService.ValidateNotes(input.Notes);
            if (input.Amount <= 0 || input.RequestId == Guid.Empty || !Enum.IsDefined(input.Category)
                || !Enum.IsDefined(input.Method) || input.ReceiptReference?.Length > 200)
            {
                throw new ValidationException("Enter a positive expense, category, payment method, and receipt reference up to 200 characters.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var hash = StoreOperation.Fingerprint(input);
            var previous = await db.Expenses.SingleOrDefaultAsync(expense => expense.RequestId == input.RequestId, ct);
            if (previous is not null)
            {
                StoreOperation.CheckReplay(actorId, previous.CreatedByUserId, previous.RequestHash, hash);
                if (previous.CreatedByUserId != actorId)
                {
                    throw new UnauthorizedAccessException();
                }
                return previous.Id;
            }
            var date = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
            AccountingCalendar.ValidateExpected(date, WorkingDayRequest.Date);
            var at = AccountingCalendar.At(date, configuration);
            var category = await db.ExpenseCategories.FindAsync([input.CategoryId ?? -(int)input.Category], ct)
                ?? throw new ValidationException("Select an expense category.");
            if (category.IsArchived)
            {
                throw new ValidationException("Select an active expense category.");
            }
            var expense = new Expense
            {
                RequestId = input.RequestId,
                RequestHash = hash,
                Category = category.BuiltInCategory ?? ExpenseCategory.Other,
                CategoryDefinitionId = category.Id,
                Amount = input.Amount,
                Method = input.Method,
                OccurredAt = at,
                ReceiptReference = input.ReceiptReference?.Trim(),
                Notes = input.Notes.Trim(),
                CreatedByUserId = actorId
            };
            db.Expenses.Add(expense);
            await FinancialPosting.PostAsync(db, actorId, input.Method, -input.Amount, CashMovementKind.Expense,
                input.Notes.Trim(), date, ct, expense: expense);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return expense.Id;
        }, ct);
    }

    public async Task<int> AddPayrollAsync(ClaimsPrincipal actor, PayrollInput input, CancellationToken ct = default)
    {
        return await StoreOperation.RunAsync<int>(async () =>
        {
            StoreAccess.Money(input.Amount, "Payroll amount");
            CashDrawerService.ValidateNotes(input.Notes);
            if (input.Amount <= 0 || input.RequestId == Guid.Empty || input.Kind is not (PayrollKind.Salary or PayrollKind.Bonus)
                || !Enum.IsDefined(input.Method) || input.Month == default)
            {
                throw new ValidationException("Choose salary payment or bonus and enter a positive payment amount.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var hash = StoreOperation.Fingerprint(new
            {
                input.RequestId, input.EmployeeId, input.Kind, input.Month,
                input.Amount, input.Method, input.Notes
            });
            var previous = await db.PayrollEntries.SingleOrDefaultAsync(entry => entry.RequestId == input.RequestId, ct);
            if (previous is not null)
            {
                StoreOperation.CheckReplay(actorId, previous.CreatedByUserId, previous.RequestHash, hash);
                return previous.Id;
            }
            var date = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
            AccountingCalendar.ValidateExpected(date, WorkingDayRequest.Date);
            AccountingCalendar.ValidateExpected(date, input.WorkingDate);
            var at = AccountingCalendar.At(date, configuration);
            var employee = await db.Employees.SingleOrDefaultAsync(employee => employee.Id == input.EmployeeId && !employee.IsDeleted, ct)
                ?? throw new ValidationException("Choose an employee from the employee list.");
            var account = await db.StaffAccounts.SingleOrDefaultAsync(item => item.EmployeeId == input.EmployeeId, ct);
            if (account is null)
            {
                account = new StaffAccount { EmployeeId = input.EmployeeId, CreatedByUserId = actorId };
                db.StaffAccounts.Add(account);
            }
            var month = new DateOnly(input.Month.Year, input.Month.Month, 1);
            if (month > new DateOnly(date.Year, date.Month, 1))
            {
                throw new ValidationException("Choose the working month or an earlier salary month.");
            }
            MonthlySalary? salary = null;
            decimal advanceCreated = 0;
            decimal advanceApplied = 0;
            if (input.Kind == PayrollKind.Salary)
            {
                salary = await db.MonthlySalaries.SingleOrDefaultAsync(salary => salary.EmployeeId == input.EmployeeId && salary.Month == month, ct)
                    ?? throw new ValidationException("Create this employee's salary for the month before paying it.");
                if (salary.CarriedToMonth is { } carriedTo)
                {
                    throw new ValidationException($"This salary balance was carried to {carriedTo:MMMM yyyy}. Pay that month's salary instead.");
                }
                var remaining = Math.Max(0, salary.Amount - salary.SettledAmount);
                advanceApplied = Math.Min(account.OutstandingAdvance, remaining);
                remaining -= advanceApplied;
                var settled = Math.Min(input.Amount, remaining);
                advanceCreated = input.Amount - settled;
                salary.SettledAmount += advanceApplied + settled;
                salary.UpdatedAt = DateTimeOffset.UtcNow;
                salary.UpdatedByUserId = actorId;
            }
            await AccountingCalendar.EnsureOpenAsync(db, date, ct);
            account.OutstandingAdvance += advanceCreated - advanceApplied;
            StoreAccess.Money(account.OutstandingAdvance, "Staff advance balance");
            account.UpdatedAt = DateTimeOffset.UtcNow;
            account.UpdatedByUserId = actorId;
            var entry = new PayrollEntry
            {
                RequestId = input.RequestId,
                RequestHash = hash,
                StaffAccount = account,
                MonthlySalary = salary,
                Kind = input.Kind,
                PayrollMonth = month,
                OccurredAt = at,
                Amount = input.Amount + advanceApplied,
                AdvanceApplied = advanceApplied,
                AdvanceCreated = advanceCreated,
                PaidAmount = input.Amount,
                AdvanceBalanceAfter = account.OutstandingAdvance,
                Method = input.Method,
                Notes = input.Notes.Trim(),
                CreatedByUserId = actorId
            };
            db.PayrollEntries.Add(entry);
            if (entry.PaidAmount != 0)
            {
                await FinancialPosting.PostAsync(db, actorId, input.Method, -entry.PaidAmount, CashMovementKind.Payroll,
                    $"{entry.Kind} · {employee.Name} · {month:MMM yyyy}", date, ct, payroll: entry);
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return entry.Id;
        }, ct);
    }

    public async Task<FinancePage> ReadAsync(ClaimsPrincipal actor, string? period, DateOnly? start, DateOnly? end,
        int page = 1, CancellationToken ct = default, DateOnly? payrollMonth = null, bool payrollOnly = false)
    {
        var range = ReportService.ResolveRange(period, start, end, Zone, DateTimeOffset.UtcNow);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);
        var admin = await CashDrawerService.IsAdminAsync(db, actorId, ct);
        decimal total = 0;
        var count = 0;
        var currentPage = 1;
        ExpenseRow[] rows = [];
        if (!payrollOnly)
        {
            var expenses = db.Expenses.AsNoTracking().Where(expense => expense.OccurredAt >= range.FromUtc
                && expense.OccurredAt < range.ToUtc && (admin || expense.CreatedByUserId == actorId));
            var summary = await expenses.GroupBy(expense => 1)
                .Select(group => new { Total = group.Sum(expense => expense.Amount), Count = group.Count() }).SingleOrDefaultAsync(ct);
            total = summary?.Total ?? 0;
            count = summary?.Count ?? 0;
            currentPage = Math.Clamp(page, 1, Math.Max(1, (count + 29) / 30));
            rows = await expenses.OrderByDescending(expense => expense.OccurredAt).ThenByDescending(expense => expense.Id)
                .Skip((currentPage - 1) * 30).Take(30)
                .Select(expense => new ExpenseRow(expense.Id, expense.OccurredAt, expense.Category, expense.Amount, expense.Method,
                    expense.Notes, expense.ReceiptReference,
                    db.Users.Where(user => user.Id == expense.CreatedByUserId).Select(user => user.UserName!).FirstOrDefault() ?? "User")
                {
                    CategoryName = expense.CategoryDefinition == null ? null : expense.CategoryDefinition.Name
                })
                .ToArrayAsync(ct);
        }
        PageInfo? payrollPagination = null;
        PayrollRow[] payroll = [];
        StaffRow[] staff = [];
        decimal salaries = 0;
        decimal advances = 0;
        decimal outflow = 0;
        if (admin)
        {
            var entries = db.PayrollEntries.AsNoTracking().Where(entry => entry.OccurredAt >= range.FromUtc && entry.OccurredAt < range.ToUtc);
            var payrollSummary = await entries.GroupBy(entry => 1).Select(group => new
            {
                Advances = group.Sum(entry => entry.AdvanceCreated),
                Outflow = group.Sum(entry => entry.PaidAmount)
            }).SingleOrDefaultAsync(ct);
            advances = payrollSummary?.Advances ?? 0;
            outflow = payrollSummary?.Outflow ?? 0;
            var payrollResult = await entries.OrderByDescending(entry => entry.Id)
                .Select(entry => new PayrollRow(entry.Id, entry.OccurredAt, entry.StaffAccount!.Employee!.Name, entry.Kind,
                    entry.PayrollMonth, entry.Amount, entry.AdvanceApplied, entry.PaidAmount, entry.Method, entry.Notes)
                {
                    AdvanceCreated = entry.AdvanceCreated
                }).ToPageAsync(page, 30, ct);
            payroll = payrollResult.Items.ToArray();
            payrollPagination = payrollResult.Pagination;
            var working = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
            var month = payrollMonth ?? new DateOnly(working.Year, working.Month, 1);
            var payrollEmployees = await EmployeePayrollReader.ReadAsync(db, month, configuration, ct);
            staff = payrollEmployees.Employees.Select(employee => new StaffRow(employee.Id, employee.Name, employee.Advance)
            {
                DefaultSalary = employee.MonthlySalary,
                BroughtForward = employee.BroughtForward,
                Salary = employee.SalaryAmount,
                Paid = employee.Paid,
                PriorAdvance = employee.PriorAdvance,
                Remaining = employee.Remaining
            }).ToArray();
            salaries = outflow;
        }
        return new(range, rows, total, count, currentPage, payroll, staff, salaries, advances, outflow, admin) { PayrollPagination = payrollPagination };
    }

    public async Task<ProfitReport> ProfitAsync(ClaimsPrincipal actor, string? period, DateOnly? start, DateOnly? end,
        string? group, CancellationToken ct = default)
    {
        return await StoreOperation.RunAsync<ProfitReport>(async () =>
        {
            var range = ReportService.ResolveRange(period, start, end, Zone, DateTimeOffset.UtcNow);
            if (range.End.DayNumber - range.Start.DayNumber > 366)
            {
                throw new ValidationException("Choose a period of up to one year.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var sales = await db.Sales.AsNoTracking().Where(sale => !sale.IsDeleted && sale.Status == SaleStatus.Completed
                    && sale.SoldAt >= range.FromUtc && sale.SoldAt < range.ToUtc)
                .Select(sale => new SalesProfitSource
                {
                    Id = sale.Id,
                    At = sale.SoldAt,
                    Subtotal = sale.Subtotal,
                    Discount = sale.DiscountAmount,
                    Revenue = sale.TotalAmount
                }).ToListAsync(ct);
            var adjustments = await db.InventoryMovements.AsNoTracking()
                .Where(move => (move.StockTakeItemId != null || move.StockAdjustmentItemId != null || move.Type == InventoryMovementType.Adjustment)
                    && move.OccurredAt >= range.FromUtc && move.OccurredAt < range.ToUtc)
                .Select(move => new { move.OccurredAt, move.Quantity, move.UnitCost }).ToArrayAsync(ct);
            var variance = adjustments.Select(move => new DatedAmount(move.OccurredAt,
                decimal.Round(move.Quantity * move.UnitCost, 2, MidpointRounding.AwayFromZero))).ToArray();
            var bySale = sales.ToDictionary(sale => sale.Id);
            // Stream scalar line snapshots so currency rounding uses the same decimal
            // rules on every provider without loading tracked products or receipts.
            var lines = db.SaleItems.AsNoTracking().Where(item => !item.Sale!.IsDeleted
                    && item.Sale.Status == SaleStatus.Completed && item.Sale.SoldAt >= range.FromUtc && item.Sale.SoldAt < range.ToUtc)
                .Select(item => new { item.SaleId, item.Quantity, item.UnitPrice, item.UnitCost });
            await foreach (var line in lines.AsAsyncEnumerable().WithCancellation(ct))
            {
                var sale = bySale[line.SaleId];
                sale.Cogs += decimal.Round(line.UnitCost * line.Quantity, 2, MidpointRounding.AwayFromZero);
                if (line.Quantity < 0)
                {
                    sale.Returns -= decimal.Round(line.UnitPrice * line.Quantity, 2, MidpointRounding.AwayFromZero);
                }
            }
            var expenses = await db.Expenses.AsNoTracking().Where(expense => expense.OccurredAt >= range.FromUtc && expense.OccurredAt < range.ToUtc)
                .Select(expense => new DatedAmount(expense.OccurredAt, expense.Amount)).ToListAsync(ct);
            var payroll = await db.PayrollEntries.AsNoTracking().Where(entry => entry.OccurredAt >= range.FromUtc && entry.OccurredAt < range.ToUtc)
                .Select(entry => new { entry.OccurredAt, entry.Kind, entry.Amount, entry.PaidAmount, entry.AdvanceCreated }).ToListAsync(ct);
            var accrued = payroll.Select(entry => new DatedAmount(entry.OccurredAt, entry.PaidAmount)).ToList();
            DateOnly Bucket(DateTimeOffset at)
            {
                var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, Zone).DateTime);
                return group switch
                {
                    "month" => new(date.Year, date.Month, 1),
                    "week" => date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
                    _ => date
                };
            }
            var periods = new SortedDictionary<DateOnly, decimal[]>();
            decimal[] Values(DateTimeOffset at)
            {
                var key = Bucket(at);
                if (!periods.TryGetValue(key, out var values))
                {
                    values = new decimal[5];
                    periods.Add(key, values);
                }
                return values;
            }
            foreach (var sale in sales)
            {
                var values = Values(sale.At);
                values[0] += sale.Revenue;
                values[1] += sale.Cogs;
            }
            foreach (var expense in expenses)
            {
                Values(expense.At)[2] += expense.Amount;
            }
            foreach (var salary in accrued)
            {
                Values(salary.At)[3] += salary.Amount;
            }
            foreach (var adjustment in variance)
            {
                Values(adjustment.At)[4] += adjustment.Amount;
            }
            var report = new ProfitReport(range, sales.Sum(sale => sale.Subtotal + sale.Returns), sales.Sum(sale => sale.Returns), sales.Sum(sale => sale.Discount),
                sales.Sum(sale => sale.Revenue), sales.Sum(sale => sale.Cogs), expenses.Sum(expense => expense.Amount),
                accrued.Sum(entry => entry.Amount),
                payroll.Sum(entry => entry.AdvanceCreated),
                periods.Select(pair => new ProfitPeriod(pair.Key.ToString(group == "month" ? "MMM yyyy" : "dd MMM yyyy"),
                    pair.Value[0], pair.Value[1], pair.Value[2], pair.Value[3])
                {
                    StockVariance = pair.Value[4]
                }).ToArray())
            {
                StockVariance = variance.Sum(adjustment => adjustment.Amount)
            };
            await transaction.CommitAsync(ct);
            return report;
        }, ct);
    }

    private sealed record DatedAmount(DateTimeOffset At, decimal Amount);

    private sealed class SalesProfitSource
    {
        public int Id { get; set; }
        public DateTimeOffset At { get; set; }
        public decimal Subtotal { get; set; }
        public decimal Returns { get; set; }
        public decimal Discount { get; set; }
        public decimal Revenue { get; set; }
        public decimal Cogs { get; set; }
    }
}
