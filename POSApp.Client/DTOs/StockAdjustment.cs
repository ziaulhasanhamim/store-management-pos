namespace POSApp.Client.DTOs;

public sealed record StockAdjustmentOptions(IReadOnlyList<Lookup> Locations, int? LocationId, DateOnly Date)
{
    public IReadOnlyList<Lookup> Suppliers { get; init; } = [];
    public IReadOnlyList<Lookup> Categories { get; init; } = [];
    public IReadOnlyList<ProductAttributeDefinitionOption> Attributes { get; init; } = [];
}

public sealed record StockAdjustmentProduct(int Id, string Name, string? Barcode, string Unit,
    bool AllowFractionUnit, decimal Cost)
{
    public IReadOnlyList<ItemAttributeDisplay> Attributes { get; init; } = [];
}

public sealed record StockAdjustmentEntry(int ProductId, decimal Quantity, decimal UnitCost);

public sealed record StockAdjustmentLine(StockAdjustmentProduct Product, decimal Quantity, decimal UnitCost);

public sealed record StockAdjustmentCommand(Guid RequestId, int LocationId, DateOnly Date,
    string Notes, IReadOnlyList<StockAdjustmentEntry> Items);

public sealed record StockAdjustmentResult(int Id, int ItemCount, decimal Value);

public sealed record StockAdjustmentHistory(int Id, string Location, DateOnly Date, int ItemCount, decimal Value);

public sealed record StockAdjustmentDetail(int Id, string Location, DateOnly Date, string Notes,
    DateTimeOffset CreatedAt, string CreatedBy, IReadOnlyList<StockAdjustmentLine> Items);

public sealed record StockAdjustmentDraft(string Version, Guid RequestId, int? LocationId, DateOnly Date,
    string Notes, IReadOnlyList<StockAdjustmentLine> Items);

public sealed class StockAdjustmentImportRow
{
    public required int ProductId { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal UnitCost { get; init; }
}
