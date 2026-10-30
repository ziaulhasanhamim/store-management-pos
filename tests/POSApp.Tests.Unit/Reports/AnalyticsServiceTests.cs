using POSApp.Client.DTOs;

namespace POSApp.Tests.Unit.Reports;

public class AnalyticsServiceTests
{
    [Test]
    public async Task CategoryPerformanceRow_PropertiesAndMath_AreConsistent()
    {
        var row = new CategoryPerformanceRow(
            CategoryId: 10,
            CategoryName: "Accessories",
            ProductCount: 25,
            UnitsSold: 120m,
            TotalRevenue: 60000m,
            TotalCost: 40000m,
            GrossProfit: 20000m,
            MarginPercent: 33.3m,
            RevenueSharePercent: 40.0m,
            TopProductName: "Tempered Glass 9D"
        );

        await row.CategoryId.Should().BeEqualTo(10);
        await row.CategoryName.Should().BeEqualTo("Accessories");
        await row.ProductCount.Should().BeEqualTo(25);
        await row.UnitsSold.Should().BeEqualTo(120m);
        await row.TotalRevenue.Should().BeEqualTo(60000m);
        await row.TotalCost.Should().BeEqualTo(40000m);
        await row.GrossProfit.Should().BeEqualTo(20000m);
        await row.MarginPercent.Should().BeEqualTo(33.3m);
        await row.RevenueSharePercent.Should().BeEqualTo(40.0m);
        await row.TopProductName.Should().BeEqualTo("Tempered Glass 9D");
    }

    [Test]
    public async Task CategoryPerformanceReportData_AggregatesTotals()
    {
        var cat1 = new CategoryPerformanceRow(1, "Display", 5, 20m, 50000m, 35000m, 15000m, 30.0m, 62.5m, "OLED Display");
        var cat2 = new CategoryPerformanceRow(2, "Battery", 8, 30m, 30000m, 18000m, 12000m, 40.0m, 37.5m, "Li-Poly Battery 4500mAh");

        var report = new CategoryPerformanceReportData(
            StartDate: new DateOnly(2026, 10, 1),
            EndDate: new DateOnly(2026, 10, 30),
            LocationId: 1,
            TotalRevenue: 80000m,
            TotalCost: 53000m,
            TotalProfit: 27000m,
            OverallMarginPercent: 33.8m,
            Locations: [new StockLocationOption(1, "Main Store")],
            Categories: [cat1, cat2]
        );

        await report.TotalRevenue.Should().BeEqualTo(80000m);
        await report.TotalCost.Should().BeEqualTo(53000m);
        await report.TotalProfit.Should().BeEqualTo(27000m);
        await report.Categories.Count.Should().BeEqualTo(2);
        await report.Categories[0].CategoryName.Should().BeEqualTo("Display");
    }

    [Test]
    public async Task SupplierSalesRow_PreservesStockAndSalesData()
    {
        var sup = new SupplierSalesRow(
            SupplierId: 5,
            SupplierName: "Apex Mobile Parts",
            ProductsSoldDistinct: 12,
            UnitsSold: 85m,
            TotalRevenue: 120000m,
            TotalCost: 80000m,
            GrossProfit: 40000m,
            MarginPercent: 33.3m,
            CurrentStockUnits: 340m,
            CurrentStockValue: 250000m
        );

        await sup.SupplierId.Should().BeEqualTo(5);
        await sup.SupplierName.Should().BeEqualTo("Apex Mobile Parts");
        await sup.ProductsSoldDistinct.Should().BeEqualTo(12);
        await sup.UnitsSold.Should().BeEqualTo(85m);
        await sup.GrossProfit.Should().BeEqualTo(40000m);
        await sup.CurrentStockUnits.Should().BeEqualTo(340m);
        await sup.CurrentStockValue.Should().BeEqualTo(250000m);
    }

    [Test]
    public async Task InventoryVelocityRow_CorrectlyIdentifiesDeadStockTier()
    {
        var deadRow = new InventoryVelocityRow(
            ProductId: 201,
            ProductName: "Old Model Back Cover",
            Barcode: "998877665544",
            CategoryName: "Cases",
            SupplierName: "Generic Supplier",
            QuantityOnHand: 45m,
            CostPrice: 120m,
            SellingPrice: 250m,
            TiedUpCapital: 5400m,
            LastSoldDate: new DateOnly(2026, 6, 1),
            DaysSinceLastSale: 120,
            UnitsSoldPeriod: 0m,
            Tier: StockVelocityTier.Dead
        );

        await deadRow.Tier.Should().BeEqualTo(StockVelocityTier.Dead);
        await deadRow.DaysSinceLastSale.Should().BeEqualTo(120);
        await deadRow.TiedUpCapital.Should().BeEqualTo(5400m);

        var fastRow = new InventoryVelocityRow(
            ProductId: 202,
            ProductName: "Fast Fast Charger 65W",
            Barcode: "112233445566",
            CategoryName: "Chargers",
            SupplierName: "Anker BD",
            QuantityOnHand: 15m,
            CostPrice: 800m,
            SellingPrice: 1400m,
            TiedUpCapital: 12000m,
            LastSoldDate: new DateOnly(2026, 10, 28),
            DaysSinceLastSale: 1,
            UnitsSoldPeriod: 32m,
            Tier: StockVelocityTier.Fast
        );

        await fastRow.Tier.Should().BeEqualTo(StockVelocityTier.Fast);
        await fastRow.DaysSinceLastSale.Should().BeEqualTo(1);
    }

    [Test]
    public async Task MarginMatrixRow_AssignsStrategicQuadrants()
    {
        var star = new MarginMatrixRow(1, "Premium AMOLED Screen", "Displays", 50m, 150000m, 90000m, 60000m, 40.0m, MarginQuadrant.Star);
        var cashCow = new MarginMatrixRow(2, "Screen Protector", "Accessories", 200m, 40000m, 32000m, 8000m, 20.0m, MarginQuadrant.CashCow);
        var opportunity = new MarginMatrixRow(3, "Specialized Micro-soldering Tool", "Tools", 3m, 18000m, 9000m, 9000m, 50.0m, MarginQuadrant.Opportunity);
        var laggard = new MarginMatrixRow(4, "Obsolete USB-A Cable", "Cables", 2m, 300m, 250m, 50m, 16.7m, MarginQuadrant.Laggard);

        await star.Quadrant.Should().BeEqualTo(MarginQuadrant.Star);
        await cashCow.Quadrant.Should().BeEqualTo(MarginQuadrant.CashCow);
        await opportunity.Quadrant.Should().BeEqualTo(MarginQuadrant.Opportunity);
        await laggard.Quadrant.Should().BeEqualTo(MarginQuadrant.Laggard);
    }

    [Test]
    public async Task SalesTimelineReportData_CorrectlyStructuresDataPoints()
    {
        var points = new List<SalesTimelinePoint>
        {
            new(new DateOnly(2026, 10, 25), 15000m, 5000m, 12),
            new(new DateOnly(2026, 10, 26), 22000m, 8000m, 18),
            new(new DateOnly(2026, 10, 27), 18500m, 6200m, 15)
        };

        var timeline = new SalesTimelineReportData(points);

        await timeline.Points.Count.Should().BeEqualTo(3);
        await timeline.Points[1].SalesRevenue.Should().BeEqualTo(22000m);
        await timeline.Points[1].GrossProfit.Should().BeEqualTo(8000m);
        await timeline.Points[1].OrdersCount.Should().BeEqualTo(18);
    }
}
