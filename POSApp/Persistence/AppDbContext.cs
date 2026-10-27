using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence.Entities;
using System.Text.Json;

namespace POSApp.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) 
    : IdentityDbContext<User, IdentityRole<int>, int>(options)
{
    public DbSet<ImportJob> ImportJobs { get; set; } = null!;
    public DbSet<EditorDraft> EditorDrafts { get; set; } = null!;
    public DbSet<StockAdjustment> StockAdjustments { get; set; } = null!;
    public DbSet<StockAdjustmentItem> StockAdjustmentItems { get; set; } = null!;
    public DbSet<StockTake> StockTakes { get; set; } = null!;
    public DbSet<StockTakeItem> StockTakeItems { get; set; } = null!;
    public DbSet<Lender> Lenders { get; set; } = null!;
    public DbSet<LenderBalance> LenderBalances { get; set; } = null!;
    public DbSet<DebtEntry> DebtEntries { get; set; } = null!;
    public DbSet<WorkingDay> WorkingDays { get; set; } = null!;
    public DbSet<DayFinance> DayFinances { get; set; } = null!;
    public DbSet<Employee> Employees { get; set; } = null!;
    public DbSet<MonthlySalary> MonthlySalaries { get; set; } = null!;
    public DbSet<BankAccount> BankAccounts { get; set; } = null!;
    public DbSet<BankMovement> BankMovements { get; set; } = null!;
    public DbSet<FinanceClosing> FinanceClosings { get; set; } = null!;
    public DbSet<StockTransfer> StockTransfers { get; set; } = null!;
    public DbSet<StockTransferItem> StockTransferItems { get; set; } = null!;
    public DbSet<CashTransfer> CashTransfers { get; set; } = null!;
    public DbSet<CashDrawer> CashDrawers { get; set; } = null!;
    public DbSet<CashSession> CashSessions { get; set; } = null!;
    public DbSet<CashMovement> CashMovements { get; set; } = null!;
    public DbSet<Expense> Expenses { get; set; } = null!;
    public DbSet<ExpenseCategoryDefinition> ExpenseCategories { get; set; } = null!;
    public DbSet<FinancialEntryBatch> FinancialEntryBatches { get; set; } = null!;
    public DbSet<StaffAccount> StaffAccounts { get; set; } = null!;
    public DbSet<PayrollEntry> PayrollEntries { get; set; } = null!;
    public DbSet<RecordChange> RecordChanges { get; set; } = null!;
    public DbSet<Category> Categories { get; set; } = null!;
    public DbSet<ProductAttributeDefinition> ProductAttributeDefinitions { get; set; } = null!;
    public DbSet<ProductAttributeOption> ProductAttributeOptions { get; set; } = null!;
    public DbSet<ProductAttributeValue> ProductAttributeValues { get; set; } = null!;
    public DbSet<Product> Products { get; set; } = null!;
    public DbSet<ProductCostHistory> ProductCostHistories { get; set; } = null!;
    public DbSet<Customer> Customers { get; set; } = null!;
    public DbSet<Supplier> Suppliers { get; set; } = null!;
    public DbSet<StockLocation> StockLocations { get; set; } = null!;
    public DbSet<InventoryBalance> InventoryBalances { get; set; } = null!;
    public DbSet<InventoryMovement> InventoryMovements { get; set; } = null!;
    public DbSet<Sale> Sales { get; set; } = null!;
    public DbSet<SaleDraftPayment> SaleDraftPayments { get; set; } = null!;
    public DbSet<SaleItem> SaleItems { get; set; } = null!;
    public DbSet<Payment> Payments { get; set; } = null!;
    public DbSet<Purchase> Purchases { get; set; } = null!;
    public DbSet<PurchaseItem> PurchaseItems { get; set; } = null!;
    public DbSet<SupplierPayment> SupplierPayments { get; set; } = null!;
    public DbSet<CustomerBalance> CustomerBalances { get; set; } = null!;
    public DbSet<SupplierBalance> SupplierBalances { get; set; } = null!;
    public DbSet<CustomerBalanceEntry> CustomerBalanceEntries { get; set; } = null!;
    public DbSet<SupplierBalanceEntry> SupplierBalanceEntries { get; set; } = null!;

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        CaptureFinancialCorrections();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        CaptureFinancialCorrections();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void CaptureFinancialCorrections()
    {
        ChangeTracker.DetectChanges();
        var corrections = ChangeTracker.Entries<AuditableEntity>()
            .Where(entry => entry.State is EntityState.Modified or EntityState.Deleted
                && entry.Entity is Expense or SupplierPayment or Payment or PayrollEntry)
            .ToArray();
        foreach (var entry in corrections)
        {
            var batchKind = entry.Entity switch
            {
                Expense => "expenses",
                SupplierPayment => "supplier-payments",
                Payment => "customer-payments",
                _ => "payroll"
            };
            if (RecordChanges.Local.Any(change => change.RecordType == batchKind
                && change.RecordId == entry.Entity.Id && Entry(change).State == EntityState.Added))
            {
                continue;
            }
            var properties = entry.Properties.Where(property => !property.Metadata.IsShadowProperty()).ToArray();
            var before = properties.ToDictionary(property => property.Metadata.Name, property => property.OriginalValue);
            var after = entry.State == EntityState.Deleted ? null
                : properties.ToDictionary(property => property.Metadata.Name, property => property.CurrentValue);
            RecordChanges.Add(new RecordChange
            {
                RequestId = Guid.NewGuid(),
                RecordType = entry.Entity switch
                {
                    Expense => "expense",
                    SupplierPayment => "supplierPayment",
                    Payment => "payment",
                    _ => "payroll"
                },
                RecordId = entry.Entity.Id,
                Action = entry.State == EntityState.Deleted ? "Remove" : "Correct",
                Reason = "Financial entry correction; original and replacement movement history retained",
                Before = JsonSerializer.Serialize(before),
                After = JsonSerializer.Serialize(after),
                CreatedByUserId = entry.Entity.UpdatedByUserId ?? entry.Entity.CreatedByUserId
            });
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        base.ConfigureConventions(builder);
        builder.Properties<decimal>().HavePrecision(18, 2);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        PosModelConfiguration.Configure(builder);
    }
}
