using POSApp.Client.Enums;

namespace POSApp.Client.DTOs;

public sealed record ReceivingOptions(
    IReadOnlyList<SupplierOption> Suppliers,
    IReadOnlyList<Lookup> Locations)
{
    public DateOnly? WorkingDate { get; init; }
    public IReadOnlyList<ProductAttributeDefinitionOption> Attributes { get; init; } = [];
}

public sealed record ReceiveLine(int ProductId, decimal Quantity, decimal UnitCost);

public sealed record ReceiveCommand(
    Guid RequestId,
    int SupplierId,
    int LocationId,
    IReadOnlyList<ReceiveLine> Lines,
    decimal Payment,
    PaymentMethod Method,
    string? Invoice,
    string? Notes)
{
    public DateOnly? WorkingDate { get; init; }
    public bool AllowDuplicateInvoice { get; init; }
    public PaymentInput[]? Payments { get; init; }
}

public sealed record ReceiveResult(
    int Id,
    string Number,
    decimal Total,
    decimal Paid,
    decimal SupplierDue);

public sealed record ReceiptRow(
    int Id,
    string Number,
    string Supplier,
    DateTimeOffset At,
    decimal Total,
    decimal Paid);

public sealed record ReceivingActivityLine(
    int ProductId,
    string Product,
    string? Barcode,
    string Unit,
    decimal Quantity,
    decimal UnitCost,
    decimal Value);

public sealed record ReceivingActivityRow(
    int Id,
    bool IsTransfer,
    DateTimeOffset At,
    string Source,
    string Destination,
    decimal Quantity,
    decimal Value,
    IReadOnlyList<ReceivingActivityLine> Items,
    string? Number = null,
    decimal Paid = 0,
    decimal Unpaid = 0)
{
    public ReceivingActivityRow(int id, bool isTransfer, DateTimeOffset at,
        string product, string? barcode, string unit, string source, string destination,
        decimal quantity, decimal unitCost, decimal value)
        : this(id, isTransfer, at, source, destination, quantity, value,
               [new ReceivingActivityLine(0, product, barcode, unit, quantity, unitCost, value)])
    {
    }

    public string Product => Items.FirstOrDefault()?.Product ?? "";
    public string? Barcode => Items.FirstOrDefault()?.Barcode;
    public string Unit => Items.FirstOrDefault()?.Unit ?? "";
    public decimal UnitCost => Items.FirstOrDefault()?.UnitCost ?? 0;
}

public sealed record ReceivingActivityReport(IReadOnlyList<ReceivingActivityRow> Rows, ReportRange Range,
    IReadOnlyList<Lookup> Locations, int Count, int Page, decimal PurchaseValue,
    decimal TransferInValue, decimal TransferOutValue, decimal SupplierPaid, decimal SupplierUnpaid)
{
    public decimal NetValue => PurchaseValue + TransferInValue - TransferOutValue;
}
