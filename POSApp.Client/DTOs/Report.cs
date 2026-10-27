namespace POSApp.Client.DTOs;

public sealed record ReportRange(
    DateOnly Start,
    DateOnly End,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc);

public sealed record ReportRow(
    int Id,
    string Number,
    string Party,
    DateTimeOffset At,
    decimal Total,
    decimal Paid)
{
    public decimal? Cost { get; init; }
    public decimal? Profit => Cost is { } cost ? Total - cost : null;
    public IReadOnlyList<SalesReportLine> Items { get; init; } = [];
}

public sealed record ReportPage(
    IReadOnlyList<ReportRow> Rows,
    int Count,
    decimal Total,
    decimal Paid,
    ReportRange Range,
    int Page)
{
    public IReadOnlyList<Lookup> Locations { get; init; } = [];
    public bool CanViewCosts { get; init; }
    public decimal? Cost { get; init; }
    public decimal? Profit => Cost is { } cost ? Total - cost : null;
}

public sealed record PurchaseDetail(
    ReportRow Receipt,
    string Location,
    string? Invoice,
    string? Notes,
    PurchaseDetailLine[] Lines);

public sealed record PurchaseDetailLine(
    string Name,
    string Barcode,
    string Unit,
    decimal Quantity,
    decimal Cost,
    decimal Total);

public sealed record SalesReportLine(int ProductId, string Name, string Barcode, string Unit,
    decimal Quantity, decimal UnitPrice, decimal UnitDiscount, decimal Subtotal, decimal Total,
    decimal? UnitCost, decimal? Cost)
{
    public decimal? Profit => Cost is { } cost ? Total - cost : null;
}

public sealed record ProductSalesRow(int ProductId, string Name, string Barcode, string Unit,
    decimal Quantity, decimal Cost, decimal Subtotal, decimal Discount, decimal Total)
{
    public decimal Profit => Total - Cost;
    public decimal? AverageCost => Quantity == 0 ? null : Cost / Quantity;
    public decimal? AveragePrice => Quantity == 0 ? null : Total / Quantity;
}

public sealed record ProductSalesReport(IReadOnlyList<ProductSalesRow> Rows, ReportRange Range,
    int Count, int Page, decimal Cost, decimal Subtotal, decimal Discount, decimal Total)
{
    public IReadOnlyList<Lookup> Locations { get; init; } = [];
    public decimal Profit => Total - Cost;
}
