using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;

namespace POSApp.Services;

public sealed class EmployeeService(IServiceScopeFactory scopes, IConfiguration configuration)
{
    public async Task<int> SaveAsync(ClaimsPrincipal actor, EmployeeInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 200 || input.Phone?.Length > 64)
        {
            throw new ValidationException("Enter an employee name and valid contact details.");
        }
        StoreAccess.Money(input.MonthlySalary, "Monthly salary");
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
        var employee = input.Id is { } id
            ? await db.Employees.SingleOrDefaultAsync(employee => employee.Id == id && !employee.IsDeleted, ct)
                ?? throw new ValidationException("Employee not found.")
            : new Employee
            {
                Name = input.Name.Trim(),
                NormalizedName = NameNormalization.Normalize(input.Name),
                CreatedByUserId = actorId
            };
        if (input.Id is null)
        {
            db.Employees.Add(employee);
        }
        employee.Name = input.Name.Trim();
        employee.NormalizedName = NameNormalization.Normalize(employee.Name);
        employee.Phone = input.Phone?.Trim();
        employee.MonthlySalary = input.MonthlySalary;
        employee.UpdatedAt = DateTimeOffset.UtcNow;
        employee.UpdatedByUserId = actorId;
        await db.SaveChangesAsync(ct);
        return employee.Id;
    }

    public async Task<EmployeeInput?> FindAsync(ClaimsPrincipal actor, int id, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
        return await db.Employees.AsNoTracking().Where(employee => employee.Id == id && !employee.IsDeleted)
            .Select(employee => new EmployeeInput
            {
                Id = employee.Id,
                Name = employee.Name,
                Phone = employee.Phone,
                MonthlySalary = employee.MonthlySalary
            }).SingleOrDefaultAsync(ct);
    }

    public async Task CreateSalaryAsync(ClaimsPrincipal actor, SalaryInput input, CancellationToken ct = default)
    {
        await StoreOperation.RunAsync(async () =>
        {
            StoreAccess.Money(input.Amount, "Salary");
            if (input.Amount <= 0 || input.Notes?.Length > 2000)
            {
                throw new ValidationException("Choose a month and a positive salary.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var date = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
            AccountingCalendar.ValidateExpected(date, WorkingDayRequest.Date);
            var month = new DateOnly(date.Year, date.Month, 1);
            await AccountingCalendar.EnsureOpenAsync(db, date, ct);
            if (!await db.Employees.AnyAsync(employee => employee.Id == input.EmployeeId && !employee.IsDeleted, ct))
            {
                throw new ValidationException("Choose an employee.");
            }
            if (await db.MonthlySalaries.AnyAsync(salary => salary.EmployeeId == input.EmployeeId && salary.Month == month, ct))
            {
                throw new ValidationException("The salary for this employee and month already exists.");
            }
            var account = await db.StaffAccounts.SingleOrDefaultAsync(account => account.EmployeeId == input.EmployeeId, ct);
            var previousSalaries = await db.MonthlySalaries
                .Where(salary => salary.EmployeeId == input.EmployeeId && salary.Month < month
                    && salary.CarriedToMonth == null && salary.Amount > salary.SettledAmount)
                .ToArrayAsync(ct);
            if (await db.MonthlySalaries.AnyAsync(salary => salary.EmployeeId == input.EmployeeId && salary.Month > month, ct))
            {
                throw new ValidationException("A later salary already exists. Create salaries in month order.");
            }
            var broughtForward = previousSalaries.Sum(salary => salary.Amount - salary.SettledAmount);
            var totalDue = input.Amount + broughtForward;
            StoreAccess.Money(totalDue, "Salary including unpaid balance");
            foreach (var previousSalary in previousSalaries)
            {
                previousSalary.CarriedForward = previousSalary.Amount - previousSalary.SettledAmount;
                previousSalary.CarriedToMonth = month;
                previousSalary.UpdatedAt = DateTimeOffset.UtcNow;
                previousSalary.UpdatedByUserId = actorId;
            }
            var applied = Math.Min(account?.OutstandingAdvance ?? 0, totalDue);
            var at = AccountingCalendar.At(date, configuration);
            var salary = new MonthlySalary
            {
                EmployeeId = input.EmployeeId,
                Month = month,
                Amount = totalDue,
                BroughtForward = broughtForward,
                SettledAmount = applied,
                OccurredAt = at,
                Notes = input.Notes?.Trim() ?? "",
                CreatedByUserId = actorId
            };
            db.MonthlySalaries.Add(salary);
            if (applied > 0)
            {
                account!.OutstandingAdvance -= applied;
                account.UpdatedAt = DateTimeOffset.UtcNow;
                account.UpdatedByUserId = actorId;
                db.PayrollEntries.Add(new PayrollEntry
                {
                    RequestId = Guid.NewGuid(),
                    StaffAccount = account,
                    MonthlySalary = salary,
                    Kind = PayrollKind.Salary,
                    PayrollMonth = month,
                    OccurredAt = at,
                    Amount = applied,
                    AdvanceApplied = applied,
                    PaidAmount = 0,
                    AdvanceBalanceAfter = account.OutstandingAdvance,
                    Notes = "Advance carried into monthly salary",
                    CreatedByUserId = actorId
                });
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }, ct);
    }

    public async Task<PayrollSetup> ReadAsync(ClaimsPrincipal actor, DateOnly month, CancellationToken ct = default, int? page = null, int pageSize = 30, string? search = null)
    {
        month = new(month.Year, month.Month, 1);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
        return await EmployeePayrollReader.ReadAsync(db, month, configuration, ct, page, pageSize, search);
    }
}
