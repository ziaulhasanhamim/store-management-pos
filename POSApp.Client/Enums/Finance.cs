namespace POSApp.Client.Enums;

public enum ExpenseCategory
{
    Utilities = 1,
    Refreshments,
    Transport,
    Packaging,
    Rent,
    Maintenance,
    Other
}

public enum PayrollKind
{
    Salary = 1,
    Advance,
    Bonus
}

public enum CashMovementKind
{
    Sale = 1,
    SupplierPayment,
    Expense,
    Payroll,
    CashIn,
    Payout,
    Reversal,
    OpeningBalance,
    Transfer,
    DebtBorrowing,
    DebtRepayment,
    OwnerContribution,
    OwnerWithdrawal
}

public enum ClosingKind
{
    Day = 1,
    Month
}

public enum DebtEntryKind
{
    OpeningBalance = 1,
    Borrowing,
    Repayment
}
