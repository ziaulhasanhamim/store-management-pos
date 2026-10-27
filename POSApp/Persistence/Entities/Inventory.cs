namespace POSApp.Persistence.Entities;

public sealed class StockLocation : SoftDeletableEntity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string NormalizedName { get; set; } = string.Empty;
    public bool CanSell { get; set; } = true;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public ICollection<InventoryBalance> InventoryBalances { get; set; } = [];
}

public sealed class InventoryBalance : AuditableEntity
{
    public required int StockLocationId { get; set; }
    public StockLocation? StockLocation { get; set; }
    public required int ProductId { get; set; }
    public Product? Product { get; set; }
    public required decimal QuantityOnHand { get; set; }
}



public sealed class InventoryMovement : AuditableEntity
{
    public required int StockLocationId { get; set; }
    public StockLocation? StockLocation { get; set; }
    public required int ProductId { get; set; }
    public Product? Product { get; set; }
    public InventoryMovementType Type { get; set; }
    public required decimal Quantity { get; set; }
    public required decimal UnitCost { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Notes { get; set; }
    public int? StockAdjustmentItemId { get; set; }
    public StockAdjustmentItem? StockAdjustmentItem { get; set; }
    public int? StockTakeItemId { get; set; }
    public StockTakeItem? StockTakeItem { get; set; }
    public int? StockTransferId { get; set; }
    public StockTransfer? StockTransfer { get; set; }
    public int? SaleItemId { get; set; }
    public SaleItem? SaleItem { get; set; }
    public int? PurchaseItemId { get; set; }
    public PurchaseItem? PurchaseItem { get; set; }
}

public sealed class StockTransfer : AuditableEntity
{
    public Guid RequestId { get; set; }
    public int SourceLocationId { get; set; }
    public StockLocation? SourceLocation { get; set; }
    public int DestinationLocationId { get; set; }
    public StockLocation? DestinationLocation { get; set; }
    public ICollection<StockTransferItem> Items { get; set; } = [];
    public DateTimeOffset OccurredAt { get; set; }
    public required string Notes { get; set; }
}

public sealed class StockTransferItem : Entity
{
    public int StockTransferId { get; set; }
    public StockTransfer? StockTransfer { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
}
