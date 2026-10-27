namespace POSApp.Persistence.Entities;

public sealed class ProductCostHistory : Entity
{
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public decimal OldCostPrice { get; set; }
    public decimal NewCostPrice { get; set; }
    public string Reason { get; set; } = "";
    public string? ReferenceType { get; set; }
    public int? ReferenceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
}
