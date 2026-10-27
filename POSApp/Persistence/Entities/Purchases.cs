namespace POSApp.Persistence.Entities;

public enum PurchaseStatus
{
    Draft = 1,
    Ordered = 2,
    Received = 3,
    Cancelled = 4
}

public sealed class Purchase : SoftDeletableEntity
{
    public string? RequestHash { get; set; }
    public string PurchaseNumber { get; set; } = string.Empty;
    public string? SupplierInvoiceNumber { get; set; }
    public required int StockLocationId { get; set; }
    public StockLocation? StockLocation { get; set; }
    public required int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public PurchaseStatus Status { get; set; } = PurchaseStatus.Draft;
    public DateTimeOffset OrderedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReceivedAt { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Notes { get; set; }
    public ICollection<PurchaseItem> Items { get; set; } = [];
    public ICollection<SupplierPayment> Payments { get; set; } = [];
}

// Payments settle the supplier account, including opening dues; invoice linkage is optional.
public sealed class SupplierPayment : AuditableEntity
{
    public string? RequestHash { get; set; }
    public required int SupplierBalanceId { get; set; }
    public SupplierBalance? SupplierBalance { get; set; }
    public int? PurchaseId { get; set; }
    public Purchase? Purchase { get; set; }
    public PaymentMethod Method { get; set; }
    public required decimal Amount { get; set; }
    public DateTimeOffset PaidAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Reference { get; set; }
    public string? Notes { get; set; }
}

public sealed class PurchaseItem : Entity
{
    public required int PurchaseId { get; set; }
    public Purchase? Purchase { get; set; }
    public required int ProductId { get; set; }
    public Product? Product { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductSku { get; set; } = string.Empty;
    public string Unit { get; set; } = "piece";
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalAmount { get; set; }
}
