using POSApp.Client.Enums;

namespace POSApp.Client.DTOs;

public sealed record CustomerOption(
    int Id,
    string Name,
    decimal Balance);

public sealed record CustomerInput(
    string Name,
    string? Phone,
    decimal OpeningBalance);

public sealed record SalesProduct(
    int Id,
    string Name,
    string? Barcode,
    string Unit,
    decimal Price,
    decimal Available,
    bool AllowFractionUnit,
    bool IsNonStocked = false)
{
    public IReadOnlyList<ItemAttributeDisplay> Attributes { get; init; } = [];
}

public sealed record SalesDraftRow(
    Guid RequestId,
    string Number,
    string? Customer,
    decimal Total);

public sealed record SalesOptions(
    Lookup[] Locations,
    CustomerOption[] Customers,
    SalesDraftRow[] Drafts)
{
    public DateOnly? WorkingDate { get; init; }
    public bool CanSetOpeningBalance { get; init; }
}

public sealed record SalesLine(
    int ProductId,
    decimal Quantity,
    decimal UnitPrice);

public sealed record SalesCommand(
    Guid RequestId,
    int LocationId,
    int? CustomerId,
    SalesLine[] Lines,
    decimal Discount,
    decimal Payment,
    PaymentMethod Method,
    string? Notes)
{
    public DateOnly? WorkingDate { get; init; }
    public long Revision { get; init; }
    public PaymentInput[]? Payments { get; init; }
}

public sealed record SalesResult(
    int Id,
    string Number,
    bool Completed,
    decimal Total,
    decimal Paid,
    decimal Due)
{
    public long Revision { get; init; }
}

public sealed record DraftLine(
    SalesProduct Product,
    decimal Quantity,
    decimal UnitPrice);

public sealed record SalesDraft(
    Guid RequestId,
    int LocationId,
    int? CustomerId,
    DraftLine[] Lines,
    decimal Discount,
    decimal Payment,
    PaymentMethod Method,
    string? Notes)
{
    public long Revision { get; init; }
    public PaymentInput[]? Payments { get; init; }
}

public sealed record BillLine(
    string Name,
    string Barcode,
    string Unit,
    decimal Quantity,
    decimal UnitPrice,
    decimal Total)
{
    public string? Warranty { get; init; }
    public decimal UnitDiscount { get; init; }
}

public sealed record class ProductFound(SalesProduct Product);
public sealed record class BarcodeNotFound(string Code);
public sealed record class AmbiguousMatches(IReadOnlyList<SalesProduct> Products);

public union BarcodeScanOutcome(ProductFound, BarcodeNotFound, AmbiguousMatches);

public sealed record class CheckoutSuccess(SalesResult Result);
public sealed record class CheckoutValidationFailure(string Reason);
public sealed record class CheckoutCreditExceeded(string CustomerName, decimal AmountDue);

public union CheckoutOutcome(CheckoutSuccess, CheckoutValidationFailure, CheckoutCreditExceeded);

public sealed record SaleBill(
    int Id,
    string Number,
    DateTimeOffset At,
    string? Customer,
    string Location,
    string Cashier,
    BillLine[] Lines,
    decimal Subtotal,
    decimal Discount,
    decimal Total,
    decimal Paid,
    decimal Tendered,
    string? Notes)
{
    public PaymentDetail[] Payments { get; init; } = [];
    public bool IsVoided { get; init; }
    public DateTimeOffset? VoidedAt { get; init; }
    public string? CustomerPhone { get; init; }
    public string? CustomerAddress { get; init; }
    public decimal PreviousDue { get; init; }
}
