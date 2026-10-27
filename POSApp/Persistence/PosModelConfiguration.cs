using Microsoft.EntityFrameworkCore;
using POSApp.Persistence.Entities;

namespace POSApp.Persistence;

internal static class PosModelConfiguration
{
    public static void Configure(ModelBuilder builder)
    {
        ConfigureNormalizedName<Category>(builder);
        ConfigureNormalizedName<Customer>(builder);
        ConfigureNormalizedName<Employee>(builder);
        ConfigureNormalizedName<ExpenseCategoryDefinition>(builder);
        ConfigureNormalizedName<Lender>(builder);
        ConfigureNormalizedName<Product>(builder);
        ConfigureNormalizedName<StockLocation>(builder);
        ConfigureNormalizedName<Supplier>(builder);
        FinanceModelConfiguration.Configure(builder);
        builder.Entity<Supplier>().HasIndex(supplier => supplier.LegacyId).IsUnique();
        builder.Entity<ImportJob>(entity =>
        {
            entity.HasKey(job => job.Id);
            entity.Property(job => job.Kind).HasMaxLength(32);
            entity.Property(job => job.ActiveKey).HasMaxLength(32);
            entity.Property(job => job.Fingerprint).HasMaxLength(64);
            entity.Property(job => job.FileName).HasMaxLength(255);
            entity.Property(job => job.Status).HasMaxLength(16);
            entity.Property(job => job.Message).HasMaxLength(2000);
            entity.HasIndex(job => job.ActiveKey).IsUnique();
            entity.HasIndex(job => new { job.Kind, job.CreatedAt });
            entity.HasOne(job => job.User).WithMany().HasForeignKey(job => job.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<EditorDraft>(entity =>
        {
            entity.HasIndex(draft => new { draft.UserId, draft.Kind }).IsUnique();
            entity.Property(draft => draft.Kind).HasMaxLength(100);
            entity.Property(draft => draft.Revision).IsConcurrencyToken();
        });
        builder.Entity<StockAdjustment>(entity =>
        {
            entity.HasIndex(opening => opening.RequestId).IsUnique();
            entity.HasIndex(opening => new { opening.StockLocationId, opening.Date });
            entity.Property(opening => opening.Notes).HasMaxLength(2000);
        });
        builder.Entity<StockAdjustmentItem>(entity =>
        {
            entity.HasIndex(item => new { item.StockAdjustmentId, item.ProductId }).IsUnique();
            entity.Property(item => item.Quantity).HasPrecision(18, 3);
            entity.Property(item => item.ProductName).HasMaxLength(200);
            entity.Property(item => item.Barcode).HasMaxLength(64);
            entity.Property(item => item.Unit).HasMaxLength(32);
        });
        builder.Entity<InventoryMovement>().HasIndex(move => move.StockAdjustmentItemId).IsUnique();
        builder.Entity<StockTake>(entity =>
        {
            entity.HasIndex(take => take.RequestId).IsUnique();
            entity.HasIndex(take => new { take.StockLocationId, take.BusinessDate });
            entity.Property(take => take.Reason).HasMaxLength(2000);
            entity.Property(take => take.Revision).IsConcurrencyToken();
        });
        builder.Entity<StockTakeItem>(entity =>
        {
            entity.HasIndex(item => new { item.StockTakeId, item.ProductId }).IsUnique();
            entity.Property(item => item.ExpectedQuantity).HasPrecision(18, 3);
            entity.Property(item => item.CountedQuantity).HasPrecision(18, 3);
            entity.Property(item => item.ProductName).HasMaxLength(200);
            entity.Property(item => item.Barcode).HasMaxLength(64);
            entity.Property(item => item.Unit).HasMaxLength(32);
        });
        builder.Entity<InventoryMovement>().HasIndex(move => move.StockTakeItemId).IsUnique();
        builder.Entity<RecordChange>(entity =>
        {
            entity.HasIndex(change => change.RequestId).IsUnique();
            entity.HasIndex(change => new { change.RecordType, change.RecordId, change.Id });
            entity.Property(change => change.RecordType).HasMaxLength(64);
            entity.Property(change => change.Action).HasMaxLength(32);
            entity.Property(change => change.Reason).HasMaxLength(2000);
        });
        builder.Entity<Category>(entity =>
        {
            entity.HasIndex(x => x.Name).IsUnique();
        });
        builder.Entity<ProductAttributeDefinition>(entity =>
        {
            entity.Property(attribute => attribute.Key).HasMaxLength(80);
            entity.Property(attribute => attribute.DisplayName).HasMaxLength(120);
            entity.HasIndex(attribute => attribute.Key).IsUnique();
        });
        builder.Entity<ProductAttributeOption>(entity =>
        {
            entity.Property(option => option.Value).HasMaxLength(120);
            entity.Property(option => option.DisplayName).HasMaxLength(120);
            entity.HasIndex(option => new { option.ProductAttributeDefinitionId, option.Value }).IsUnique();
            entity.HasOne(option => option.Definition).WithMany(definition => definition.Options)
                .HasForeignKey(option => option.ProductAttributeDefinitionId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<ProductAttributeValue>(entity =>
        {
            entity.Property(value => value.Value).HasMaxLength(2000);
            entity.Property(value => value.DisplayValue).HasMaxLength(2000);
            entity.HasIndex(value => new { value.ProductId, value.ProductAttributeDefinitionId }).IsUnique();
            entity.HasOne(value => value.Product).WithMany(product => product.AttributeValues)
                .HasForeignKey(value => value.ProductId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(value => value.Definition).WithMany(definition => definition.Values)
                .HasForeignKey(value => value.ProductAttributeDefinitionId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<Product>(entity =>
        {
            entity.HasIndex(x => x.Barcode).IsUnique();
            entity.Property(x => x.Barcode).HasMaxLength(64);
            entity.Property(x => x.Unit).HasMaxLength(32);
            entity.Property(x => x.AllowFractionUnit).HasDefaultValue(false);
        });
        builder.Entity<ProductCostHistory>(entity =>
        {
            entity.HasIndex(h => new { h.ProductId, h.CreatedAt });
            entity.Property(h => h.Reason).HasMaxLength(500);
            entity.Property(h => h.ReferenceType).HasMaxLength(64);
            entity.HasOne(h => h.Product).WithMany().HasForeignKey(h => h.ProductId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(h => h.CreatedByUser).WithMany().HasForeignKey(h => h.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<StockLocation>(entity =>
        {
            entity.Property(x => x.CanSell).HasDefaultValue(true);
            entity.Property(x => x.Code).HasMaxLength(32);
            entity.HasIndex(x => x.Code).IsUnique();
        });
        builder.Entity<InventoryBalance>(entity =>
        {
            entity.HasIndex(x => new { x.StockLocationId, x.ProductId })
                .IsUnique();
            entity.Property(x => x.QuantityOnHand).HasPrecision(18, 3);
        });
        builder.Entity<InventoryMovement>(entity =>
        {
            entity.HasIndex(x => new { x.StockLocationId, x.ProductId, x.OccurredAt });
            entity.Property(x => x.Quantity).HasPrecision(18, 3);
        });
        builder.Entity<Sale>(entity =>
        {
            entity.Property(x => x.ReceiptNumber).HasMaxLength(64);
            entity.HasIndex(x => x.ReceiptNumber).IsUnique();
            entity.HasIndex(x => x.SoldAt);
            entity.HasIndex(x => new { x.StockLocationId, x.SoldAt });
        });
        builder.Entity<Purchase>(entity =>
        {
            entity.Property(x => x.PurchaseNumber).HasMaxLength(64);
            entity.Property(x => x.SupplierInvoiceNumber).HasMaxLength(64);
            entity.HasIndex(x => x.PurchaseNumber).IsUnique();
            entity.HasIndex(x => new { x.SupplierId, x.SupplierInvoiceNumber });
            entity.HasIndex(x => new { x.StockLocationId, x.OrderedAt });
            entity.HasIndex(x => x.ReceivedAt);
            entity.HasIndex(x => new { x.StockLocationId, x.ReceivedAt });
        });
        builder.Entity<SaleItem>(entity =>
        {
            entity.Property(x => x.Quantity).HasPrecision(18, 3);
            entity.Property(x => x.UnitDiscount).HasPrecision(38, 20);
            entity.Property(x => x.TotalAmount).HasComputedColumnSql("""
                ("UnitPrice" - "UnitDiscount") * "Quantity"
                """, stored: true);
        });
        builder.Entity<PurchaseItem>().Property(x => x.Quantity).HasPrecision(18, 3);

        builder.Entity<Payment>(entity =>
        {
            entity.HasIndex(x => new { x.SaleId, x.PaidAt });
            entity.HasIndex(x => new { x.CustomerBalanceId, x.PaidAt });
            entity.Property(x => x.Reference).HasMaxLength(128);
            entity.HasIndex(x => x.Reference).IsUnique();
            entity.Property(x => x.Notes).HasMaxLength(2000);
        });
        builder.Entity<SupplierPayment>(entity =>
        {
            entity.HasIndex(x => new { x.PurchaseId, x.PaidAt });
            entity.HasIndex(x => new { x.SupplierBalanceId, x.PaidAt });
            entity.Property(x => x.Reference).HasMaxLength(128);
            entity.HasIndex(x => x.Reference).IsUnique();
            entity.Property(x => x.Notes).HasMaxLength(2000);
        });

        // Reference navigations on both sides and CustomerId/SupplierId let EF infer
        // one-to-one balance relationships and create unique foreign-key indexes.
        builder.Entity<CustomerBalanceEntry>(entity =>
        {
            entity.HasIndex(x => x.RequestId).IsUnique();
            entity.HasIndex(x => x.SaleId).IsUnique();
            entity.HasIndex(x => x.PaymentId).IsUnique();
            entity.Property(x => x.Notes).HasMaxLength(2000);
        });
        builder.Entity<SupplierBalanceEntry>(entity =>
        {
            entity.HasIndex(x => x.RequestId).IsUnique();
            entity.HasIndex(x => x.PurchaseId).IsUnique();
            entity.HasIndex(x => x.SupplierPaymentId).IsUnique();
            entity.Property(x => x.Notes).HasMaxLength(2000);
        });

        foreach (var entity in builder.Model.GetEntityTypes())
        {
            if (entity.FindProperty("RequestHash") is { } fingerprint)
            {
                fingerprint.SetMaxLength(64);
            }
        }
        builder.Entity<Sale>().Property(sale => sale.Revision).IsConcurrencyToken();

        // EF discovers relationships from navigation properties and matching <Navigation>Id fields.
        // Keep those conventions, changing only deletion behavior to protect financial history.
        foreach (var entity in builder.Model.GetEntityTypes().Where(x => typeof(Entity).IsAssignableFrom(x.ClrType)))
        {
            foreach (var foreignKey in entity.GetForeignKeys())
            {
                foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
            }
        }
    }

    private static void ConfigureNormalizedName<TEntity>(ModelBuilder builder)
        where TEntity : class
    {
        builder.Entity<TEntity>().Property<string>("NormalizedName")
            .HasMaxLength(256)
            .IsRequired();
        builder.Entity<TEntity>().HasIndex("NormalizedName");
    }
}
