using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using TUnit.Assertions.Should;

namespace POSApp.Tests.Unit.Reports;

public class ExpenseReportTests
{
    [Test]
    public async Task LocationExpenseSummary_CalculatesNetImpactAndShareCorrectly()
    {
        var summary = new LocationExpenseSummaryDto(
            LocationId: 1,
            LocationName: "Main Store",
            TotalExpense: 20000m,
            SharePercentage: 40.0m,
            TopCategoryName: "Rent",
            TopCategoryAmount: 15000m,
            ProcessingFees: 400m,
            SalesRevenue: 100000m,
            NetExpenseImpact: 80000m);

        await summary.LocationName.Should().BeEqualTo("Main Store");
        await summary.TotalExpense.Should().BeEqualTo(20000m);
        await summary.SharePercentage.Should().BeEqualTo(40.0m);
        await summary.NetExpenseImpact.Should().BeEqualTo(summary.SalesRevenue - summary.TotalExpense);
        await summary.ProcessingFees.Should().BeEqualTo(400m);
    }

    [Test]
    public async Task ExpenseCategoryTotal_SupportsDynamicCategoryIdsAndNames()
    {
        var cat = new ExpenseCategoryTotal(
            CategoryId: 42,
            Name: "Custom Packaging & Boxes",
            Amount: 3200m);

        await cat.CategoryId.Should().BeEqualTo(42);
        await cat.Name.Should().BeEqualTo("Custom Packaging & Boxes");
        await cat.Amount.Should().BeEqualTo(3200m);
    }

    [Test]
    public async Task ExpenseReportDto_AggregatesTotalsAcrossStores()
    {
        var range = new ReportRange(
            new DateOnly(2026, 11, 1),
            new DateOnly(2026, 11, 7),
            DateTimeOffset.UtcNow.AddDays(-7),
            DateTimeOffset.UtcNow);

        var loc1 = new LocationExpenseSummaryDto(1, "Main Store", 30000m, 60.0m, "Rent", 25000m, 500m, 120000m, 90000m);
        var loc2 = new LocationExpenseSummaryDto(2, "Branch Store", 20000m, 40.0m, "Snacks", 5000m, 200m, 60000m, 40000m);

        var report = new ExpenseReportDto(
            Range: range,
            TotalExpenses: 50000m,
            TopLocationName: "Main Store",
            TopLocationAmount: 30000m,
            TopLocationPercentage: 60.0m,
            TopCategoryName: "Rent",
            TopCategoryAmount: 25000m,
            TopCategoryPercentage: 50.0m,
            ProcessingFeesTotal: 700m,
            Locations: [loc1, loc2],
            Categories: [new CategoryExpenseSummaryDto(1, "Rent", 25000m, 50.0m)],
            Items: []);

        await report.TotalExpenses.Should().BeEqualTo(50000m);
        await report.Locations.Count.Should().BeEqualTo(2);
        await report.Locations.Sum(l => l.TotalExpense).Should().BeEqualTo(report.TotalExpenses);
        await report.ProcessingFeesTotal.Should().BeEqualTo(700m);
        await report.TopLocationName.Should().BeEqualTo("Main Store");
    }
}
