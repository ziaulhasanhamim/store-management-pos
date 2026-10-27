namespace POSApp.Client.DTOs;

public sealed record ImportInput(Guid RequestId, string FileName, string Json,
    int? LocationId = null, DateOnly? Date = null, string Notes = "");

public sealed record ImportStatus(Guid Id, string Kind, string FileName, string Status,
    string? Message, DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt)
{
    public bool Active => Status is "Queued" or "Processing";
}

public sealed class SupplierImportRow
{
    public int? Id { get; init; }
    public required string Name { get; init; }
    public string? Phone { get; init; }
    public decimal Balance { get; init; }
}

public sealed class ProductImportRow
{
    // Supplied IDs are preserved as product primary keys; omitted IDs are database-generated.
    public int? Id { get; init; }
    public required string Name { get; init; }
    public string? Barcode { get; init; }
    public string Unit { get; init; } = "piece";
    public decimal CostPrice { get; init; }
    public decimal SellingPrice { get; init; }
    public string? Description { get; init; }
    public required string Category { get; init; }
    public required string Supplier { get; init; }
    public bool AllowFractionUnit { get; init; }
    public bool IsNonStocked { get; init; }
    public Dictionary<string, System.Text.Json.JsonElement>? Attributes { get; init; }
}
