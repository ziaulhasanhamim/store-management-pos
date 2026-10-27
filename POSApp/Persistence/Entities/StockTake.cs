namespace POSApp.Persistence.Entities;

public sealed class StockTake : AuditableEntity
{
    public Guid RequestId { get; set; }
    public int StockLocationId { get; set; }
    public StockLocation? StockLocation { get; set; }
    public DateOnly BusinessDate { get; set; }
    public long Revision { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset? CompletedAt { get; set; }
    public int? CompletedByUserId { get; set; }
    public Guid? CompletionRequestId { get; set; }
    public ICollection<StockTakeItem> Items { get; set; } = [];
}

public sealed class StockTakeItem : Entity
{
    public int StockTakeId { get; set; }
    public StockTake? StockTake { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public required string ProductName { get; set; }
    public string? Barcode { get; set; }
    public required string Unit { get; set; }
    public bool AllowFractionUnit { get; set; }
    public decimal ExpectedQuantity { get; set; }
    public decimal? CountedQuantity { get; set; }
    public bool IsIncluded { get; set; }
    public decimal UnitCost { get; set; }
    public int LastMovementId { get; set; }
}
