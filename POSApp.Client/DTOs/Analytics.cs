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

public enum ProductTrendStatus
{
    Stable = 0,
    RisingToStar = 1,
    ImprovingMargin = 2,
    DecliningVolume = 3,
    SlippingToLaggard = 4,
    NewOrInsufficientData = 5
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
    MarginQuadrant Quadrant,
    decimal ForecastUnitsSold = 0m,
    decimal ForecastMarginPercent = 0m,
    ProductTrendStatus TrendStatus = ProductTrendStatus.Stable,
    MarginQuadrant? ForecastQuadrant = null,
    float ClusterDistance = 0f);

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
    IReadOnlyList<MarginMatrixRow> Items,
    bool IsMlPowered = false,
    bool IsFallback = false,
    string AnalysisMode = "ai");

public record SalesTimelinePoint(
    DateOnly Date,
    decimal SalesRevenue,
    decimal GrossProfit,
    int OrdersCount)
{
    public decimal MarginPercentage => SalesRevenue > 0
        ? decimal.Round(GrossProfit / SalesRevenue * 100m, 1, MidpointRounding.AwayFromZero)
        : 0m;
}

public record SalesTimelineReportData(
    IReadOnlyList<SalesTimelinePoint> Points,
    int ClosedDaysExcludedCount = 0,
    IReadOnlyList<DateOnly>? ClosedDays = null);
