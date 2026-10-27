namespace POSApp.Client.DTOs;

public sealed record EditorDraftDocument(long Revision, string Payload, DateTimeOffset UpdatedAt, Guid SaveId)
{
    public bool Pending { get; init; }
}
public sealed record EditorDraftInput(long Revision, string Payload, Guid SaveId);

public sealed record ReceivingEditorState(Guid RequestId, int SupplierId, int LocationId,
    DateOnly? WorkingDate, string? Invoice, bool AllowDuplicateInvoice, string? Notes, PaymentInput[] Payments,
    ReceivingEditorLine[] Lines);
public sealed record ReceivingEditorLine(ItemRow Product, decimal Quantity, decimal Cost, bool QuantityValid, bool CostValid);
public sealed record SalesEditorState(Guid RequestId, long Revision, int LocationId, int? CustomerId,
    DateOnly? WorkingDate, decimal Discount, bool DiscountValid, string? Notes,
    PaymentInput[] Payments, SalesEditorLine[] Lines);
public sealed record SalesEditorLine(SalesProduct Product, decimal Quantity, decimal Price, bool QuantityValid, bool PriceValid);
public sealed record TransferEditorState(Guid RequestId, int SourceId, int DestinationId,
    DateOnly? WorkingDate, string Notes, TransferEditorLine[] Lines);
public sealed record TransferEditorLine(ItemRow Product, decimal Quantity, bool Valid);
public sealed record MoneyEditorState(Guid BatchId, DateOnly WorkingDate, MoneyEditorLine[] Lines);
public sealed record MoneyEditorLine(MoneyEntryInput Input, bool ValidAmount, bool FixedParty);
