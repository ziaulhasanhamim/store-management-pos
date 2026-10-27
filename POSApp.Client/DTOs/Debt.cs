using POSApp.Client.Enums;

namespace POSApp.Client.DTOs;

public sealed class LenderInput
{
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public DateOnly WorkingDate { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public decimal OpeningBalance { get; set; }
}

public sealed class DebtPaymentInput
{
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public DateOnly WorkingDate { get; set; }
    public int LenderId { get; set; }
    public DebtEntryKind Kind { get; set; } = DebtEntryKind.Borrowing;
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.Cash;
    public int? CashLocationId { get; set; }
    public string Notes { get; set; } = "";
}

public sealed record LenderRow(int Id, string Name, string? Phone, decimal Opening, decimal Balance);
public sealed record DebtLocation(int Id, string Name);
public sealed record DebtHistoryRow(int Id, DateOnly Date, int LenderId, string Lender,
    DebtEntryKind Kind, decimal Amount, decimal BalanceAfter, PaymentMethod? Method,
    string? CashLocation, string Notes);
public sealed record DebtPage(DateOnly WorkingDate, bool CanPost, IReadOnlyList<LenderRow> Lenders,
    IReadOnlyList<DebtLocation> Locations, decimal BorrowedToday, decimal RepaidToday,
    IReadOnlyList<DebtHistoryRow> History)
{
    public PageInfo? LenderPagination { get; init; }
    public PageInfo? HistoryPagination { get; init; }
    public decimal TotalOutstanding { get; init; }
}
