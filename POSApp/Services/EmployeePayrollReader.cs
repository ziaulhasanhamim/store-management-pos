using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;
using POSApp.Persistence.Entities;

namespace POSApp.Services;

internal static class EmployeePayrollReader
{
    public static async Task<PayrollSetup> ReadAsync(AppDbContext db, DateOnly month, IConfiguration configuration, CancellationToken ct, int? page = null, int pageSize = 30, string? search = null)
    {
        var zone = AccountingCalendar.Zone(configuration);
        DateTimeOffset Utc(DateOnly date) => new(TimeZoneInfo.ConvertTimeToUtc(
            date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone), TimeSpan.Zero);
        var start = Utc(month);
        var end = Utc(month.AddMonths(1));
        var query = db.Employees.AsNoTracking().Where(employee => !employee.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var normalizedTerm = NameNormalization.Normalize(term);
            query = query.Where(employee => employee.NormalizedName.Contains(normalizedTerm)
                || employee.Phone != null && employee.Phone.Contains(term));
        }
        var ordered = query.OrderBy(employee => employee.Name).ThenBy(employee => employee.Id)
            .Select(employee => new { employee.Id, employee.Name, employee.Phone, employee.MonthlySalary });
        var selected = page is { } number ? await ordered.ToPageAsync(number, pageSize, ct) : null;
        var employees = selected?.Items ?? await ordered.ToArrayAsync(ct);
        var employeeIds = employees.Select(employee => employee.Id).ToArray();
        var salaries = await db.MonthlySalaries.AsNoTracking().Where(salary => salary.Month == month && employeeIds.Contains(salary.EmployeeId))
            .Select(salary => new { salary.Id, salary.EmployeeId, salary.Amount, salary.BroughtForward, Name = salary.Employee!.Name }).ToArrayAsync(ct);
        var arrears = await db.MonthlySalaries.AsNoTracking()
            .Where(salary => employeeIds.Contains(salary.EmployeeId) && salary.Month < month && salary.CarriedToMonth == null)
            .GroupBy(salary => salary.EmployeeId)
            .Select(group => new { EmployeeId = group.Key, Amount = group.Sum(salary => salary.Amount - salary.SettledAmount) })
            .ToDictionaryAsync(row => row.EmployeeId, row => row.Amount, ct);
        var totals = await db.PayrollEntries.AsNoTracking().Where(entry => entry.OccurredAt < end && employeeIds.Contains(entry.StaffAccount!.EmployeeId))
            .GroupBy(entry => entry.StaffAccount!.EmployeeId)
            .Select(group => new
            {
                EmployeeId = group.Key,
                Advance = group.Sum(entry => entry.AdvanceCreated - entry.AdvanceApplied),
                PriorAdvance = group.Sum(entry => entry.OccurredAt < start ? entry.AdvanceCreated - entry.AdvanceApplied : 0),
                Paid = group.Sum(entry => entry.OccurredAt >= start ? entry.PaidAmount : 0),
                Bonus = group.Sum(entry => entry.OccurredAt >= start && entry.Kind == PayrollKind.Bonus ? entry.PaidAmount : 0),
                Settled = group.Sum(entry => entry.MonthlySalary != null && entry.MonthlySalary.Month == month ? entry.Amount - entry.AdvanceCreated : 0)
            }).ToArrayAsync(ct);
        var rows = employees.Select(employee =>
        {
            var salary = salaries.SingleOrDefault(salary => salary.EmployeeId == employee.Id);
            var total = totals.SingleOrDefault(total => total.EmployeeId == employee.Id);
            var settled = total?.Settled ?? 0;
            var advance = total?.Advance ?? 0;
            var broughtForward = salary?.BroughtForward ?? arrears.GetValueOrDefault(employee.Id);
            return new EmployeeRow(employee.Id, employee.Name, employee.Phone, employee.MonthlySalary, advance)
            {
                SalaryAmount = salary?.Amount,
                BroughtForward = broughtForward,
                Settled = settled,
                Paid = total?.Paid ?? 0,
                Bonus = total?.Bonus ?? 0,
                PriorAdvance = total?.PriorAdvance ?? 0,
                Remaining = (salary?.Amount ?? employee.MonthlySalary + broughtForward) - settled - advance
            };
        }).ToArray();
        return new(rows, salaries.Select(salary => new SalaryRow(salary.Id, salary.EmployeeId, salary.Name, month,
            salary.Amount, rows.Single(employee => employee.Id == salary.EmployeeId).Settled)).ToArray()) { Pagination = selected?.Pagination };
    }
}
