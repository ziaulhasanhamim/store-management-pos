using POSApp.Client.Enums;

namespace POSApp.Persistence.Entities;

public sealed class PaymentClearingEntry : AuditableEntity
{
    public string? RequestHash { get; set; }
    public Guid RequestId { get; set; }
    public int PaymentId { get; set; }
    public Payment? Payment { get; set; }
    public int? StockLocationId { get; set; }
    public StockLocation? StockLocation { get; set; }
    public DateOnly BusinessDate { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal SettledAmount { get; set; }
    public PaymentClearingStatus Status { get; set; } = PaymentClearingStatus.Unsettled;
    public int? PayoutId { get; set; }
    public PaymentPayout? Payout { get; set; }
    public DateTimeOffset? SettledAt { get; set; }
    public string? Notes { get; set; }
}

public sealed class PaymentPayout : AuditableEntity
{
    public string? RequestHash { get; set; }
    public Guid RequestId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public DateTimeOffset SettledAt { get; set; }
    public PaymentMethod Method { get; set; }
    public int StockLocationId { get; set; }
    public StockLocation? StockLocation { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal NetAmount { get; set; }
    public decimal FeeAmount { get; set; }
    public int BankAccountId { get; set; }
    public BankAccount? BankAccount { get; set; }
    public int? BankMovementId { get; set; }
    public BankMovement? BankMovement { get; set; }
    public int? ExpenseId { get; set; }
    public Expense? Expense { get; set; }
    public string? Reference { get; set; }
    public required string Notes { get; set; }
    public ICollection<PaymentClearingEntry> Entries { get; set; } = [];
}
