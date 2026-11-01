using POSApp.Client.DTOs;

namespace POSApp.Tests.Unit.Reports;

public class ProductSalesReportTests
{
    [Test]
    public async Task ProductSalesRow_CalculatesProfitFromSumOfHistoricalCosts_NotAverageCost()
    {
        // Scenario requested: Item 1 sold at cost 300, then cost changes and another sold at cost 400.
        // Selling price is 500 each.
        // Line 1: Qty = 1, Price = 500, Cost at selling time = 300
        // Line 2: Qty = 1, Price = 500, Cost at selling time = 400
        const decimal line1Total = 500m;
        const decimal line1Cost = 300m;
        const decimal line2Total = 500m;
        const decimal line2Cost = 400m;

        var totalQty = 2m;
        var totalCost = line1Cost + line2Cost; // 700m
        var totalSelling = line1Total + line2Total; // 1000m
        var subtotal = 1000m;
        var discount = 0m;

        var row = new ProductSalesRow(
            ProductId: 1,
            Name: "Smartphone Adapter",
            Barcode: "12345678",
            Unit: "piece",
            Quantity: totalQty,
            Cost: totalCost,
            Subtotal: subtotal,
            Discount: discount,
            Total: totalSelling
        );

        // Profit must strictly equal Total Selling Amount - Sum of Costs at selling time:
        await row.Cost.Should().BeEqualTo(700m);
        await row.Total.Should().BeEqualTo(1000m);
        await row.Profit.Should().BeEqualTo(300m);

        // Average cost is displayed for reference (350), but profit is exact:
        await row.AverageCost.Should().BeEqualTo(350m);
        await row.AveragePrice.Should().BeEqualTo(500m);
        await (row.Total - row.Cost).Should().BeEqualTo(row.Profit);
    }

    [Test]
    public async Task ProductSalesRow_CalculatesExactProfit_WithChangingCosts_300And400_SoldAt_400And600()
    {
        // Scenario from user:
        // Item 1: Sold at 400 when cost was 300
        // Item 2: Sold at 600 when cost was 400
        // Total selling = 400 + 600 = 1000
        // Total cost = 300 + 400 = 700
        // Realized profit = 1000 - 700 = 300 (NOT 1000 - (400 * 2) = 200)
        const decimal line1Total = 400m;
        const decimal line1Cost = 300m;
        const decimal line2Total = 600m;
        const decimal line2Cost = 400m;

        var totalQty = 2m;
        var totalCost = line1Cost + line2Cost; // 700m
        var totalSelling = line1Total + line2Total; // 1000m

        var row = new ProductSalesRow(
            ProductId: 1,
            Name: "Smartphone Case",
            Barcode: "99887766",
            Unit: "piece",
            Quantity: totalQty,
            Cost: totalCost,
            Subtotal: totalSelling,
            Discount: 0m,
            Total: totalSelling
        );

        await row.Cost.Should().BeEqualTo(700m);
        await row.Total.Should().BeEqualTo(1000m);
        await row.Profit.Should().BeEqualTo(300m);
        await (row.Profit != (totalSelling - (400m * 2m))).Should().BeTrue();
        await row.AverageCost.Should().BeEqualTo(350m);
        await row.AveragePrice.Should().BeEqualTo(500m);
    }

    [Test]
    public async Task ProductSalesRow_WithReturns_SubtractedHistoricalCostPreservesProfit()
    {
        // Sale: 2 units sold at 500 each (Cost: 300 each = 600)
        // Return: 1 unit returned at 500 (Cost: 300 saved = -300)
        var netQuantity = 1m;
        var netCost = 300m;
        var netTotal = 500m;

        var row = new ProductSalesRow(
            ProductId: 2,
            Name: "Wireless Mouse",
            Barcode: "87654321",
            Unit: "piece",
            Quantity: netQuantity,
            Cost: netCost,
            Subtotal: 500m,
            Discount: 0m,
            Total: netTotal
        );

        await row.Cost.Should().BeEqualTo(300m);
        await row.Profit.Should().BeEqualTo(200m);
        await row.AverageCost.Should().BeEqualTo(300m);
    }
}
