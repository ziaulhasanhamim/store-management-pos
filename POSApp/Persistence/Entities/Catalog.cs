namespace POSApp.Persistence.Entities;

public sealed class Category : SoftDeletableEntity
{
    public required string Name { get; set; }
    public string NormalizedName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ICollection<Product> Products { get; set; } = [];
}

public enum ProductAttributeKind
{
    Text,
    Select,
    Checkbox,
    Number
}

public sealed class ProductAttributeDefinition : SoftDeletableEntity
{
    public required string Key { get; set; }
    public required string DisplayName { get; set; }
    public ProductAttributeKind Kind { get; set; }
    public bool AllowFraction { get; set; }
    public bool NonNegative { get; set; }
    public bool ShowInItems { get; set; }
    public bool ShowInSales { get; set; }
    public bool ShowInReceiving { get; set; }
    public bool ShowInStockAdjustment { get; set; }
    public int DisplayOrder { get; set; }
    public ICollection<ProductAttributeOption> Options { get; set; } = [];
    public ICollection<ProductAttributeValue> Values { get; set; } = [];
}

public sealed class ProductAttributeOption : Entity
{
    public int ProductAttributeDefinitionId { get; set; }
    public ProductAttributeDefinition? Definition { get; set; }
    public required string Value { get; set; }
    public required string DisplayName { get; set; }
    public int DisplayOrder { get; set; }
}

public sealed class ProductAttributeValue : Entity
{
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public int ProductAttributeDefinitionId { get; set; }
    public ProductAttributeDefinition? Definition { get; set; }
    public required string Value { get; set; }
    public string? DisplayValue { get; set; }
}

public sealed class Product : SoftDeletableEntity
{
    public string? Barcode { get; set; }
    public required string Name { get; set; }
    public string NormalizedName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Unit { get; set; } = "piece";
    public bool AllowFractionUnit { get; set; }
    public bool IsNonStocked { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SellingPrice { get; set; }
    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    public ICollection<ProductAttributeValue> AttributeValues { get; set; } = [];
    public ICollection<InventoryBalance> InventoryBalances { get; set; } = [];
}
