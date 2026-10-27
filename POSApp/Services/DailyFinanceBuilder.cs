using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;

namespace POSApp.Services;

internal static class DailyFinanceBuilder
{
    public static async Task<DailyFinanceDetails> BuildAsync(
        AppDbContext db, IConfiguration configuration, DateOnly date, CancellationToken ct, int? locationId = null)
    {
        var zone = AccountingCalendar.Zone(configuration);
        DateTimeOffset Utc(DateOnly day) => new(TimeZoneInfo.ConvertTimeToUtc(
            day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone), TimeSpan.Zero);
        var from = Utc(date);
        var through = Utc(date.AddDays(1));
        var expenses = await db.Expenses.AsNoTracking()
            .Where(expense => expense.OccurredAt >= from && expense.OccurredAt < through)
            .Select(expense => new
            {
                expense.Category,
                expense.CategoryDefinitionId,
                Name = expense.CategoryDefinition == null ? null : expense.CategoryDefinition.Name,
                expense.Amount
            }).ToArrayAsync(ct);
        var categories = expenses.GroupBy(expense => new
            {
                Id = expense.CategoryDefinitionId ?? -(int)expense.Category,
                expense.Category
            })
            .Select(group => new ExpenseCategoryTotal(group.Key.Category, group.Sum(expense => expense.Amount))
            {
                Name = group.First().Name
            }).OrderBy(category => category.Name ?? category.Category.ToString()).ToArray();

        // Use active ledger postings so corrections and deleted invoices are counted once.
        // Drawer-to-drawer transfers are internal and do not change business funds.
        var cash = await db.CashMovements.AsNoTracking()
            .Where(move => move.BusinessDate == date && move.CashTransferId == null
                && move.ReversesMovementId == null
                && !db.CashMovements.Any(reverse => reverse.ReversesMovementId == move.Id)
                && (locationId == null || move.CashSession!.StockLocationId == locationId.Value))
            .Select(move => new PaymentSnapshot(
                move.CreatedAt, move.Kind,
                move.Payment != null && move.Payment.SaleId == null,
                move.SupplierPayment != null ? move.SupplierPayment.SupplierBalance!.Supplier!.Name
                    : move.Payment != null ? (move.Payment.CustomerBalance == null ? "Walk-in" : move.Payment.CustomerBalance.Customer!.Name)
                    : move.PayrollEntry != null ? move.PayrollEntry.StaffAccount!.Employee!.Name
                    : move.DebtEntry != null ? move.DebtEntry.LenderBalance!.Lender!.Name : "",
                move.CashSession!.StockLocation == null ? "Cash drawer" : move.CashSession.StockLocation.Name,
                PaymentMethod.Cash, move.Amount, move.Notes)).ToArrayAsync(ct);
        var bank = locationId == null
            ? await db.BankMovements.AsNoTracking()
                .Where(move => move.BusinessDate == date && move.Kind != CashMovementKind.OpeningBalance
                    && move.ReversesMovementId == null
                    && !db.BankMovements.Any(reverse => reverse.ReversesMovementId == move.Id))
                .Select(move => new PaymentSnapshot(
                    move.CreatedAt, move.Kind,
                    move.Payment != null && move.Payment.SaleId == null,
                    move.SupplierPayment != null ? move.SupplierPayment.SupplierBalance!.Supplier!.Name
                        : move.Payment != null ? (move.Payment.CustomerBalance == null ? "Walk-in" : move.Payment.CustomerBalance.Customer!.Name)
                        : move.PayrollEntry != null ? move.PayrollEntry.StaffAccount!.Employee!.Name
                        : move.DebtEntry != null ? move.DebtEntry.LenderBalance!.Lender!.Name : "",
                    "Bank / digital", move.Method, move.Amount, move.Notes)).ToArrayAsync(ct)
            : [];
        return new(categories, cash.Concat(bank).OrderBy(row => row.At)
            .Select(row => new DayPayment(row.At, Label(row.Kind, row.IsCollection), row.Party,
                row.Account, row.Method, row.Amount, row.Notes)).ToArray());
    }

    private static string Label(CashMovementKind kind, bool collection) => collection ? "Customer collections" : kind switch
    {
        CashMovementKind.Sale => "Sales receipts / refunds",
        CashMovementKind.SupplierPayment => "Supplier payments",
        CashMovementKind.Expense => "Expenses",
        CashMovementKind.Payroll => "Salary / bonus / advances",
        CashMovementKind.OwnerContribution => "Owner contributions",
        CashMovementKind.OwnerWithdrawal => "Owner withdrawals",
        CashMovementKind.Transfer => "Account transfers",
        CashMovementKind.DebtBorrowing => "Money borrowed",
        CashMovementKind.DebtRepayment => "Debt repayments",
        _ => "Other cash movements"
    };

    private sealed record PaymentSnapshot(DateTimeOffset At, CashMovementKind Kind, bool IsCollection,
        string Party, string Account, PaymentMethod Method, decimal Amount, string Notes);
}
