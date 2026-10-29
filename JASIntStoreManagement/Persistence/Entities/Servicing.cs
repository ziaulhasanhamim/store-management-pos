namespace JASIntStoreManagement.Persistence.Entities;

public sealed class Technician : SoftDeletableEntity
{
    public required string Name { get; set; }
    public string NormalizedName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public decimal DefaultCommissionRate { get; set; } = 50m;
    public bool IsArchived { get; set; }
    public int LocationId { get; set; }
    public StockLocation? Location { get; set; }
    public ICollection<MobileServicingEntry> ServicingEntries { get; set; } = [];
}

public sealed class MobileServicingEntry : AuditableEntity
{
    public DateOnly Date { get; set; }
    public int LocationId { get; set; }
    public StockLocation? Location { get; set; }
    public int TechnicianId { get; set; }
    public Technician? Technician { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal TotalCost { get; set; }
    public decimal CommissionRate { get; set; } = 50m;
    public decimal TechnicianCut { get; set; }
    public decimal ShopProfit { get; set; }
    public bool IsDeposited { get; set; }
    public DateTimeOffset? DepositedAt { get; set; }
    public int? CashMovementId { get; set; }
    public CashMovement? CashMovement { get; set; }
    public string? Notes { get; set; }
}
