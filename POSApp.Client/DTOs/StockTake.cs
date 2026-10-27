namespace POSApp.Client.DTOs;

public sealed class StockTakeStartInput
{
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public int LocationId { get; set; }
}

public sealed record StockTakeCount(int ProductId, decimal? Quantity)
{
    public StockTakeLine? Snapshot { get; init; }
}
public sealed record StockTakeSave(long Revision, string Reason, IReadOnlyList<StockTakeCount> Counts);
public sealed record StockTakeRefresh(long Revision);
public sealed record StockTakeApply(Guid RequestId, long Revision, bool Confirmed);

public sealed record StockTakeRow(int Id, string Location, DateOnly Date, int Counted, int Items,
    decimal VarianceValue, DateTimeOffset? CompletedAt);

public sealed record StockTakeLine(int ProductId, string Name, string? Barcode, string Unit,
    bool AllowFractionUnit, decimal Expected, decimal? Counted, decimal UnitCost)
{
    public int LastMovementId { get; init; }
    public decimal? Difference => Counted - Expected;
    public decimal? Value => Difference is { } difference
        ? decimal.Round(difference * UnitCost, 2, MidpointRounding.AwayFromZero) : null;
}

public sealed record StockTakePage(int Id, int LocationId, string Location, DateOnly Date,
    long Revision, string Reason, DateTimeOffset? CompletedAt, string? CompletedBy,
    IReadOnlyList<StockTakeLine> Items)
{
    public bool CanEdit { get; init; }
}

public sealed record StockTakeListRow(StockTakeLine Item, string? Category, bool Added);

public sealed record StockTakeList(int Id, string Location, DateOnly Date, DateTimeOffset? CompletedAt,
    IReadOnlyList<StockTakeListRow> Rows, IReadOnlyList<Lookup> Categories,
    int Count, int Counted, int Shortages, int Extras, int Page, int PageSize);

public sealed record StockTakeLocalDraft(string Version, long Revision, string Reason, IReadOnlyList<StockTakeLocalRow> Rows)
{
    public bool BatchMode { get; init; }
    public IReadOnlyList<StockTakeLocalRow> Batch { get; init; } = [];
}
public sealed record StockTakeLocalRow(StockTakeLine Item, string Text, decimal? Count, bool Valid);

public sealed record StockTakeFilters(string Search, string View, int? CategoryId, string Sort,
    int Page, int PageSize, string ProductSearch);
