namespace POSApp.Persistence.Entities;

public sealed class StockAdjustment : AuditableEntity
{
    public Guid RequestId { get; set; }
    public int StockLocationId { get; set; }
    public StockLocation? StockLocation { get; set; }
    public DateOnly Date { get; set; }
    public string Notes { get; set; } = "";
    public ICollection<StockAdjustmentItem> Items { get; set; } = [];
}

public sealed class StockAdjustmentItem : Entity
{
    public int StockAdjustmentId { get; set; }
    public StockAdjustment? StockAdjustment { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public required string ProductName { get; set; }
    public string? Barcode { get; set; }
    public required string Unit { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
}
