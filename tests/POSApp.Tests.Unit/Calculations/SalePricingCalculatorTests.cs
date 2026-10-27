using System.ComponentModel.DataAnnotations;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Persistence.Entities;
using POSApp.Services.Calculations;

namespace POSApp.Tests.Unit.Calculations;

public class SalePricingCalculatorTests
{
    [Test]
    public async Task Calculate_WithSingleLineAndNoDiscount_CalculatesTotalsAndExactPayment()
    {
        // Arrange
        var lines = new List<SalesLine>
        {
            new(ProductId: 1, Quantity: 2, UnitPrice: 100m)
        };

        // Act
        var result = SalePricingCalculator.Calculate(
            lines: lines,
            orderDiscount: 0m,
            payments: [new PaymentInput(PaymentMethod.Cash, 200m)],
            legacyPayment: 0m,
            legacyMethod: PaymentMethod.Cash);

        // Assert
        await result.Subtotal.Should().BeEqualTo(200m);
        await result.DiscountAmount.Should().BeEqualTo(0m);
        await result.TotalAmount.Should().BeEqualTo(200m);
        await result.PaidAmount.Should().BeEqualTo(200m);
        await result.TenderedAmount.Should().BeEqualTo(200m);
        await result.ChangeAmount.Should().BeEqualTo(0m);
        await result.DueAmount.Should().BeEqualTo(0m);
        await result.UnitDiscounts.Length.Should().BeEqualTo(1);
        await result.UnitDiscounts[0].Should().BeEqualTo(0m);
    }

    [Test]
    public async Task Calculate_WithDiscount_ApportionsDiscountAcrossLines()
    {
        // Arrange: Line 1 = 100, Line 2 = 100. Subtotal = 200. Discount = 20. Total = 180.
        var lines = new List<SalesLine>
        {
            new(ProductId: 1, Quantity: 10, UnitPrice: 10m),
            new(ProductId: 2, Quantity: 10, UnitPrice: 10m)
        };

        // Act
        var result = SalePricingCalculator.Calculate(
            lines: lines,
            orderDiscount: 20m,
            payments: [new PaymentInput(PaymentMethod.Cash, 180m)],
            legacyPayment: 0m,
            legacyMethod: PaymentMethod.Cash);

        // Assert
        await result.Subtotal.Should().BeEqualTo(200m);
        await result.DiscountAmount.Should().BeEqualTo(20m);
        await result.TotalAmount.Should().BeEqualTo(180m);
        await result.PaidAmount.Should().BeEqualTo(180m);
        await result.DueAmount.Should().BeEqualTo(0m);
        await result.UnitDiscounts[0].Should().BeEqualTo(1m);
        await result.UnitDiscounts[1].Should().BeEqualTo(1m);
    }

    [Test]
    public async Task Calculate_WhenCashPaymentExceedsTotal_CalculatesChange()
    {
        // Arrange: Total = 180, Paid with 200 cash
        var lines = new List<SalesLine>
        {
            new(ProductId: 1, Quantity: 1, UnitPrice: 180m)
        };

        // Act
        var result = SalePricingCalculator.Calculate(
            lines: lines,
            orderDiscount: 0m,
            payments: [new PaymentInput(PaymentMethod.Cash, 200m)],
            legacyPayment: 0m,
            legacyMethod: PaymentMethod.Cash);

        // Assert
        await result.TotalAmount.Should().BeEqualTo(180m);
        await result.PaidAmount.Should().BeEqualTo(180m);
        await result.TenderedAmount.Should().BeEqualTo(200m);
        await result.ChangeAmount.Should().BeEqualTo(20m);
        await result.DueAmount.Should().BeEqualTo(0m);
    }

    [Test]
    public async Task Calculate_WhenNonCashPaymentExceedsTotal_ThrowsValidationException()
    {
        // Arrange
        var lines = new List<SalesLine>
        {
            new(ProductId: 1, Quantity: 1, UnitPrice: 100m)
        };

        // Act & Assert
        Assert.Throws<ValidationException>(() =>
        {
            SalePricingCalculator.Calculate(
                lines: lines,
                orderDiscount: 0m,
                payments: [new PaymentInput(PaymentMethod.Card, 150m)],
                legacyPayment: 0m,
                legacyMethod: PaymentMethod.Card);
        });
    }

    [Test]
    public async Task Calculate_WhenPaymentIsLessThanTotal_CalculatesDueBalance()
    {
        // Arrange: Total 300, partial payment 100
        var lines = new List<SalesLine>
        {
            new(ProductId: 1, Quantity: 3, UnitPrice: 100m)
        };

        // Act
        var result = SalePricingCalculator.Calculate(
            lines: lines,
            orderDiscount: 0m,
            payments: [new PaymentInput(PaymentMethod.Cash, 100m)],
            legacyPayment: 0m,
            legacyMethod: PaymentMethod.Cash);

        // Assert
        await result.TotalAmount.Should().BeEqualTo(300m);
        await result.PaidAmount.Should().BeEqualTo(100m);
        await result.TenderedAmount.Should().BeEqualTo(100m);
        await result.ChangeAmount.Should().BeEqualTo(0m);
        await result.DueAmount.Should().BeEqualTo(200m);
    }

    [Test]
    public async Task Calculate_WithSplitPayments_AllocatesAcrossMethods()
    {
        // Arrange: Total 500, Card 200 + Cash 350 (50 change)
        var lines = new List<SalesLine>
        {
            new(ProductId: 1, Quantity: 5, UnitPrice: 100m)
        };

        // Act
        var result = SalePricingCalculator.Calculate(
            lines: lines,
            orderDiscount: 0m,
            payments:
            [
                new PaymentInput(PaymentMethod.Card, 200m),
                new PaymentInput(PaymentMethod.Cash, 350m)
            ],
            legacyPayment: 0m,
            legacyMethod: PaymentMethod.Cash);

        // Assert
        await result.TotalAmount.Should().BeEqualTo(500m);
        await result.PaidAmount.Should().BeEqualTo(500m);
        await result.TenderedAmount.Should().BeEqualTo(550m);
        await result.ChangeAmount.Should().BeEqualTo(50m);
        await result.DueAmount.Should().BeEqualTo(0m);
        await result.PaymentPlan.Length.Should().BeEqualTo(2);
        await result.PaymentPlan[0].Amount.Should().BeEqualTo(200m);
        await result.PaymentPlan[1].Amount.Should().BeEqualTo(300m);
        await result.PaymentPlan[1].Tendered.Should().BeEqualTo(350m);
    }

    [Test]
    public async Task Calculate_WhenLineTotalOverflows_ThrowsValidationException()
    {
        var lines = new List<SalesLine>
        {
            new(ProductId: 1, Quantity: 10000000000000000m, UnitPrice: 100m)
        };

        Assert.Throws<ValidationException>(() =>
        {
            SalePricingCalculator.Calculate(
                lines: lines,
                orderDiscount: 0m,
                payments: [],
                legacyPayment: 0m,
                legacyMethod: PaymentMethod.Cash);
        });
    }
}
