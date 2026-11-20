namespace POSApp.Client.DTOs;

public enum StockIntelligenceSegment
{
    CapitalCompounder = 1, // High GMROI / profit per investment Tk
    CashFlowAnchor = 2,     // High sales velocity & frequency; must-have staple
    RareGem = 3,            // Profitable slow mover: high unit profit, maintain small buffer
    StandardActive = 4,     // Regular performing item
    DeadStockTrap = 5       // Trapped working capital: low velocity, low margin, candidate for liquidation
}

public enum StockReorderUrgency
{
    Critical = 1,   // Out of stock or run-out before supplier lead time
    OrderSoon = 2,  // Stock below target buffer
    Healthy = 3,    // Adequate inventory on hand
    Overstocked = 4,// Inventory far exceeds demand cycle
    Liquidate = 5   // Dead stock: do not reorder, clear out
}

public sealed record StockIntelligenceRow(
    int ProductId,
    string ProductName,
    string? Barcode,
    string CategoryName,
    string SupplierName,
    decimal CurrentStock,
    decimal CostPrice,
    decimal RealizedAvgPrice,
    decimal RealizedMarginPercent,
    decimal RealizedUnitProfit,
    decimal TotalUnitsSold,
    decimal TotalRevenue,
    decimal TotalGrossProfit,
    decimal DailyVelocity,
    decimal DaysOfSupply,
    int DaysSinceLastSale,
    decimal TiedUpCapital,
    decimal Gmroi,
    decimal ProfitPerInvestmentTk,
    StockIntelligenceSegment Segment,
    StockReorderUrgency ReorderUrgency,
    decimal RecommendedOrderQty,
    decimal EstimatedOrderCost,
    decimal ExpectedProfitReturn,
    bool IsDeadStock,
    bool? UserDeadStockOverride,
    float DeadStockProbability,
    int LeadTimeDays);

public sealed record StockIntelligenceReportData(
    decimal TotalInventoryInvestment,
    decimal UrgentReorderValue,
    int UrgentReorderSkuCount,
    decimal TrappedDeadStockValue,
    int DeadStockSkuCount,
    int CashFlowAnchorCount,
    int CapitalCompounderCount,
    int RareGemCount,
    DateTimeOffset ModelTrainedAt,
    bool IsCached,
    IReadOnlyList<StockIntelligenceRow> Items,
    IReadOnlyList<StockLocationOption> Locations,
    IReadOnlyList<CategoryOption> Categories);
