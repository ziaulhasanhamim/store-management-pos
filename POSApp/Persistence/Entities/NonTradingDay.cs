namespace POSApp.Persistence.Entities;

public sealed class NonTradingDay : Entity
{
    public DateOnly Date { get; set; }
    public int? StockLocationId { get; set; }
    public StockLocation? StockLocation { get; set; }
    public string? Reason { get; set; }
    public int MarkedById { get; set; }
    public User? MarkedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
