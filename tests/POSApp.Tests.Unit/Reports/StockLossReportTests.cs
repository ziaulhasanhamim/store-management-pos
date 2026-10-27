using POSApp.Client.DTOs;
using POSApp.Client.Enums;

namespace POSApp.Tests.Unit.Reports;

public class StockLossReportTests
{
    [Test]
    public async Task StockLossReasonTotal_HoldsCorrectTotals()
    {
        var reason1 = new StockLossReasonTotal("Damaged Goods", 3m, 1500m);
        var reason2 = new StockLossReasonTotal("Expired Stock", 2m, 900m);
        var reason3 = new StockLossReasonTotal("Promotional Gift", 1m, 600m);

        await reason1.Amount.Should().BeEqualTo(1500m);
        await reason1.Quantity.Should().BeEqualTo(3m);
        await reason2.Amount.Should().BeEqualTo(900m);
        await reason3.Amount.Should().BeEqualTo(600m);
    }

    [Test]
    public async Task StockLossReportRow_CalculatesLossAtCostPrice()
    {
        var row = new StockLossReportRow(
            OccurredAt: new DateTimeOffset(2026, 10, 26, 14, 30, 0, TimeSpan.Zero),
            LocationName: "Main Store",
            ProductId: 101,
            ProductName: "Organic Fresh Milk 1L",
            Barcode: "894110001001",
            Unit: "Bottle",
            Reason: "Expired",
            Quantity: 5m,
            UnitCost: 95m,
            TotalLoss: 475m,
            Notes: "Expired on shelf batch #202"
        );

        await row.TotalLoss.Should().BeEqualTo(475m);
        await row.Quantity.Should().BeEqualTo(5m);
        await row.Reason.Should().BeEqualTo("Expired");
        await row.LocationName.Should().BeEqualTo("Main Store");
    }

    [Test]
    public async Task StockLossReportPage_SummaryMetrics_MatchTotals()
    {
        var rows = new List<StockLossReportRow>
        {
            new(DateTimeOffset.UtcNow, "Main Store", 10, "Item A", "A01", "Pcs", "Damaged", 4m, 50m, 200m, "Broken bottle"),
            new(DateTimeOffset.UtcNow, "Main Store", 11, "Item B", "B01", "Pcs", "Expired", 2m, 150m, 300m, "Past best before"),
            new(DateTimeOffset.UtcNow, "Main Store", 12, "Item C", "C01", "Pcs", "Store Use", 1m, 100m, 100m, "Floor cleaning demo"),
            new(DateTimeOffset.UtcNow, "Main Store", 13, "Item D", "D01", "Pcs", "Promotional Gift", 5m, 80m, 400m, "VIP corporate hamper")
        };

        var reasons = new List<StockLossReasonTotal>
        {
            new("Damaged", 4m, 200m),
            new("Expired", 2m, 300m),
            new("Store Use", 1m, 100m),
            new("Promotional Gift", 5m, 400m)
        };

        var range = new ReportRange(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero));
        var pagination = new PageInfo(1, 50, 4);

        var page = new StockLossReportPage(
            Rows: rows,
            Range: range,
            TotalQuantity: 12m,
            TotalFinancialLoss: 1000m,
            Reasons: reasons,
            Page: 1
        )
        {
            Locations = [new Lookup(1, "Main Store")],
            Pagination = pagination
        };

        await page.TotalFinancialLoss.Should().BeEqualTo(1000m);
        await page.TotalQuantity.Should().BeEqualTo(12m);
        await page.Rows.Count.Should().BeEqualTo(4);
        await page.Reasons.Count.Should().BeEqualTo(4);
        await page.Pagination.Total.Should().BeEqualTo(4);
    }
}
