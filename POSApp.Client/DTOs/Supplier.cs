using System.ComponentModel.DataAnnotations;
using POSApp.Client.Enums;

namespace POSApp.Client.DTOs;

public sealed record SupplierInput(
    string Name,
    string? Phone,
    decimal OpeningDue);

public sealed record SupplierOption(
    int Id,
    string Name,
    decimal Balance);

public sealed record SupplierRow(
    int Id,
    string Name,
    string? Phone,
    decimal OpeningBalance,
    decimal Balance,
    decimal StockValue = 0);

public sealed record SupplierPaymentRow(
    int Id,
    string Supplier,
    DateTimeOffset At,
    decimal Amount,
    PaymentMethod Method,
    string? Reference);

public sealed record SupplierLedgerRow(
    DateTimeOffset At,
    string Type,
    decimal Amount,
    decimal BalanceAfter,
    string? Notes);

public sealed record SupplierDetail(
    SupplierRow Supplier,
    IReadOnlyList<SupplierLedgerRow> Entries)
{
    public PageInfo? Pagination { get; init; }
}

public sealed class SupplierPaymentInput
{
    public Guid RequestId { get; set; } = Guid.NewGuid();

    [Range(1, int.MaxValue)]
    public int SupplierId { get; set; }

    [Range(typeof(decimal), "0.01", "9999999999999999.99")]
    public decimal Amount { get; set; }

    public PaymentMethod Method { get; set; } = PaymentMethod.Cash;

    [StringLength(2000)]
    public string? Notes { get; set; }
}

public sealed class SupplierCreateInput
{
    [Required, StringLength(200)]
    public string Name { get; set; } = "";

    [StringLength(64)]
    public string? Phone { get; set; }

    public decimal OpeningBalance { get; set; }
}
