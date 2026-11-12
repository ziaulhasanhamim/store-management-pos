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
            entity.Property(entry => entry.RequestId).IsRequired();
            entity.Property(entry => entry.Notes).HasMaxLength(2000).IsRequired();
            entity.Property(entry => entry.VoidReason).HasMaxLength(2000);
        });
        builder.Entity<CashMovement>().HasIndex(move => move.DebtEntryId).IsUnique();
        builder.Entity<BankMovement>().HasIndex(move => move.DebtEntryId).IsUnique();
        builder.Entity<ExpenseCategoryDefinition>(entity =>
        {
            entity.Property(category => category.Name).HasMaxLength(100);
            entity.HasIndex(category => category.Name).IsUnique();
        });
        builder.Entity<FinancialEntryBatch>(entity =>
        {
            entity.HasIndex(batch => batch.RequestId).IsUnique();
            entity.Property(batch => batch.Kind).HasMaxLength(32);
        });

        builder.Entity<CashDrawer>(entity =>
        {
            entity.HasIndex(drawer => drawer.StockLocationId).IsUnique();
            entity.HasMany(drawer => drawer.Sessions)
                .WithOne(session => session.CashDrawer)
                .HasForeignKey(session => session.CashDrawerId);
            entity.HasMany(drawer => drawer.Movements)
                .WithOne(move => move.CashDrawer)
                .HasForeignKey(move => move.CashDrawerId);
            entity.HasMany(drawer => drawer.Counts)
                .WithOne(count => count.CashDrawer)
                .HasForeignKey(count => count.CashDrawerId);
            entity.HasOne(drawer => drawer.CurrentSession)
                .WithOne()
                .HasForeignKey<CashDrawer>(drawer => drawer.CurrentSessionId);
        });
        builder.Entity<CashCount>(entity =>
        {
            entity.HasIndex(count => count.RequestId).IsUnique();
            entity.HasIndex(count => new { count.CashDrawerId, count.CountedAt });
            entity.HasIndex(count => new { count.CashDrawerId, count.BusinessDate, count.CountedAt, count.Id });
            entity.HasIndex(count => new { count.StockLocationId, count.BusinessDate });
            entity.Property(count => count.Notes).HasMaxLength(2000);
            entity.Property(count => count.VoidReason).HasMaxLength(2000);
            entity.Property(count => count.AcceptedVariance).HasPrecision(18, 2);
            entity.Property(count => count.IsDeleted).HasColumnName("IsVoided");
            entity.Property(count => count.DeletedAt).HasColumnName("VoidedAt");
            entity.Ignore(count => count.RecordedAt);
            entity.Ignore(count => count.IsVoided);
            entity.Ignore(count => count.VoidedAt);
            entity.HasOne(count => count.CashDrawer)
                .WithMany(drawer => drawer.Counts)
                .HasForeignKey(count => count.CashDrawerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(count => count.StockLocation)
                .WithMany()
                .HasForeignKey(count => count.StockLocationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(count => count.CountedByUser)
                .WithMany()
                .HasForeignKey(count => count.CountedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(count => count.VarianceMovement)
                .WithMany()
                .HasForeignKey(count => count.VarianceMovementId)
                .OnDelete(DeleteBehavior.SetNull);
        });
        builder.Entity<CashTransfer>(entity =>
        {
            entity.HasIndex(transfer => transfer.RequestId).IsUnique();
            entity.Property(transfer => transfer.Notes).HasMaxLength(2000);
            entity.Property(transfer => transfer.VoidReason).HasMaxLength(2000);
        });
        builder.Entity<CashSession>(entity =>
        {
            entity.HasIndex(session => session.RequestId).IsUnique();
            entity.HasIndex(session => new { session.CashDrawerId, session.OpenedAt });
            entity.Property(session => session.ClosingNotes).HasMaxLength(2000);
        });
        builder.Entity<CashMovement>(entity =>
        {
            entity.HasIndex(move => move.RequestId).IsUnique();
            entity.HasIndex(move => move.BusinessDate);
            entity.HasIndex(move => new { move.CashDrawerId, move.CreatedAt });
            entity.HasIndex(move => new { move.CashDrawerId, move.BusinessDate, move.CreatedAt, move.Id })
                .IncludeProperties(move => new { move.Amount, move.Kind });
            entity.HasIndex(move => new { move.CashSessionId, move.Id });
            entity.HasIndex(move => move.PayrollEntryId).IsUnique();
            entity.HasIndex(move => move.ReversesMovementId).IsUnique();
            entity.Property(move => move.Notes).HasMaxLength(2000);
            entity.HasOne(move => move.CashDrawer)
                .WithMany(drawer => drawer.Movements)
                .HasForeignKey(move => move.CashDrawerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(move => move.CashSession)
                .WithMany(session => session.Movements)
                .HasForeignKey(move => move.CashSessionId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);
        });
        builder.Entity<Expense>(entity =>
        {
            entity.HasIndex(expense => expense.RequestId).IsUnique();
            entity.HasIndex(expense => expense.OccurredAt);
            entity.HasIndex(expense => new { expense.StockLocationId, expense.OccurredAt });
            entity.HasIndex(expense => new { expense.CategoryDefinitionId, expense.OccurredAt });
            entity.Property(expense => expense.ReceiptReference).HasMaxLength(200);
            entity.Property(expense => expense.Notes).HasMaxLength(2000);
            entity.Property(expense => expense.VoidReason).HasMaxLength(2000);
            entity.HasOne(expense => expense.StockLocation)
                .WithMany()
                .HasForeignKey(expense => expense.StockLocationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(expense => expense.CategoryDefinition)
                .WithMany()
                .HasForeignKey(expense => expense.CategoryDefinitionId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<PaymentClearingEntry>(entity =>
        {
            entity.HasIndex(entry => entry.RequestId).IsUnique();
            entity.HasIndex(entry => new { entry.Status, entry.StockLocationId, entry.Method, entry.BusinessDate });
            entity.HasIndex(entry => entry.PaymentId);
            entity.Property(entry => entry.Notes).HasMaxLength(2000);
            entity.HasOne(entry => entry.Payment)
                .WithMany()
                .HasForeignKey(entry => entry.PaymentId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(entry => entry.StockLocation)
                .WithMany()
                .HasForeignKey(entry => entry.StockLocationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(entry => entry.Payout)
                .WithMany(payout => payout.Entries)
                .HasForeignKey(entry => entry.PayoutId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<PaymentPayout>(entity =>
        {
            entity.HasIndex(payout => payout.RequestId).IsUnique();
            entity.HasIndex(payout => new { payout.StockLocationId, payout.BusinessDate });
            entity.Property(payout => payout.Reference).HasMaxLength(100);
            entity.Property(payout => payout.Notes).HasMaxLength(2000);
            entity.HasOne(payout => payout.StockLocation)
                .WithMany()
                .HasForeignKey(payout => payout.StockLocationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(payout => payout.BankAccount)
                .WithMany()
                .HasForeignKey(payout => payout.BankAccountId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(payout => payout.BankMovement)
                .WithMany()
                .HasForeignKey(payout => payout.BankMovementId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(payout => payout.Expense)
                .WithMany()
                .HasForeignKey(payout => payout.ExpenseId)
                .OnDelete(DeleteBehavior.Restrict);
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

        builder.Entity<StockTransfer>(entity =>
        {
            entity.HasIndex(transfer => transfer.RequestId).IsUnique();
            entity.HasIndex(transfer => transfer.OccurredAt);
            entity.HasIndex(transfer => new { transfer.SourceLocationId, transfer.OccurredAt });
            entity.HasIndex(transfer => new { transfer.DestinationLocationId, transfer.OccurredAt });
            entity.Property(transfer => transfer.Notes).HasMaxLength(2000);
            entity.Property(transfer => transfer.VoidReason).HasMaxLength(2000);
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
            entity.Property(entry => entry.VoidReason).HasMaxLength(2000);
        });
        builder.Entity<RolePermission>(entity =>
        {
            entity.HasIndex(permission => new { permission.RoleName, permission.PolicyName }).IsUnique();
            entity.Property(permission => permission.RoleName).HasMaxLength(64).IsRequired();
            entity.Property(permission => permission.PolicyName).HasMaxLength(64).IsRequired();
        });
    }
}
