using System.ComponentModel.DataAnnotations;
using POSApp.Persistence.Entities;

namespace POSApp.Services.Calculations;

public record SalePricingSummary(
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TotalAmount,
    decimal PaidAmount,
    decimal TenderedAmount,
    decimal ChangeAmount,
    decimal DueAmount,
    decimal[] UnitDiscounts,
    PaymentDetail[] PaymentPlan);

public static class SalePricingCalculator
{
    public static SalePricingSummary Calculate(
        IReadOnlyList<SalesLine> lines,
        decimal orderDiscount,
        PaymentInput[]? payments,
        decimal legacyPayment,
        PaymentMethod legacyMethod,
        bool allowChange = true,
        bool allowRefund = true)
    {
        foreach (var line in lines)
        {
            if (line.UnitPrice != 0 && Math.Abs(line.Quantity) > 9999999999999999.99m / line.UnitPrice)
            {
                throw new ValidationException("The line total exceeds the supported amount.");
            }
        }

        StoreAccess.Money(orderDiscount, "Discount");
        StoreAccess.Money(legacyPayment, "Payment", negative: true);

        var subtotal = lines.Sum(line => decimal.Round(line.Quantity * line.UnitPrice, 2, MidpointRounding.AwayFromZero));
        var unitDiscounts = OrderDiscount.AllocateUnits(lines, orderDiscount);
        var discountAmount = decimal.Round(lines.Select((line, index) =>
            unitDiscounts[index] * line.Quantity).Sum(), 2, MidpointRounding.AwayFromZero);

        var total = subtotal - discountAmount;
        StoreAccess.Money(total, "Total", negative: true);

        var paymentPlan = PaymentAllocation.Allocate(total, payments, legacyPayment, legacyMethod, allowChange: allowChange, allowRefund: allowRefund);
        var paidAmount = paymentPlan.Sum(payment => payment.Amount);
        var tenderedAmount = paymentPlan.Sum(payment => payment.Tendered);
        var changeAmount = tenderedAmount - paidAmount;
        var dueAmount = total - paidAmount;

        return new SalePricingSummary(
            subtotal,
            discountAmount,
            total,
            paidAmount,
            tenderedAmount,
            changeAmount,
            dueAmount,
            unitDiscounts,
            paymentPlan);
    }
}
