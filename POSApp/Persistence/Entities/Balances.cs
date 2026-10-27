namespace POSApp.Persistence.Entities;

public enum BalanceEntryType
{
    OpeningBalance = 1,
    Invoice = 2,
    Payment = 3,
    Adjustment = 4
}

public abstract class AccountBalance : AuditableEntity
{
    public decimal OpeningBalance { get; set; }
    public DateTimeOffset? OpenedAt { get; set; }
    // Positive: outstanding due. Negative: an advance/credit balance.
    public decimal CurrentBalance { get; set; }
}

public sealed class CustomerBalance : AccountBalance
{
    public required int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public ICollection<CustomerBalanceEntry> Entries { get; set; } = [];
    public ICollection<Payment> Payments { get; set; } = [];
}

public sealed class SupplierBalance : AccountBalance
{
    public required int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public ICollection<SupplierBalanceEntry> Entries { get; set; } = [];
    public ICollection<SupplierPayment> Payments { get; set; } = [];
}

public sealed class CustomerBalanceEntry : AuditableEntity
{
    public string? RequestHash { get; set; }
    public required int CustomerBalanceId { get; set; }
    public CustomerBalance? CustomerBalance { get; set; }
    public Guid? RequestId { get; set; }
    public BalanceEntryType Type { get; set; }
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? Notes { get; set; }
    public int? SaleId { get; set; }
    public Sale? Sale { get; set; }
    public int? PaymentId { get; set; }
    public Payment? Payment { get; set; }
}

public sealed class SupplierBalanceEntry : AuditableEntity
{
    public string? RequestHash { get; set; }
    public required int SupplierBalanceId { get; set; }
    public SupplierBalance? SupplierBalance { get; set; }
    public Guid? RequestId { get; set; }
    public BalanceEntryType Type { get; set; }
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? Notes { get; set; }
    public int? PurchaseId { get; set; }
    public Purchase? Purchase { get; set; }
    public int? SupplierPaymentId { get; set; }
    public SupplierPayment? SupplierPayment { get; set; }
}
