namespace POSApp.Client.DTOs;

public sealed record StatementRow(DateOnly Date, DateTimeOffset PostedAt, string Type, string Reference,
    string Notes, decimal Amount, decimal Balance);

public sealed record BalanceStatement(string Account, ReportRange Range, IReadOnlyList<Lookup> Drawers,
    int? DrawerId, decimal Opening, decimal MoneyIn, decimal MoneyOut, decimal Closing,
    decimal OwnerContributions, decimal OwnerWithdrawals, IReadOnlyList<StatementRow> Rows, int Count, int Page)
{
    public decimal PageOpening { get; init; }
    public decimal? PendingCountDifference { get; init; }
    public string? PendingCountNotes { get; init; }
    public DateTimeOffset? PendingCountAt { get; init; }
}

public sealed record StockStatementRow(
    DateOnly Date,
    DateTimeOffset OccurredAt,
    string Type,
    string Reference,
    string? ReferenceUrl,
    string LocationName,
    decimal QuantityChange,
    decimal RunningBalance,
    decimal UnitCost,
    decimal ValueChange,
    string? Notes);

public sealed record StockStatement(
    int ProductId,
    string ProductName,
    string? Sku,
    string Unit,
    int? LocationId,
    string? LocationName,
    ReportRange Range,
    decimal OpeningQuantity,
    decimal InQuantity,
    decimal OutQuantity,
    decimal ClosingQuantity,
    decimal CurrentCostPrice,
    decimal ClosingValue,
    IReadOnlyList<StockStatementRow> Rows,
    int TotalCount,
    int CurrentPage,
    IReadOnlyList<Lookup> AvailableProducts,
    IReadOnlyList<Lookup> AvailableLocations);
