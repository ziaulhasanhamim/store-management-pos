namespace POSApp.Client.DTOs;

public sealed record NonTradingDayDto(
    int Id,
    DateOnly Date,
    int? StockLocationId,
    string? LocationName,
    string? Reason,
    string MarkedByName,
    DateTimeOffset CreatedAt);

public sealed record MarkNonTradingDayInput
{
    public DateOnly Date { get; set; }
    public int? StockLocationId { get; set; }
    public string? Reason { get; set; }
}

public sealed record TradingCalendarPageData(
    IReadOnlyList<NonTradingDayDto> Closures,
    IReadOnlyList<StockLocationOption> Locations);
