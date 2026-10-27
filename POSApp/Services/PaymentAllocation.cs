using System.ComponentModel.DataAnnotations;

namespace POSApp.Services;

public static class PaymentAllocation
{
    public static PaymentDetail[] Allocate(decimal total, PaymentInput[]? payments, decimal legacyAmount,
        PaymentMethod legacyMethod, bool allowChange, bool allowRefund = false)
    {
        var inputs = payments ?? (legacyAmount == 0 ? [] : [new PaymentInput(legacyMethod, legacyAmount)]);
        if (inputs.Length > 20 || !Enum.IsDefined(legacyMethod))
        {
            throw new ValidationException("Use up to 20 payments and a valid payment method.");
        }

        var direction = allowRefund && total < 0 ? -1 : 1;
        var remaining = allowRefund ? Math.Abs(total) : Math.Max(total, 0);
        var result = new List<PaymentDetail>();
        foreach (var input in inputs)
        {
            StoreAccess.Money(input.Amount, "Payment", negative: allowRefund);
            var amount = input.Amount * direction;
            if (amount <= 0 || !Enum.IsDefined(input.Method) || remaining <= 0)
            {
                throw new ValidationException("Payments must be positive. Remove payments after the order has been paid in full.");
            }
            if (amount > remaining && (!allowChange || direction < 0 || input.Method != PaymentMethod.Cash))
            {
                throw new ValidationException("Payment exceeds the remaining amount. Only sales cash payments can include change.");
            }
            var applied = Math.Min(amount, remaining);
            result.Add(new(input.Method, applied * direction, input.Amount));
            remaining -= applied;
        }
        StoreAccess.Money(result.Sum(payment => payment.Tendered), "Total tendered", negative: allowRefund);
        return result.ToArray();
    }
}
