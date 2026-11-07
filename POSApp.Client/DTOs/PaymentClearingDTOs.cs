using POSApp.Client.Enums;

namespace POSApp.Client.DTOs;

public sealed record ClearingSummaryDto(
    decimal CardAwaiting,
    decimal MobileWalletAwaiting,
    decimal TotalAwaiting,
    int UnsettledCount,
    DateOnly? OldestUnsettledDate,
    int OverdueCount,
    decimal OverdueAmount);

public sealed record ClearingSaleItemDto(
    int Id,
    int PaymentId,
    int? SaleId,
    DateOnly BusinessDate,
    DateTimeOffset SoldAt,
    int StockLocationId,
    string StockLocationName,
    PaymentMethod Method,
    decimal GrossAmount,
    int DaysWaiting);

public sealed class RecordPayoutInput
{
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public int StockLocationId { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.Card;
    public List<int> ClearingEntryIds { get; set; } = [];
    public decimal GrossAmount { get; set; }
    public decimal NetAmount { get; set; }
    public int BankAccountId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public string? Reference { get; set; }
    public string Notes { get; set; } = "";
}

public sealed record PayoutHistoryRowDto(
    int Id,
    DateOnly BusinessDate,
    DateTimeOffset SettledAt,
    int StockLocationId,
    string StockLocationName,
    PaymentMethod Method,
    decimal GrossAmount,
    decimal FeeAmount,
    decimal FeePercentage,
    decimal NetAmount,
    string BankAccountName,
    string? Reference,
    string Notes,
    string CreatedByName);

public sealed record ClearingAgeingDto(
    int LocationId,
    string LocationName,
    PaymentMethod Method,
    decimal Under3Days,
    decimal From3To7Days,
    decimal Over7Days,
    decimal Total);

public sealed record MonthlyFeeAnalyticsDto(
    DateOnly Month,
    PaymentMethod Method,
    decimal GrossAmount,
    decimal FeeAmount,
    decimal EffectiveFeePercentage);
