namespace POSApp.Client.DTOs;

public record CategoryOption(int Id, string Name);

public record CategoryPerformanceRow(
    int CategoryId,
    string CategoryName,
    int ProductCount,
    decimal UnitsSold,
    decimal TotalRevenue,
    decimal TotalCost,
    decimal GrossProfit,
    decimal MarginPercent,
    decimal RevenueSharePercent,
    string TopProductName);

public record CategoryPerformanceReportData(
    DateOnly StartDate,
    DateOnly EndDate,
    int? LocationId,
    decimal TotalRevenue,
    decimal TotalCost,
    decimal TotalProfit,
    decimal OverallMarginPercent,
    IReadOnlyList<StockLocationOption> Locations,
    IReadOnlyList<CategoryPerformanceRow> Categories);

public record SupplierSalesRow(
    int SupplierId,
    string SupplierName,
    int ProductsSoldDistinct,
    decimal UnitsSold,
    decimal TotalRevenue,
    decimal TotalCost,
    decimal GrossProfit,
    decimal MarginPercent,
    decimal CurrentStockUnits,
    decimal CurrentStockValue);

public record SupplierSalesPerformanceReportData(
    DateOnly StartDate,
    DateOnly EndDate,
    int? LocationId,
    decimal TotalRevenue,
    decimal TotalProfit,
    decimal TotalStockValue,
    IReadOnlyList<StockLocationOption> Locations,
    IReadOnlyList<SupplierSalesRow> Suppliers);

public enum StockVelocityTier
{
    Fast,
    Normal,
    Slow,
    Dead
}

public record InventoryVelocityRow(
    int ProductId,
    string ProductName,
    string? Barcode,
    string CategoryName,
    string SupplierName,
    decimal QuantityOnHand,
    decimal CostPrice,
    decimal SellingPrice,
    decimal TiedUpCapital,
    DateOnly? LastSoldDate,
    int DaysSinceLastSale,
    decimal UnitsSoldPeriod,
    StockVelocityTier Tier);

public record InventoryVelocityReportData(
    int DaysThreshold,
    int? LocationId,
    int? CategoryId,
    decimal TotalDeadStockCapital,
    int DeadStockItemsCount,
    int FastMoversCount,
    IReadOnlyList<StockLocationOption> Locations,
    IReadOnlyList<CategoryOption> Categories,
    IReadOnlyList<InventoryVelocityRow> Items);

public enum MarginQuadrant
{
    Star,
    CashCow,
    Opportunity,
    Laggard
}

public record MarginMatrixRow(
    int ProductId,
    string ProductName,
    string CategoryName,
    decimal UnitsSold,
    decimal TotalRevenue,
    decimal TotalCost,
    decimal TotalProfit,
    decimal MarginPercent,
    MarginQuadrant Quadrant);

public record MarginMatrixReportData(
    DateOnly StartDate,
    DateOnly EndDate,
    int? LocationId,
    decimal MedianVolume,
    decimal MedianMargin,
    int StarsCount,
    int CashCowsCount,
    int OpportunitiesCount,
    int LaggardsCount,
    IReadOnlyList<StockLocationOption> Locations,
    IReadOnlyList<MarginMatrixRow> Items);

public record SalesTimelinePoint(
    DateOnly Date,
    decimal SalesRevenue,
    decimal GrossProfit,
    int OrdersCount);

public record SalesTimelineReportData(
    IReadOnlyList<SalesTimelinePoint> Points);
