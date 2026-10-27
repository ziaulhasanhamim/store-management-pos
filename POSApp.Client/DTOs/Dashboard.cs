using POSApp.Client.Enums;

namespace POSApp.Client.DTOs;

public sealed record DashboardData(
    int Items,
    decimal Stock,
    int OutOfStock,
    decimal? StockValue,
    decimal? SupplierDue,
    IReadOnlyList<ReceiptRow> Receipts)
{
    public decimal TodaySales { get; init; }
    public int TodayOrdersCount { get; init; }
    public decimal? TodayProfit { get; init; }
    public decimal OfficeCash { get; init; }
    public decimal? BankBalance { get; init; }
    public int OpenDrawersCount { get; init; }
    public int TotalDrawersCount { get; init; }
    public DateOnly BusinessDate { get; init; }
    public string? LocationName { get; init; }
    public IReadOnlyList<RecentSaleRow> RecentSales { get; init; } = [];
    public IReadOnlyList<StockAlertItem> StockAlerts { get; init; } = [];
}

public sealed record RecentSaleRow(
    int Id,
    string ReceiptNumber,
    DateTimeOffset SoldAt,
    string Cashier,
    string Customer,
    decimal Total,
    PaymentMethod PaymentMethod,
    int ItemCount);

public sealed record StockAlertItem(
    int Id,
    string Name,
    string? Barcode,
    decimal Stock,
    string Unit,
    string Supplier);
