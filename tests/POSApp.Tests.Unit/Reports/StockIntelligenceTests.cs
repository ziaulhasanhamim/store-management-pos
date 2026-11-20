using POSApp.Client.DTOs;
using POSApp.Services.ML;

namespace POSApp.Tests.Unit.Reports;

public class StockIntelligenceTests
{
    private readonly DeadStockMLService _mlService = new();

    [Test]
    public async Task DeadStockMLService_ClassifiesStagnantLowProfitItems_AsDeadStock()
    {
        var catalog = new List<ProductDeadStockProfile>
        {
            // Stagnant items: 120 days since sale, 0 velocity, tiny profit
            new(1, "Stagnant Case", 120, 0.001m, 1m, 500m, 10m, 15m, 1, null),
            new(2, "Old Cable", 150, 0.002m, 2m, 300m, 5m, 10m, 1, null),

            // Active items: recent sales, high velocity
            new(3, "Best Charger", 1, 0.50m, 100m, 1000m, 150m, 30m, 5, null),
            new(4, "Top Screen Protector", 2, 0.80m, 160m, 800m, 80m, 40m, 8, null),
            new(5, "Popular Battery", 3, 0.40m, 80m, 2000m, 200m, 25m, 4, null),
            new(6, "Fast Adapter", 4, 0.30m, 60m, 1200m, 120m, 35m, 3, null),
            new(7, "USB Hub", 5, 0.25m, 50m, 1500m, 100m, 28m, 3, null),
            new(8, "Earphones", 2, 0.60m, 120m, 900m, 90m, 32m, 6, null)
        };

        var classifications = _mlService.ClassifyCatalog(catalog);

        await classifications.ContainsKey(1).Should().BeTrue();
        await classifications.ContainsKey(3).Should().BeTrue();

        // Stagnant item should be identified as dead stock
        await classifications[1].IsDeadStock.Should().BeTrue();

        // Active items should NOT be dead stock
        await classifications[3].IsDeadStock.Should().BeFalse();
        await classifications[4].IsDeadStock.Should().BeFalse();
    }

    [Test]
    public async Task DeadStockMLService_ProtectsRareGems_FromDeadStock()
    {
        // High unit profit item that sells rarely (e.g. specialized display repair kit generating 1500 Tk profit)
        var catalog = new List<ProductDeadStockProfile>
        {
            new(1, "High Profit Rare Gem Display", 80, 0.02m, 5m, 5000m, 1500m, 45m, 1, null),
            new(2, "Dead Low Margin Item", 110, 0.005m, 1m, 800m, 20m, 10m, 1, null),
            new(3, "Active Item 1", 3, 0.3m, 50m, 500m, 100m, 25m, 2, null),
            new(4, "Active Item 2", 2, 0.4m, 60m, 600m, 120m, 30m, 2, null),
            new(5, "Active Item 3", 1, 0.5m, 80m, 800m, 150m, 35m, 3, null),
            new(6, "Active Item 4", 4, 0.2m, 40m, 400m, 90m, 28m, 2, null)
        };

        var classifications = _mlService.ClassifyCatalog(catalog);

        // Rare gem generates substantial unit profit, so it must NOT be marked dead stock
        await classifications[1].IsDeadStock.Should().BeFalse();

        // Low margin stagnant item should be marked dead stock
        await classifications[2].IsDeadStock.Should().BeTrue();
    }

    [Test]
    public async Task DeadStockMLService_HonorsUserManualOverride()
    {
        var catalog = new List<ProductDeadStockProfile>
        {
            // User explicitly marked this active item as dead stock
            new(1, "User Forcibly Marked Dead", 1, 1.0m, 100m, 1000m, 200m, 30m, 5, UserOverride: true),

            // User explicitly marked this stagnant item as NOT dead stock
            new(2, "User Forcibly Kept Active", 180, 0m, 0m, 500m, 10m, 10m, 1, UserOverride: false),

            new(3, "Item 3", 10, 0.2m, 30m, 600m, 100m, 25m, 2, null),
            new(4, "Item 4", 5, 0.3m, 40m, 700m, 110m, 28m, 2, null),
            new(5, "Item 5", 2, 0.4m, 50m, 800m, 120m, 30m, 3, null),
            new(6, "Item 6", 3, 0.5m, 60m, 900m, 130m, 32m, 3, null)
        };

        var classifications = _mlService.ClassifyCatalog(catalog);

        await classifications[1].IsDeadStock.Should().BeTrue();
        await classifications[1].IsUserOverridden.Should().BeTrue();

        await classifications[2].IsDeadStock.Should().BeFalse();
        await classifications[2].IsUserOverridden.Should().BeTrue();
    }

    [Test]
    public async Task PortfolioQuadrant_RecognizesHighAbsoluteProfitItem_AsHighProfitability()
    {
        // 2000 Tk item with 25% margin gives 500 Tk profit
        var bigTicketItem = new ProductRawStat(
            ProductId: 10,
            ProductName: "Premium Screen",
            CategoryName: "Screens",
            Volume: 2m,
            Revenue: 4000m,
            Cost: 3000m,
            Profit: 1000m, // 500 Tk profit per unit
            MarginPercent: 25m,
            UnitCost: 1500m,
            SellingPrice: 2000m,
            OrderCount: 2);

        // Thresholds
        decimal thresholdVol = 5m;
        decimal thresholdMargin = 30m;
        decimal thresholdProfit = 200m;
        decimal medianOrders = 3m;

        var quadrant = PortfolioMLService.ComputeQuadrant(
            bigTicketItem,
            thresholdVol,
            thresholdMargin,
            thresholdProfit,
            medianOrders);

        // Even though margin is 25% (< 30%), the 1000 Tk profit (> 200 Tk threshold) makes it an Opportunity (or Star if high volume)!
        await quadrant.Should().BeEqualTo(MarginQuadrant.Opportunity);
    }

    [Test]
    public async Task DeadStockMLService_ClassifiesUnsoldTiedUpInventory_AsDeadStock()
    {
        var catalog = new List<ProductDeadStockProfile>
        {
            // Unsold items with tied-up capital sitting in warehouse for 50 days
            new(101, "BW9 4G SIM Smart Watch", 50, 0m, 0m, 3500m, 0m, 0m, 1, null),
            new(102, "Itel Icon Smart Watch", 60, 0m, 0m, 2400m, 0m, 0m, 1, null),

            // Active items with sales
            new(201, "Fast Selling Screen Protector", 1, 1.2m, 120m, 500m, 80m, 40m, 5, null),
            new(202, "Popular USB-C Cable", 2, 0.8m, 80m, 800m, 50m, 30m, 4, null),
            new(203, "Fast Fast Charger", 3, 0.6m, 60m, 1200m, 120m, 35m, 3, null),
            new(204, "Power Bank", 2, 0.5m, 50m, 1500m, 200m, 28m, 3, null),
            new(205, "Phone Holder", 4, 0.3m, 30m, 400m, 60m, 25m, 2, null),
            new(206, "Wired Earphones", 1, 0.7m, 70m, 700m, 90m, 32m, 4, null)
        };

        var classifications = _mlService.ClassifyCatalog(catalog);

        // Unsold items with tied-up capital must be classified as Dead Stock
        await classifications[101].IsDeadStock.Should().BeTrue();
        await classifications[102].IsDeadStock.Should().BeTrue();

        // Active selling items must not be dead stock
        await classifications[201].IsDeadStock.Should().BeFalse();
        await classifications[202].IsDeadStock.Should().BeFalse();
    }
}
