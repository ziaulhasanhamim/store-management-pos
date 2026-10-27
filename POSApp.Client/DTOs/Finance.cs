using System.ComponentModel.DataAnnotations;
using POSApp.Client.Enums;

namespace POSApp.Client.DTOs;

public sealed class OpenCashInput
{
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public int? StockLocationId { get; set; }
    public decimal OpeningCash { get; set; }
}

public sealed class CloseCashInput
{
    public bool Confirmed { get; set; }
    public decimal? ExpectedCash { get; set; }
    public int SessionId { get; set; }
    [Required]
    public decimal? ActualCash { get; set; }
    public string? Notes { get; set; }
}

public sealed class CashAdjustmentInput
{
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public int SessionId { get; set; }
    public bool Payout { get; set; }
    public decimal Amount { get; set; }
    public string Notes { get; set; } = "";
}

public sealed record CashSummary(CashMovementKind Kind, decimal Amount);
public sealed record CashMovementRow(DateTimeOffset At, CashMovementKind Kind, decimal Amount, string Notes);
public sealed record CashSessionRow(int Id, string Cashier, DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt, decimal Opening, decimal Expected, decimal? Actual, decimal? Difference, string? Notes)
{
    public int DrawerId { get; init; }
    public int? LocationId { get; init; }
    public string Location { get; init; } = "Legacy / unassigned";
    public DateOnly BusinessDate { get; init; }
}
public sealed record CashDrawerPage(CashSessionRow? Current, CashSessionRow? Viewed, IReadOnlyList<CashMovementRow> Movements,
    IReadOnlyList<CashSummary> Breakdown, IReadOnlyList<CashSessionRow> Recent, bool Admin)
{
    public PageInfo? MovementPagination { get; init; }
    public PageInfo? SessionPagination { get; init; }
    public int? SelectedLocationId { get; init; }
    public IReadOnlyList<CashLocationRow> Locations { get; init; } = [];
    public IReadOnlyList<LegacyCashDrawerRow> Unassigned { get; init; } = [];
}

public sealed record CashLocationRow(int Id, string Name, decimal Balance, bool Initialized, int? SessionId, bool ClosedToday);
public sealed record LegacyCashDrawerRow(int Id, decimal Balance, int? SessionId);
public sealed class CashLocationInput
{
    public int LocationId { get; set; }
}
public sealed class AssignCashDrawerInput
{
    public int DrawerId { get; set; }
    public int LocationId { get; set; }
}
public sealed class CashTransferInput
{
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public int SourceLocationId { get; set; }
    public int DestinationLocationId { get; set; }
    public decimal Amount { get; set; }
    public string Notes { get; set; } = "";
}

public sealed class ExpenseInput
{
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public ExpenseCategory Category { get; set; } = ExpenseCategory.Other;
    public int? CategoryId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.Cash;
    public DateTime Date { get; set; } = DateTime.Today;
    public string? ReceiptReference { get; set; }
    public string Notes { get; set; } = "";
}

public sealed class PayrollInput
{
    public DateOnly? WorkingDate { get; set; }
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public int EmployeeId { get; set; }
    public PayrollKind Kind { get; set; } = PayrollKind.Salary;
    public DateTime Date { get; set; } = DateTime.Today;
    public DateTime Month { get; set; } = DateTime.Today;
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.Cash;
    public string Notes { get; set; } = "";
}

public sealed record ExpenseRow(int Id, DateTimeOffset At, ExpenseCategory Category, decimal Amount,
    PaymentMethod Method, string Notes, string? Receipt, string Actor)
{
    public string? CategoryName { get; init; }
}
public sealed record PayrollRow(int Id, DateTimeOffset At, string Employee, PayrollKind Kind, DateOnly Month,
    decimal Amount, decimal AdvanceApplied, decimal Paid, PaymentMethod Method, string Notes)
{
    public decimal AdvanceCreated { get; init; }
}
public sealed record StaffRow(int Id, string Name, decimal Advance)
{
    public decimal BroughtForward { get; init; }
    public decimal DefaultSalary { get; init; }
    public decimal? Salary { get; init; }
    public decimal Paid { get; init; }
    public decimal PriorAdvance { get; init; }
    public decimal Remaining { get; init; }
}
public sealed record EmployeeRow(int Id, string Name, string? Phone, decimal MonthlySalary, decimal Advance)
{
    public decimal BroughtForward { get; init; }
    public decimal? SalaryAmount { get; init; }
    public decimal Settled { get; init; }
    public decimal Paid { get; init; }
    public decimal Bonus { get; init; }
    public decimal PriorAdvance { get; init; }
    public decimal Remaining { get; init; }
}
public sealed record SalaryRow(int Id, int EmployeeId, string Employee, DateOnly Month, decimal Amount, decimal Settled);
public sealed class EmployeeInput
{
    public int? Id { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public decimal MonthlySalary { get; set; }
}
public sealed class SalaryInput
{
    public int EmployeeId { get; set; }
    public DateTime Month { get; set; }
    public decimal Amount { get; set; }
    public string Notes { get; set; } = "";
}
public sealed record PayrollSetup(IReadOnlyList<EmployeeRow> Employees, IReadOnlyList<SalaryRow> Salaries)
{
    public PageInfo? Pagination { get; init; }
}
public sealed record FinancePage(ReportRange Range, IReadOnlyList<ExpenseRow> Expenses, decimal ExpenseTotal,
    int ExpenseCount, int Page, IReadOnlyList<PayrollRow> Payroll, IReadOnlyList<StaffRow> Staff,
    decimal Salaries, decimal AdvancesPaid, decimal PayrollOutflow, bool Admin)
{
    public PageInfo? PayrollPagination { get; init; }
}
public sealed record ProfitPeriod(string Label, decimal Revenue, decimal Cogs, decimal Expenses, decimal Salaries)
{
    public decimal StockVariance { get; init; }
}
public sealed record ProfitReport(ReportRange Range, decimal GrossSales, decimal Returns, decimal Discount,
    decimal Revenue, decimal Cogs, decimal Expenses, decimal Salaries, decimal AdvancesPaid,
    IReadOnlyList<ProfitPeriod> Periods)
{
    public decimal StockVariance { get; init; }
}
