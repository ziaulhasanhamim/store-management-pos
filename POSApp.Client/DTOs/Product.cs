using System.ComponentModel.DataAnnotations;

namespace POSApp.Client.DTOs;

public sealed class ProductInput
{
    [Required, StringLength(200)]
    public string Name { get; set; } = "";

    [StringLength(64)]
    public string? Barcode { get; set; }

    public bool GenerateBarcodeWhenEmpty { get; set; } = true;

    [Required, StringLength(32)]
    public string Unit { get; set; } = "piece";

    public bool AllowFractionUnit { get; set; }

    public bool IsNonStocked { get; set; }

    [Range(typeof(decimal), "0", "9999999999999999.99", ErrorMessage = "Cost price must be between 0 and 99,999,999,999,999.99.")]
    public decimal CostPrice { get; set; }

    [Range(typeof(decimal), "0", "9999999999999999.99")]
    public decimal SellingPrice { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Select a default supplier.")]
    public int SupplierId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Select a category.")]
    public int CategoryId { get; set; }

    public List<ProductAttributeInput> Attributes { get; set; } = [];
    public int AttributeToAddId { get; set; }
    public int RemoveAttributeId { get; set; }
    public string FormAction { get; set; } = "save";
}

public sealed class ProductAttributeInput
{
    public int DefinitionId { get; set; }
    public string? TextValue { get; set; }
    public bool BooleanValue { get; set; }
    public decimal? NumberValue { get; set; }
}

public sealed record ProductAttributeChoice(int Id, string Value, string DisplayName);

public sealed record ProductAttributeDefinitionOption(
    int Id,
    string Key,
    string DisplayName,
    string Kind,
    bool AllowFraction,
    bool NonNegative,
    bool ShowInItems,
    bool ShowInSales,
    bool ShowInReceiving,
    bool ShowInStockAdjustment,
    IReadOnlyList<ProductAttributeChoice> Options);

public sealed class ProductAttributeDefinitionInput
{
    public int Id { get; set; }

    [Required, StringLength(80)]
    public string Key { get; set; } = "";

    [Required, StringLength(120)]
    public string DisplayName { get; set; } = "";

    [Required]
    public string Kind { get; set; } = "Text";

    public bool AllowFraction { get; set; }
    public bool NonNegative { get; set; }
    public bool ShowInItems { get; set; } = true;
    public bool ShowInSales { get; set; }
    public bool ShowInReceiving { get; set; }
    public bool ShowInStockAdjustment { get; set; }
    public List<ProductAttributeOptionInput> Options { get; set; } = [];
    public string SubmitAction { get; set; } = "save";
}

public sealed class ProductAttributeOptionInput
{
    [StringLength(120)]
    public string Value { get; set; } = "";

    [StringLength(120)]
    public string DisplayName { get; set; } = "";
}

public sealed class CategoryInput
{
    [Required, StringLength(120)]
    public string Name { get; set; } = "";

    [StringLength(500)]
    public string? Description { get; set; }
}

public sealed record ItemLocationOptions(IReadOnlyList<Lookup> Locations, int? SelectedLocationId);

public sealed record ItemRow(
    int Id,
    string Name,
    string? Barcode,
    string Unit,
    string? Category,
    decimal Price,
    decimal? Cost,
    decimal Stock,
    bool AllowFractionUnit = false,
    int SupplierId = 0,
    string Supplier = "",
    bool IsNonStocked = false)
{
    public IReadOnlyList<ItemAttributeDisplay> Attributes { get; init; } = [];
}

public sealed record ItemAttributeDisplay(
    string Key,
    string DisplayName,
    string Value,
    bool ShowInItems,
    bool ShowInSales,
    bool ShowInReceiving,
    bool ShowInStockAdjustment,
    string? StoredValue = null);

public sealed record ItemAttributeColumn(string Key, string DisplayName);

public sealed record ProductCostHistoryRow(
    int Id,
    DateTimeOffset At,
    decimal OldCost,
    decimal NewCost,
    string Reason,
    string? User = null);

public sealed record ItemDetail(
    ItemRow Item,
    string? Description,
    int CategoryId,
    IReadOnlyList<StockEvent> History,
    IReadOnlyList<ProductStockLocation> Locations,
    int? SelectedHistoryLocationId)
{
    public PageInfo? HistoryPagination { get; init; }
    public IReadOnlyList<ItemAttributeDisplay> Attributes { get; init; } = [];
    public IReadOnlyList<ProductCostHistoryRow> CostHistory { get; init; } = [];
}

public sealed record BarcodeLabel(int Id, string Name, string Barcode);

public sealed class BarcodeLabelInput
{
    public List<int> ProductIds { get; set; } = [];
    public int Copies { get; set; } = 1;
}
