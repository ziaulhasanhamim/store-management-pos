namespace POSApp.Persistence.Entities;

public sealed class Lender : AuditableEntity
{
    public Guid RequestId { get; set; }
    public required string Name { get; set; }
    public string NormalizedName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public LenderBalance? Balance { get; set; }
}

public sealed class LenderBalance : AuditableEntity
{
    public int LenderId { get; set; }
    public Lender? Lender { get; set; }
    public DateOnly OpeningDate { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal CurrentBalance { get; set; }
    public ICollection<DebtEntry> Entries { get; set; } = [];
}

public sealed class DebtEntry : AuditableEntity
{
    public Guid RequestId { get; set; }
    public int LenderBalanceId { get; set; }
    public LenderBalance? LenderBalance { get; set; }
    public DateOnly BusinessDate { get; set; }
    public DebtEntryKind Kind { get; set; }
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public PaymentMethod? Method { get; set; }
    public int? CashLocationId { get; set; }
    public StockLocation? CashLocation { get; set; }
    public required string Notes { get; set; }
}
