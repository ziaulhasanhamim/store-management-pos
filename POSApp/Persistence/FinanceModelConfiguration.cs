using Microsoft.EntityFrameworkCore;
using POSApp.Persistence.Entities;

namespace POSApp.Persistence;

internal static class FinanceModelConfiguration
{
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<Lender>(entity =>
        {
            entity.HasIndex(lender => lender.RequestId).IsUnique();
            entity.Property(lender => lender.Name).HasMaxLength(200);
            entity.Property(lender => lender.Phone).HasMaxLength(64);
        });
        builder.Entity<DebtEntry>(entity =>
        {
            entity.HasIndex(entry => entry.RequestId).IsUnique();
            entity.HasIndex(entry => new { entry.LenderBalanceId, entry.BusinessDate });
            entity.HasIndex(entry => entry.BusinessDate);
            entity.Property(entry => entry.Notes).HasMaxLength(2000);
        });
        builder.Entity<CashMovement>().HasIndex(move => move.DebtEntryId).IsUnique();
        builder.Entity<BankMovement>().HasIndex(move => move.DebtEntryId).IsUnique();
        builder.Entity<ExpenseCategoryDefinition>(entity =>
        {
            entity.Property(category => category.Name).HasMaxLength(100);
            entity.HasIndex(category => category.Name).IsUnique();
            entity.HasData(Enum.GetValues<ExpenseCategory>().Select(category => new ExpenseCategoryDefinition
            {
                Id = -(int)category,
                Name = category == ExpenseCategory.Refreshments ? "Snacks / refreshments" : category.ToString(),
                NormalizedName = NameNormalization.Normalize(category == ExpenseCategory.Refreshments ? "Snacks / refreshments" : category.ToString()),
                BuiltInCategory = category
            }));
        });
        builder.Entity<FinancialEntryBatch>(entity =>
        {
            entity.HasIndex(batch => batch.RequestId).IsUnique();
            entity.Property(batch => batch.Kind).HasMaxLength(32);
        });
        builder.Entity<WorkingDay>().Property(day => day.Id).ValueGeneratedNever();
        builder.Entity<DayFinance>(entity =>
        {
            entity.HasIndex(day => day.Date).IsUnique();
            entity.HasIndex(day => day.FinanceClosingId).IsUnique();
        });
        builder.Entity<CashDrawer>(entity =>
        {
            entity.HasIndex(drawer => drawer.StockLocationId).IsUnique();
            entity.HasMany(drawer => drawer.Sessions)
                .WithOne(session => session.CashDrawer)
                .HasForeignKey(session => session.CashDrawerId);
            entity.HasOne(drawer => drawer.CurrentSession)
                .WithOne()
                .HasForeignKey<CashDrawer>(drawer => drawer.CurrentSessionId);
        });
        builder.Entity<CashTransfer>(entity =>
        {
            entity.HasIndex(transfer => transfer.RequestId).IsUnique();
            entity.Property(transfer => transfer.Notes).HasMaxLength(2000);
        });
        builder.Entity<CashSession>(entity =>
        {
            entity.HasIndex(session => session.RequestId).IsUnique();
            entity.HasIndex(session => new { session.CashDrawerId, session.BusinessDate });
            entity.HasIndex(session => new { session.CashDrawerId, session.OpenedAt });
            entity.Property(session => session.ClosingNotes).HasMaxLength(2000);
        });
        builder.Entity<CashMovement>(entity =>
        {
            entity.HasIndex(move => move.RequestId).IsUnique();
            entity.HasIndex(move => new { move.CashSessionId, move.Id });
            entity.HasIndex(move => move.PayrollEntryId).IsUnique();
            entity.HasIndex(move => move.ReversesMovementId).IsUnique();
            entity.Property(move => move.Notes).HasMaxLength(2000);
        });
        builder.Entity<Expense>(entity =>
        {
            entity.HasIndex(expense => expense.RequestId).IsUnique();
            entity.HasIndex(expense => expense.OccurredAt);
            entity.Property(expense => expense.ReceiptReference).HasMaxLength(200);
            entity.Property(expense => expense.Notes).HasMaxLength(2000);
        });
        builder.Entity<StaffAccount>().HasIndex(account => account.EmployeeId).IsUnique();
        builder.Entity<Employee>(entity =>
        {
            entity.Property(employee => employee.Name).HasMaxLength(200);
            entity.Property(employee => employee.Phone).HasMaxLength(64);
        });
        builder.Entity<MonthlySalary>(entity =>
        {
            entity.HasIndex(salary => new { salary.EmployeeId, salary.Month }).IsUnique();
            entity.HasIndex(salary => salary.OccurredAt);
            entity.Property(salary => salary.Notes).HasMaxLength(2000);
        });
        builder.Entity<BankAccount>().HasIndex(account => account.Code).IsUnique();
        builder.Entity<BankMovement>(entity =>
        {
            entity.HasIndex(move => move.RequestId).IsUnique();
            entity.HasIndex(move => move.BusinessDate);
            entity.HasIndex(move => move.PayrollEntryId).IsUnique();
            entity.HasIndex(move => move.ReversesMovementId).IsUnique();
            entity.Property(move => move.Notes).HasMaxLength(2000);
        });
        builder.Entity<CashMovement>().HasIndex(move => move.BusinessDate);
        builder.Entity<FinanceClosing>(entity =>
        {
            entity.HasIndex(closing => new { closing.Kind, closing.Date }).IsUnique();
            entity.Property(closing => closing.Notes).HasMaxLength(2000);
        });
        builder.Entity<StockTransfer>(entity =>
        {
            entity.HasIndex(transfer => transfer.RequestId).IsUnique();
            entity.HasIndex(transfer => transfer.OccurredAt);
            entity.HasIndex(transfer => new { transfer.SourceLocationId, transfer.OccurredAt });
            entity.HasIndex(transfer => new { transfer.DestinationLocationId, transfer.OccurredAt });
            entity.Property(transfer => transfer.Notes).HasMaxLength(2000);
        });
        builder.Entity<StockTransferItem>(entity =>
        {
            entity.HasIndex(item => new { item.StockTransferId, item.ProductId }).IsUnique();
            entity.Property(item => item.Quantity).HasPrecision(18, 3);
        });
        builder.Entity<PayrollEntry>(entity =>
        {
            entity.HasIndex(entry => entry.RequestId).IsUnique();
            entity.HasIndex(entry => entry.OccurredAt);
            entity.HasIndex(entry => new { entry.StaffAccountId, entry.OccurredAt });
            entity.Property(entry => entry.Notes).HasMaxLength(2000);
        });
    }
}
