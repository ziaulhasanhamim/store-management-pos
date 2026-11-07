using POSApp.Client.Enums;

namespace POSApp.Client.DTOs;

public sealed record ExpenseReportDto(
    ReportRange Range,
    decimal TotalExpenses,
    string? TopLocationName,
    decimal TopLocationAmount,
    decimal TopLocationPercentage,
    string? TopCategoryName,
    decimal TopCategoryAmount,
    decimal TopCategoryPercentage,
    decimal ProcessingFeesTotal,
    IReadOnlyList<LocationExpenseSummaryDto> Locations,
    IReadOnlyList<CategoryExpenseSummaryDto> Categories,
    IReadOnlyList<ExpenseReportItemDto> Items);

public sealed record LocationExpenseSummaryDto(
    int LocationId,
    string LocationName,
    decimal TotalExpense,
    decimal SharePercentage,
    string? TopCategoryName,
    decimal TopCategoryAmount,
    decimal ProcessingFees,
    decimal SalesRevenue,
    decimal NetExpenseImpact);

public sealed record CategoryExpenseSummaryDto(
    int CategoryId,
    string CategoryName,
    decimal TotalAmount,
    decimal SharePercentage);

public sealed record ExpenseReportItemDto(
    int Id,
    DateTimeOffset OccurredAt,
    int? StockLocationId,
    string LocationName,
    int CategoryId,
    string CategoryName,
    decimal Amount,
    PaymentMethod Method,
    string? ReceiptReference,
    string Notes,
    string CreatedByUserName);
