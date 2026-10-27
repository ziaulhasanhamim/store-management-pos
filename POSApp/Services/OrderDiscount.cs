using System.ComponentModel.DataAnnotations;

namespace POSApp.Services;

public static class OrderDiscount
{
    public static decimal[] Allocate(IReadOnlyList<decimal> grossAmounts, decimal discount)
    {
        StoreAccess.Money(discount, "Discount");
        var subtotal = grossAmounts.Sum();
        if (discount < 0 || discount > subtotal || grossAmounts.Any(amount => amount < 0))
        {
            throw new ValidationException("Discount must be between zero and the subtotal.");
        }
        if (subtotal == 0)
        {
            return new decimal[grossAmounts.Count];
        }

        // Allocate whole cents, then give residual cents to the largest fractional
        // remainders. The saved line discounts always add up to the order discount.
        var exact = grossAmounts.Select(amount => amount * (discount / subtotal)).ToArray();
        var allocated = exact.Select(amount => decimal.Floor(amount * 100) / 100).ToArray();
        var cents = (int)decimal.Round((discount - allocated.Sum()) * 100);
        var order = Enumerable.Range(0, exact.Length)
            .OrderByDescending(index => exact[index] - allocated[index])
            .ThenBy(index => index).ToArray();
        foreach (var index in order.Take(cents))
        {
            allocated[index] += 0.01m;
        }
        return allocated;
    }
    public static decimal[] AllocateUnits(IReadOnlyList<SalesLine> lines, decimal discount)
    {
        var gross = lines.Select(line => decimal.Round(line.UnitPrice * Math.Max(line.Quantity, 0), 2,
            MidpointRounding.AwayFromZero)).ToArray();
        var lineDiscounts = Allocate(gross, discount);
        return lines.Select((line, index) => line.Quantity < 0 ? 0 : decimal.Round(lineDiscounts[index] / line.Quantity,
            20, MidpointRounding.AwayFromZero)).ToArray();
    }

}
