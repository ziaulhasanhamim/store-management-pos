using POSApp.Services.Calculations;
using TUnit.Assertions.Should;

namespace POSApp.Tests.Unit.Calculations;

public class InventoryValuationCalculatorTests
{
    [Test]
    public async Task CalculateMovingAverageCost_WhenExistingStockIsZero_ReturnsIncomingUnitCost()
    {
        // Arrange & Act
        var result = InventoryValuationCalculator.CalculateMovingAverageCost(
            existingStock: 0,
            currentCost: 100m,
            incomingQuantity: 10,
            incomingUnitCost: 150m);

        // Assert
        await result.Should().BeEqualTo(150m);
    }

    [Test]
    public async Task CalculateMovingAverageCost_WhenExistingStockIsNegative_CalculatesWeightedAverageForNetPositiveStock()
    {
        // Arrange & Act: -2 units @ 6 + 4 units @ 8 => ((-2 * 6) + (4 * 8)) / (-2 + 4) = (-12 + 32) / 2 = 20 / 2 = 10
        var result = InventoryValuationCalculator.CalculateMovingAverageCost(
            existingStock: -2,
            currentCost: 6m,
            incomingQuantity: 4,
            incomingUnitCost: 8m);

        // Assert
        await result.Should().BeEqualTo(10m);
    }

    [Test]
    public async Task CalculateMovingAverageCost_WhenTotalQuantityIsZero_ReturnsIncomingUnitCost()
    {
        // Arrange & Act: Divide by zero protection (-2 existing + 2 incoming = 0)
        var result = InventoryValuationCalculator.CalculateMovingAverageCost(
            existingStock: -2,
            currentCost: 6m,
            incomingQuantity: 2,
            incomingUnitCost: 8m);

        // Assert
        await result.Should().BeEqualTo(8m);
    }

    [Test]
    public async Task CalculateMovingAverageCost_WithExistingAndIncomingStock_CalculatesCorrectWeightedAverage()
    {
        // Arrange: 10 units @ 100 + 10 units @ 200 => (1000 + 2000) / 20 = 150
        var result = InventoryValuationCalculator.CalculateMovingAverageCost(
            existingStock: 10,
            currentCost: 100m,
            incomingQuantity: 10,
            incomingUnitCost: 200m);

        // Assert
        await result.Should().BeEqualTo(150m);
    }

    [Test]
    public async Task CalculateMovingAverageCost_WithUnequalWeights_CalculatesWeightedAverageAccurately()
    {
        // Arrange: 3 units @ 50 + 7 units @ 100 => (150 + 700) / 10 = 85
        var result = InventoryValuationCalculator.CalculateMovingAverageCost(
            existingStock: 3,
            currentCost: 50m,
            incomingQuantity: 7,
            incomingUnitCost: 100m);

        // Assert
        await result.Should().BeEqualTo(85m);
    }

    [Test]
    public async Task CalculateMovingAverageCost_WhenIncomingQuantityIsZeroOrNegative_ReturnsCurrentCost()
    {
        // Arrange & Act
        var zeroIncoming = InventoryValuationCalculator.CalculateMovingAverageCost(
            existingStock: 10,
            currentCost: 100m,
            incomingQuantity: 0,
            incomingUnitCost: 200m);

        var negativeIncoming = InventoryValuationCalculator.CalculateMovingAverageCost(
            existingStock: 10,
            currentCost: 100m,
            incomingQuantity: -2,
            incomingUnitCost: 200m);

        // Assert
        await zeroIncoming.Should().BeEqualTo(100m);
        await negativeIncoming.Should().BeEqualTo(100m);
    }

    [Test]
    public async Task CalculateMovingAverageCost_RoundsAwayFromZeroToTwoDecimalPlaces()
    {
        // 2 units @ 10.33 + 1 unit @ 10.34 => (20.66 + 10.34) / 3 = 31 / 3 = 10.3333... -> 10.33
        var result = InventoryValuationCalculator.CalculateMovingAverageCost(
            existingStock: 2,
            currentCost: 10.33m,
            incomingQuantity: 1,
            incomingUnitCost: 10.34m);

        // Assert
        await result.Should().BeEqualTo(10.33m);
    }
}
