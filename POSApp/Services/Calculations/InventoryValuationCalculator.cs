namespace POSApp.Services.Calculations;

public static class InventoryValuationCalculator
{
    public static decimal CalculateMovingAverageCost(decimal existingStock, decimal currentCost, decimal incomingQuantity, decimal incomingUnitCost)
    {
        if (incomingQuantity <= 0)
        {
            return currentCost;
        }

        if (existingStock <= 0)
        {
            return decimal.Round(incomingUnitCost, 2, MidpointRounding.AwayFromZero);
        }

        var totalQuantity = existingStock + incomingQuantity;
        if (totalQuantity <= 0)
        {
            return decimal.Round(incomingUnitCost, 2, MidpointRounding.AwayFromZero);
        }

        var newAverage = currentCost + (incomingUnitCost - currentCost) * (incomingQuantity / totalQuantity);
        return decimal.Round(newAverage, 2, MidpointRounding.AwayFromZero);
    }
}
