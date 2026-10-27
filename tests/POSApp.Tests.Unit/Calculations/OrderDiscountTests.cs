using System.ComponentModel.DataAnnotations;
using POSApp.Client.DTOs;
using POSApp.Services;

namespace POSApp.Tests.Unit.Calculations;

public class OrderDiscountTests
{
    [Test]
    public async Task Allocate_WhenDiscountIsZero_ReturnsAllZeros()
    {
        var gross = new decimal[] { 50m, 150m, 100m };
        var result = OrderDiscount.Allocate(gross, 0m);

        await result.Length.Should().BeEqualTo(3);
        await result[0].Should().BeEqualTo(0m);
        await result[1].Should().BeEqualTo(0m);
        await result[2].Should().BeEqualTo(0m);
    }

    [Test]
    public async Task Allocate_WhenSubtotalIsZero_ReturnsAllZeros()
    {
        var gross = new decimal[] { 0m, 0m };
        var result = OrderDiscount.Allocate(gross, 0m);

        await result.Length.Should().BeEqualTo(2);
        await result[0].Should().BeEqualTo(0m);
        await result[1].Should().BeEqualTo(0m);
    }

    [Test]
    public async Task Allocate_WhenDiscountNegative_ThrowsValidationException()
    {
        var gross = new decimal[] { 100m };
        Assert.Throws<ValidationException>(() => OrderDiscount.Allocate(gross, -5m));
    }

    [Test]
    public async Task Allocate_WhenDiscountExceedsSubtotal_ThrowsValidationException()
    {
        var gross = new decimal[] { 100m };
        Assert.Throws<ValidationException>(() => OrderDiscount.Allocate(gross, 105m));
    }

    [Test]
    public async Task Allocate_WhenGrossHasNegativeAmount_ThrowsValidationException()
    {
        var gross = new decimal[] { 100m, -20m };
        Assert.Throws<ValidationException>(() => OrderDiscount.Allocate(gross, 10m));
    }

    [Test]
    public async Task Allocate_DistributesResidualCentsToLargestFractionalRemainders()
    {
        // 3 items with 10.00 each -> subtotal 30.00. Discount 1.00.
        // Exact: 1.00 / 3 = 0.3333333333...
        // Floor cents: 0.33 each, sum = 0.99. Residual = 0.01 cent.
        // Largest remainder goes to first item index 0 -> 0.34, 0.33, 0.33.
        var gross = new decimal[] { 10m, 10m, 10m };
        var result = OrderDiscount.Allocate(gross, 1.00m);

        await result.Sum().Should().BeEqualTo(1.00m);
        await result[0].Should().BeEqualTo(0.34m);
        await result[1].Should().BeEqualTo(0.33m);
        await result[2].Should().BeEqualTo(0.33m);
    }

    [Test]
    public async Task AllocateUnits_DividesLineDiscountByQuantity()
    {
        // Line 1: qty 2 @ 50 = 100. Line 2: qty 4 @ 25 = 100. Subtotal 200.
        // Discount 20. Line 1 discount = 10, unit discount = 10 / 2 = 5.
        // Line 2 discount = 10, unit discount = 10 / 4 = 2.5.
        var lines = new SalesLine[]
        {
            new(ProductId: 1, Quantity: 2, UnitPrice: 50m),
            new(ProductId: 2, Quantity: 4, UnitPrice: 25m)
        };

        var unitDiscounts = OrderDiscount.AllocateUnits(lines, 20m);

        await unitDiscounts.Length.Should().BeEqualTo(2);
        await unitDiscounts[0].Should().BeEqualTo(5m);
        await unitDiscounts[1].Should().BeEqualTo(2.5m);
    }
}
