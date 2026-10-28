using System.ComponentModel.DataAnnotations;

namespace JASIntStoreManagement.Client.DTOs;

public record TechnicianDto(
    int Id,
    string Name,
    string? Phone,
    decimal DefaultCommissionRate,
    bool IsArchived,
    int TotalJobs = 0,
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

    [Range(0, 100, ErrorMessage = "Commission must be between 0% and 100%.")]
    public decimal DefaultCommissionRate { get; set; } = 50m;

    public bool IsArchived { get; set; }
}

public record ServicingJobDto(
    int Id,
    DateOnly Date,
    int LocationId,
    string LocationName,
    int TechnicianId,
    string TechnicianName,
    string GadgetDescription,
    string? CustomerName,
    string? CustomerPhone,
    decimal BillAmount,
    decimal MaterialCost,
    decimal CommissionRate,
    decimal JobProfit,
    decimal TechnicianCut,
    decimal ShopProfit,
    bool IsDeposited,
    DateTimeOffset? DepositedAt,
    DateTimeOffset CreatedAt,
    string? Notes);

public class ServicingJobInput
{
    public DateOnly Date { get; set; }

    public int LocationId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Please select a technician.")]
    public int TechnicianId { get; set; }

    [Required(ErrorMessage = "Gadget / service details are required.")]
    [StringLength(200, ErrorMessage = "Description cannot exceed 200 characters.")]
    public string GadgetDescription { get; set; } = string.Empty;

    [StringLength(100, ErrorMessage = "Customer name cannot exceed 100 characters.")]
    public string? CustomerName { get; set; }

    [StringLength(30, ErrorMessage = "Customer phone cannot exceed 30 characters.")]
    public string? CustomerPhone { get; set; }

    [Range(0.01, 10000000, ErrorMessage = "Bill amount must be greater than zero.")]
    public decimal BillAmount { get; set; }

    [Range(0, 10000000, ErrorMessage = "Material cost cannot be negative.")]
    public decimal MaterialCost { get; set; }

    [Range(0, 100, ErrorMessage = "Commission rate must be between 0% and 100%.")]
    public decimal CommissionRate { get; set; } = 50m;

    [StringLength(500, ErrorMessage = "Notes cannot exceed 500 characters.")]
    public string? Notes { get; set; }

    public decimal JobProfit => Math.Max(0, BillAmount - MaterialCost);

    public decimal TechnicianCut => Math.Round(JobProfit * (CommissionRate / 100m), 2, MidpointRounding.AwayFromZero);

    public decimal ShopProfit => JobProfit - TechnicianCut;
}

public record ServicingSummaryDto(
    int TotalJobs,
    decimal TotalBilled,
    decimal TotalMaterialCost,
    decimal TotalProfit,
    decimal TotalTechnicianCut,
    decimal TotalShopProfit,
    decimal UndepositedShopProfit,
    decimal DepositedShopProfit);

public record ServicingPageData(
    DateOnly Date,
    int? LocationId,
    IReadOnlyList<StockLocationOption> Locations,
    IReadOnlyList<TechnicianDto> Technicians,
    IReadOnlyList<ServicingJobDto> Jobs,
    ServicingSummaryDto Summary,
    bool HasActiveDrawer,
    decimal ActiveDrawerBalance);
