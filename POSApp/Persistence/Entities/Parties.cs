namespace POSApp.Persistence.Entities;

public sealed class Customer : SoftDeletableEntity
{
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public ICollection<Sale> Sales { get; set; } = [];
    public CustomerBalance? Balance { get; set; }
}

public sealed class Supplier : SoftDeletableEntity
{
    public int? LegacyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public ICollection<Purchase> Purchases { get; set; } = [];
    public SupplierBalance? Balance { get; set; }
}
