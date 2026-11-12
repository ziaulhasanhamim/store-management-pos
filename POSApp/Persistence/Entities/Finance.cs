using System.ComponentModel.DataAnnotations.Schema;

namespace POSApp.Persistence.Entities;

public sealed class CashDrawer : AuditableEntity
{
    public int? StockLocationId { get; set; }
    public StockLocation? StockLocation { get; set; }
    public decimal OpeningBalance { get; set; }
    public DateOnly? OpeningDate { get; set; }
    public decimal CurrentCash { get; set; }
    public DateTimeOffset? LastCountedAt { get; set; }
    public decimal? LastCountedCash { get; set; }
    public int? CurrentSessionId { get; set; }
    public CashSession? CurrentSession { get; set; }
    public ICollection<CashSession> Sessions { get; set; } = [];
    public ICollection<CashCount> Counts { get; set; } = [];
    public ICollection<CashMovement> Movements { get; set; } = [];
}

public sealed class CashSession : AuditableEntity
{
    public Guid RequestId { get; set; }
    public int CashDrawerId { get; set; }
    public CashDrawer? CashDrawer { get; set; }
    public int? StockLocationId { get; set; }
    public StockLocation? StockLocation { get; set; }
    public DateOnly BusinessDate { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public decimal OpeningCash { get; set; }
    public decimal ExpectedCash { get; set; }
    public decimal? ExpectedClosingCash { get; set; }
    public decimal? ActualClosingCash { get; set; }
    public decimal? Difference { get; set; }
    public string? ClosingNotes { get; set; }
    public ICollection<CashMovement> Movements { get; set; } = [];
}

public sealed class CashCount : SoftDeletableEntity
{
    public Guid RequestId { get; set; }
    public int CashDrawerId { get; set; }
    public CashDrawer? CashDrawer { get; set; }
    public int StockLocationId { get; set; }
    public StockLocation? StockLocation { get; set; }
    public DateOnly BusinessDate { get; set; }
    public DateTimeOffset CountedAt { get; set; }
    [NotMapped]
    public DateTimeOffset RecordedAt { get => CreatedAt; set => CreatedAt = value; }
    public int CountedByUserId { get; set; }
    public User? CountedByUser { get; set; }
    public decimal CountedCash { get; set; }
    public decimal ExpectedCash { get; set; }
    public decimal Difference => CountedCash - ExpectedCash;
    public decimal? AcceptedVariance { get; set; }
    public string? Notes { get; set; }
    public CashCountStatus Status { get; set; } = CashCountStatus.PendingReview;
    public DateTimeOffset? CompletedAt { get; set; }
    public int? CompletedByUserId { get; set; }
    public DateTimeOffset? VarianceAcceptedAt { get; set; }
    public int? VarianceAcceptedByUserId { get; set; }
    public int? VarianceMovementId { get; set; }
    public CashMovement? VarianceMovement { get; set; }
    [NotMapped]
    public bool IsVoided { get => IsDeleted; set => IsDeleted = value; }
    [NotMapped]
    public DateTimeOffset? VoidedAt { get => DeletedAt; set => DeletedAt = value; }
    public int? VoidedByUserId { get; set; }
    public string? VoidReason { get; set; }
}

public sealed class CashMovement : AuditableEntity
{
    public Guid RequestId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public int CashDrawerId { get; set; }
    public CashDrawer? CashDrawer { get; set; }
    public int? CashTransferId { get; set; }
    public CashTransfer? CashTransfer { get; set; }
    public int? CashSessionId { get; set; }
    public CashSession? CashSession { get; set; }
    public CashMovementKind Kind { get; set; }
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public required string Notes { get; set; }
    public int? PaymentId { get; set; }
    public Payment? Payment { get; set; }
    public int? SupplierPaymentId { get; set; }
    public SupplierPayment? SupplierPayment { get; set; }
    public int? ExpenseId { get; set; }
    public Expense? Expense { get; set; }
    public int? DebtEntryId { get; set; }
    public DebtEntry? DebtEntry { get; set; }
    public int? PayrollEntryId { get; set; }
    public PayrollEntry? PayrollEntry { get; set; }
    public int? ReversesMovementId { get; set; }
    public CashMovement? ReversesMovement { get; set; }
}

public sealed class Expense : AuditableEntity
{
    public string? RequestHash { get; set; }
    public Guid RequestId { get; set; }
    public ExpenseCategory Category { get; set; } = ExpenseCategory.Other;
    public int? CategoryDefinitionId { get; set; }
    public ExpenseCategoryDefinition? CategoryDefinition { get; set; }
    public int? StockLocationId { get; set; }
    public StockLocation? StockLocation { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? ReceiptReference { get; set; }
    public required string Notes { get; set; }
    public bool IsVoided { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public int? VoidedByUserId { get; set; }
    public string? VoidReason { get; set; }
}

public sealed class ExpenseCategoryDefinition : Entity
{
    public required string Name { get; set; }
    public string NormalizedName { get; set; } = string.Empty;
    public ExpenseCategory? BuiltInCategory { get; set; }
    public bool IsArchived { get; set; }
}

public sealed class FinancialEntryBatch : AuditableEntity
{
    public Guid RequestId { get; set; }
    public required string Kind { get; set; }
    public DateOnly WorkingDate { get; set; }
    public required string ResultJson { get; set; }
}

public sealed class StaffAccount : AuditableEntity
{
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public decimal OutstandingAdvance { get; set; }
    public ICollection<PayrollEntry> Entries { get; set; } = [];
}

public sealed class PayrollEntry : AuditableEntity
{
    public string? RequestHash { get; set; }
    public Guid RequestId { get; set; }
    public int StaffAccountId { get; set; }
    public StaffAccount? StaffAccount { get; set; }
    public int? MonthlySalaryId { get; set; }
    public MonthlySalary? MonthlySalary { get; set; }
    public PayrollKind Kind { get; set; }
    public DateOnly PayrollMonth { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public decimal Amount { get; set; }
    public decimal AdvanceApplied { get; set; }
    public decimal AdvanceCreated { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal AdvanceBalanceAfter { get; set; }
    public PaymentMethod Method { get; set; }
    public required string Notes { get; set; }
    public bool IsVoided { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public int? VoidedByUserId { get; set; }
    public string? VoidReason { get; set; }
}

public sealed class Employee : SoftDeletableEntity
{
    public required string Name { get; set; }
    public string NormalizedName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public decimal MonthlySalary { get; set; }
    public StaffAccount? StaffAccount { get; set; }
}

public sealed class MonthlySalary : AuditableEntity
{
    public decimal BroughtForward { get; set; }
    public decimal CarriedForward { get; set; }
    public DateOnly? CarriedToMonth { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public DateOnly Month { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public decimal Amount { get; set; }
    public decimal SettledAmount { get; set; }
    public required string Notes { get; set; }
}

public sealed class BankAccount : AuditableEntity
{
    public string Code { get; set; } = "MAIN";
    public decimal OpeningBalance { get; set; }
    public DateOnly? OpeningDate { get; set; }
    public decimal CurrentBalance { get; set; }
}

public sealed class BankMovement : AuditableEntity
{
    public string? RequestHash { get; set; }
    public Guid RequestId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public int BankAccountId { get; set; }
    public BankAccount? BankAccount { get; set; }
    public CashMovementKind Kind { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public required string Notes { get; set; }
    public int? PaymentId { get; set; }
    public Payment? Payment { get; set; }
    public int? SupplierPaymentId { get; set; }
    public SupplierPayment? SupplierPayment { get; set; }
    public int? ExpenseId { get; set; }
    public Expense? Expense { get; set; }
    public int? DebtEntryId { get; set; }
    public DebtEntry? DebtEntry { get; set; }
    public int? PayrollEntryId { get; set; }
    public PayrollEntry? PayrollEntry { get; set; }
    public int? ReversesMovementId { get; set; }
    public BankMovement? ReversesMovement { get; set; }
}

public sealed class CashTransfer : AuditableEntity
{
    public Guid RequestId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public int SourceDrawerId { get; set; }
    public CashDrawer? SourceDrawer { get; set; }
    public int DestinationDrawerId { get; set; }
    public CashDrawer? DestinationDrawer { get; set; }
    public decimal Amount { get; set; }
    public required string Notes { get; set; }
    public bool IsVoided { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public int? VoidedByUserId { get; set; }
    public string? VoidReason { get; set; }
}
