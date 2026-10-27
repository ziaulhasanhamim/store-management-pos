namespace POSApp.Persistence.Entities;

public enum SaleStatus
{
    Draft = 1,
    Completed = 2,
    Voided = 3
}

public sealed class Sale : SoftDeletableEntity
{
    public long Revision { get; set; }
    public string? RequestHash { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public required int StockLocationId { get; set; }
    public StockLocation? StockLocation { get; set; }
    public required int CashierId { get; set; }
    public User? Cashier { get; set; }
    // Null supports walk-in customers.
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public SaleStatus Status { get; set; } = SaleStatus.Draft;
    public DateTimeOffset SoldAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DueAt { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal TenderedAmount { get; set; }
    public decimal DraftPaymentAmount { get; set; }
    public PaymentMethod DraftPaymentMethod { get; set; } = PaymentMethod.Cash;
    public string? Notes { get; set; }
    public ICollection<SaleDraftPayment> DraftPayments { get; set; } = [];
    public ICollection<SaleItem> Items { get; set; } = [];
    public ICollection<Payment> Payments { get; set; } = [];
}

public sealed class SaleItem : Entity
{
    public required int SaleId { get; set; }
    public Sale? Sale { get; set; }
    public required int ProductId { get; set; }
    public Product? Product { get; set; }
    // Snapshot fields preserve receipt details when the catalog changes.
    public string ProductName { get; set; } = string.Empty;
    public string ProductSku { get; set; } = string.Empty;
    public string Unit { get; set; } = "piece";
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal UnitCost { get; set; }
    public decimal UnitDiscount { get; set; }
    public decimal TotalAmount { get; private set; }
}



public sealed class Payment : AuditableEntity
{
    public string? RequestHash { get; set; }
    // Invoice is optional for collections against opening or accumulated customer dues.
    public int? SaleId { get; set; }
    public Sale? Sale { get; set; }
    public int? CustomerBalanceId { get; set; }
    public CustomerBalance? CustomerBalance { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public decimal TenderedAmount { get; set; }
    // This is a settled payment, including payments collected after checkout.
    public DateTimeOffset PaidAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Reference { get; set; }
    public string? Notes { get; set; }
}

public sealed class SaleDraftPayment : Entity
{
    public required int SaleId { get; set; }
    public Sale? Sale { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
}
