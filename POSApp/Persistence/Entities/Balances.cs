using POSApp.Client.Enums;

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

public sealed class LenderBalance : AccountBalance
{
    public int LenderId { get; set; }
    public Lender? Lender { get; set; }
    public DateOnly OpeningDate { get; set; }
    public ICollection<DebtEntry> Entries { get; set; } = [];
}

public abstract class BalanceEntry : AuditableEntity
{
    public string? RequestHash { get; set; }
    public Guid? RequestId { get; set; }
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public string? Notes { get; set; }
}

public sealed class CustomerBalanceEntry : BalanceEntry
{
    public required int CustomerBalanceId { get; set; }
    public CustomerBalance? CustomerBalance { get; set; }
    public BalanceEntryType Type { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public int? SaleId { get; set; }
    public Sale? Sale { get; set; }
    public int? PaymentId { get; set; }
    public Payment? Payment { get; set; }
}

public sealed class SupplierBalanceEntry : BalanceEntry
{
    public required int SupplierBalanceId { get; set; }
    public SupplierBalance? SupplierBalance { get; set; }
    public BalanceEntryType Type { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public int? PurchaseId { get; set; }
    public Purchase? Purchase { get; set; }
    public int? SupplierPaymentId { get; set; }
    public SupplierPayment? SupplierPayment { get; set; }
}

public sealed class DebtEntry : BalanceEntry
{
    public int LenderBalanceId { get; set; }
    public LenderBalance? LenderBalance { get; set; }
    public DateOnly BusinessDate { get; set; }
    public DebtEntryKind Kind { get; set; }
    public PaymentMethod? Method { get; set; }
    public int? CashLocationId { get; set; }
    public StockLocation? CashLocation { get; set; }
    public bool IsVoided { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public int? VoidedByUserId { get; set; }
    public string? VoidReason { get; set; }
}
