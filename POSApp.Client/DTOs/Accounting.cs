using POSApp.Client.Enums;

namespace POSApp.Client.DTOs;

public sealed class BankInput
{
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public decimal Amount { get; set; }
    public DateOnly Date { get; set; }
    public string Notes { get; set; } = "";
    public string Action { get; set; } = "opening";
}

public sealed record BankRow(DateOnly Date, string Kind, PaymentMethod Method, decimal Amount, decimal BalanceAfter, string Notes);
public sealed record BankPage(decimal Opening, decimal Balance, DateOnly? OpeningDate, IReadOnlyList<BankRow> History, decimal MonthOpening = 0, DateOnly? MonthDate = null);

public sealed class ClosingInput
{
    public bool Confirmed { get; set; }
    public string? ExpectedRevision { get; set; }
    public DateOnly Date { get; set; }
    public decimal? CountedCash { get; set; }
    public ClosingKind Kind { get; set; } = ClosingKind.Day;
    public decimal OpeningCash { get; set; }
    public string? Notes { get; set; }
}

public sealed record StockLocationOption(int Id, string Name);

public sealed record LocationFinanceSummary(
    int LocationId,
    string LocationName,
    decimal Sales,
    decimal Cogs,
    decimal SalesProfit,
    decimal StockReceived,
    decimal StockValue);

public sealed record ClosingTotals(DateOnly Start, DateOnly End, decimal OpeningCash, decimal OpeningBank,
    decimal CashIn, decimal CashOut, decimal BankIn, decimal BankOut, decimal ClosingCash, decimal ClosingBank,
    decimal Revenue, decimal Cogs, decimal Expenses, decimal Salaries, decimal SupplierPaid, decimal PayrollPaid,
    decimal SupplierDue, decimal CustomerDue, decimal StockValue, DateTimeOffset? ClosedAt, string? Notes)
{
    public int? SelectedLocationId { get; init; }
    public string? SelectedLocationName { get; init; }
    public IReadOnlyList<StockLocationOption> AvailableLocations { get; init; } = [];
    public IReadOnlyList<LocationFinanceSummary> LocationBreakdown { get; init; } = [];
    public decimal OwnerContributions { get; init; }
    public decimal OwnerWithdrawals { get; init; }
    public decimal StockVariance { get; init; }
    public decimal DebtBorrowed { get; init; }
    public decimal DebtRepaid { get; init; }
    public decimal OutstandingDebt { get; init; }
    public MonthlyFinanceDetails? Monthly { get; init; }
    public DailyFinanceDetails? Daily { get; init; }
    public IReadOnlyList<LocationCashClosing> Locations { get; init; } = [];
    public decimal? CountedCash { get; init; }
    public decimal? Difference { get; init; }
    public bool DayOpened { get; init; }
    public bool OpeningLocked { get; init; }
    public bool IsWorkingDay { get; init; }
    public decimal SalesReceipts { get; init; }
    public decimal CustomerCollections { get; init; }
    public decimal PurchaseTotal { get; init; }
    public decimal AdvancesPaid { get; init; }
    public decimal CashVariance { get; init; }
    public AwaitingPayoutSummary? AwaitingPayout { get; init; }
}

public sealed record AwaitingPayoutSummary(
    decimal CardAmount,
    decimal MobileWalletAmount,
    int UnsettledCount,
    DateOnly? OldestDate)
{
    public decimal TotalAmount => CardAmount + MobileWalletAmount;
}

public sealed record DailyStockActivity(
    decimal TotalStockInValue,
    decimal TotalStockInUnits,
    decimal TotalStockOutValue,
    decimal TotalStockOutUnits,
    decimal NetStockValueChange,
    decimal NetStockUnitsChange,
    IReadOnlyList<DailyStockInItem> Inward,
    IReadOnlyList<DailyStockOutItem> Outward);

public sealed record DailyStockInItem(
    DateTimeOffset At,
    int PurchaseId,
    string PurchaseNumber,
    string? SupplierInvoiceNumber,
    string SupplierName,
    string LocationName,
    int ItemCount,
    decimal TotalQuantity,
    decimal TotalAmount,
    string? Notes);

public sealed record DailyStockOutItem(
    DateTimeOffset At,
    string Type,
    string Description,
    string LocationName,
    decimal TotalQuantity,
    decimal TotalCostValue,
    string? Notes);

public sealed record DailyFinanceDetails(IReadOnlyList<ExpenseCategoryTotal> Categories,
    IReadOnlyList<DayPayment> Payments)
{
    public AwaitingPayoutSummary? AwaitingPayout { get; init; }
    public DailyStockActivity? StockActivity { get; init; }
}

public sealed record DayPayment(DateTimeOffset At, string Type, string Party, string Account,
    PaymentMethod Method, decimal Amount, string Notes)
{
    public string? CustomMethod { get; init; }
    public bool IsStockIn { get; init; }
}

public sealed class PartyInput
{
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public decimal OpeningBalance { get; set; }
}

public sealed class BalanceAdjustmentInput
{
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public int PartyId { get; set; }
    public decimal Amount { get; set; }
    public bool Opening { get; set; }
    public string Reason { get; set; } = "";
}

public sealed record PartyRow(int Id, string Name, string? Phone, decimal Opening, decimal Balance);
public sealed record PartyEntry(DateTimeOffset At, string Kind, decimal Amount, decimal BalanceAfter, string? Notes);
public sealed record PartyPage(IReadOnlyList<PartyRow> Parties, IReadOnlyList<PartyEntry> History)
{
    public PageInfo? Pagination { get; init; }
    public PageInfo? HistoryPagination { get; init; }
    public IReadOnlyList<PartyRow>? AllParties { get; init; }
}

public sealed record ClosingDay(DateOnly Date, decimal CashIn, decimal CashOut, decimal BankIn, decimal BankOut);

public sealed class CustomerPaymentInput
{
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public int CustomerId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.Cash;
    public string Notes { get; set; } = "";
    public DateTimeOffset? TransactionTime { get; set; }
}

public sealed record DayOverview(DateOnly Date, decimal OpeningCash, decimal CashIn, decimal CashOut,
    decimal ExpectedCash, decimal? CountedCash, DateTimeOffset? ClosedAt);

public sealed record LocationCashClosing(string Location, int? SessionId, string Status,
    decimal Opening, decimal Expected, decimal? Counted, decimal? Difference);

public sealed record DailyBusinessResult(DateOnly Date, decimal Sales, decimal Cogs, decimal Expenses, decimal Payroll)
{
    public decimal StockVariance { get; init; }
    public decimal CashVariance { get; init; }
    public decimal CashShortage => CashVariance < 0 ? Math.Abs(CashVariance) : 0m;
    public decimal CashOverage => CashVariance > 0 ? CashVariance : 0m;
    public decimal SupplierPaid { get; init; }
    public decimal StockReceived { get; init; }
    public IReadOnlyList<ExpenseCategoryTotal> ExpenseCategories { get; init; } = [];
    public bool IsWorkingDay { get; init; } = true;
    public decimal SalesProfit => Sales - Cogs;
    public decimal TotalSpend => Expenses + Payroll;
    public decimal NetProfit => SalesProfit - TotalSpend + StockVariance + CashVariance;
}

public sealed record ExpenseCategoryTotal(int CategoryId, string Name, decimal Amount, ExpenseCategory Category = ExpenseCategory.Other)
{
    public ExpenseCategory Category { get; init; } = Category;
    public string Name { get; init; } = Name;

    public ExpenseCategoryTotal(ExpenseCategory category, decimal amount)
        : this(-(int)category, category == ExpenseCategory.Refreshments ? "Snacks / refreshments" : category.ToString(), amount, category)
    {
    }
}

public sealed record EmployeeMonthlyPaid(int EmployeeId, string Employee, decimal? Salary,
    decimal SalaryPaid, decimal BonusPaid, decimal AdvancePaid)
{
    public decimal TotalPaid => SalaryPaid + BonusPaid + AdvancePaid;
}

public sealed record AssetPosition(decimal Stock, decimal Cash, decimal Bank, decimal SupplierDue, decimal CustomerDue)
{
    public decimal Debt { get; init; }
    public decimal NetAssets => Stock + Cash + Bank - SupplierDue + CustomerDue - Debt;
}

public sealed record SupplierMonthlySummary(
    int SupplierId,
    string SupplierName,
    decimal StockReceived,
    decimal PaymentsMade,
    decimal CurrentDue);

public sealed record MonthlyFinanceDetails(IReadOnlyList<DailyBusinessResult> Days,
    IReadOnlyList<ExpenseCategoryTotal> Categories, IReadOnlyList<EmployeeMonthlyPaid> Employees,
    AssetPosition OpeningAssets, AssetPosition ClosingAssets)
{
    public int AssetCalculationVersion { get; init; }
    public IReadOnlyList<SupplierMonthlySummary> Suppliers { get; init; } = [];
    public int? SelectedLocationId { get; init; }
    public string? SelectedLocationName { get; init; }
    public IReadOnlyList<LocationFinanceSummary> LocationBreakdown { get; init; } = [];

    public decimal StockVariance => Days.Sum(day => day.StockVariance);
    public decimal CashVariance => Days.Sum(day => day.CashVariance);
    public decimal CashShortage => Days.Where(day => day.CashVariance < 0).Sum(day => Math.Abs(day.CashVariance));
    public decimal CashOverage => Days.Where(day => day.CashVariance > 0).Sum(day => day.CashVariance);
    public decimal TotalSales => Days.Sum(day => day.Sales);
    public decimal SalesProfit => Days.Sum(day => day.SalesProfit);
    public decimal TotalSupplierPaid => Days.Sum(day => day.SupplierPaid);
    public decimal TotalStockReceived => Days.Sum(day => day.StockReceived);
    public decimal Expenses => Categories.Sum(category => category.Amount);
    public decimal Payroll => Employees.Sum(employee => employee.TotalPaid);
    public decimal TotalSpend => Expenses + Payroll;
    public decimal NetProfit => SalesProfit - TotalSpend + StockVariance + CashVariance;
    public decimal AssetGrowth => ClosingAssets.NetAssets - OpeningAssets.NetAssets;
}

public sealed class OpeningAssetsInput
{
    public DateOnly Month { get; set; }
    public decimal OpeningStock { get; set; }
    public decimal OpeningCash { get; set; }
    public decimal OpeningBank { get; set; }
    public decimal OpeningSupplierDue { get; set; }
    public decimal OpeningCustomerDue { get; set; }
    public decimal OpeningDebt { get; set; }
}

public sealed class ReopenInput
{
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "day";
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public string Revision { get; set; } = "";
    public string Reason { get; set; } = "";
    public bool Confirmed { get; set; }
}

public sealed record ReopenTarget(int Id, string Kind, DateOnly Date, string Title,
    DateTimeOffset ClosedAt, string Revision);
public sealed record OperatingDay(DateOnly Date, bool Opened, bool Closed,
    int OpenDrawers, int ClosedDrawers, int UnopenedDrawers, bool MonthClosed);

