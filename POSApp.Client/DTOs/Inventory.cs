using POSApp.Client.Enums;

namespace POSApp.Client.DTOs;

public sealed record StockEvent(
    int Id,
    DateTimeOffset At,
    InventoryMovementType Type,
    decimal Quantity,
    decimal? Cost,
    string Location,
    string? Receipt,
    string? Notes);

public sealed record LocationInput(string Name, bool CanSell = true);
public sealed record LocationRow(int Id, string Name, bool CanSell);
public sealed class StockTransferInput
{
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public int SourceLocationId { get; set; }
    public int DestinationLocationId { get; set; }
    public int ProductId { get; set; }
    public decimal Quantity { get; set; }
    public string Notes { get; set; } = "";
}
public sealed record TransferRow(int Id, DateTimeOffset At, string Product, string Source, string Destination, decimal Quantity, string Notes);

public sealed record TransferLine(int ProductId, decimal Quantity);
public sealed record TransferCommand(Guid RequestId, int SourceLocationId, int DestinationLocationId,
    IReadOnlyList<TransferLine> Lines, string Notes)
{
    public DateOnly? WorkingDate { get; init; }
}
public sealed record TransferResult(int Id, int ItemCount);

public sealed record ProductStockLocation(int Id, string Name, decimal Quantity, bool IsDeleted);

public sealed record InventoryValueRow(
    int Id,
    string Name,
    string? Barcode,
    string Unit,
    string Supplier,
    decimal Quantity,
    decimal UnitCost,
    decimal Value,
    bool Archived);

public sealed record InventoryValueReport(
    IReadOnlyList<InventoryValueRow> Rows,
    int Count,
    int Page,
    decimal TotalValue,
    decimal? SupplierDue,
    IReadOnlyList<Lookup> Suppliers,
    IReadOnlyList<Lookup> Locations);
