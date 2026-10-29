using System.ComponentModel.DataAnnotations;

namespace JASIntStoreManagement.Client.DTOs;

public record TechnicianDto(
    int Id,
    string Name,
    string? Phone,
    decimal DefaultCommissionRate,
    bool IsArchived,
    int LocationId = 0,
    string LocationName = "",
    int TotalDays = 0,
    decimal TotalProfit = 0m,
    decimal TotalTechnicianCut = 0m);

public class TechnicianInput
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Technician name is required.")]
    [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(30, ErrorMessage = "Phone cannot exceed 30 characters.")]
    public string? Phone { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Please select a store location.")]
    public int LocationId { get; set; }

    [Range(0, 100, ErrorMessage = "Commission must be between 0% and 100%.")]
    public decimal DefaultCommissionRate { get; set; } = 50m;

    public bool IsArchived { get; set; }
}

public record MobileServicingEntryDto(
    int Id,
    DateOnly Date,
    int LocationId,
    string LocationName,
    int TechnicianId,
    string TechnicianName,
    decimal TotalAmount,
    decimal TotalCost,
    decimal CommissionRate,
    decimal Profit,
    decimal TechnicianCut,
    decimal ShopProfit,
    bool IsDeposited,
    DateTimeOffset? DepositedAt,
    DateTimeOffset CreatedAt,
    string? Notes);

public class MobileServicingEntryInput
{
    public int? Id { get; set; }

    public DateOnly Date { get; set; }

    public int LocationId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Please select a technician.")]
    public int TechnicianId { get; set; }

    [Range(0, 10000000, ErrorMessage = "Total servicing amount must be zero or positive.")]
    public decimal TotalAmount { get; set; }

    [Range(0, 10000000, ErrorMessage = "Total cost cannot be negative.")]
    public decimal TotalCost { get; set; }

    [Range(0, 100, ErrorMessage = "Commission rate must be between 0% and 100%.")]
    public decimal CommissionRate { get; set; } = 50m;

    [StringLength(500, ErrorMessage = "Notes cannot exceed 500 characters.")]
    public string? Notes { get; set; }

    public decimal Profit => Math.Max(0m, TotalAmount - TotalCost);

    public decimal TechnicianCut => Math.Round(Profit * (CommissionRate / 100m), 2, MidpointRounding.AwayFromZero);

    public decimal ShopProfit => Profit - TechnicianCut;
}

public record MobileServicingSummaryDto(
    int TechnicianCount,
    decimal TotalServicingAmount,
    decimal TotalCost,
    decimal TotalProfit,
    decimal TotalTechnicianCut,
    decimal TotalShopProfit,
    decimal UndepositedShopProfit,
    decimal DepositedShopProfit);

public record MobileServicingPageData(
    DateOnly Date,
    int? LocationId,
    IReadOnlyList<StockLocationOption> Locations,
    IReadOnlyList<TechnicianDto> Technicians,
    IReadOnlyList<MobileServicingEntryDto> Entries,
    MobileServicingSummaryDto Summary);

public record ServicingReportTechnicianRow(
    int TechnicianId,
    string TechnicianName,
    string LocationName,
    decimal CommissionRate,
    int DaysWorked,
    decimal TotalServicing,
    decimal TotalCost,
    decimal TotalProfit,
    decimal TechnicianCut,
    decimal ShopProfit);

public record ServicingReportData(
    DateOnly StartDate,
    DateOnly EndDate,
    int? LocationId,
    int? TechnicianId,
    string? Search,
    MobileServicingSummaryDto Summary,
    IReadOnlyList<StockLocationOption> Locations,
    IReadOnlyList<TechnicianDto> Technicians,
    IReadOnlyList<ServicingReportTechnicianRow> TechnicianSummaries,
    IReadOnlyList<MobileServicingEntryDto> Entries);
