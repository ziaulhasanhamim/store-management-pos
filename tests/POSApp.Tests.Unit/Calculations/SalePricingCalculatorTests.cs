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

    [Test]
    public async Task Calculate_WhenCustomerDueSpecified_AllowsPaymentCoveringOrderAndPastDue()
    {
        // Line total: 850, customer past due: 2400. Total payable: 3250.
        // Customer pays 3250 in full.
        var lines = new List<SalesLine>
        {
            new(ProductId: 1, Quantity: 1, UnitPrice: 850m)
        };

        var result = SalePricingCalculator.Calculate(
            lines: lines,
            orderDiscount: 0m,
            payments: [new PaymentInput(PaymentMethod.Cash, 3250m)],
            legacyPayment: 0m,
            legacyMethod: PaymentMethod.Cash,
            customerDue: 2400m);

        await result.TotalAmount.Should().BeEqualTo(850m);
        await result.PaidAmount.Should().BeEqualTo(3250m);
        await result.TenderedAmount.Should().BeEqualTo(3250m);
        await result.ChangeAmount.Should().BeEqualTo(0m);
        await result.DueAmount.Should().BeEqualTo(0m);
    }

    [Test]
    public async Task Calculate_WhenCustomerDueSpecified_AndNoPayment_DueAmountIsOrderPlusPastDue()
    {
        // Line total: 850, customer past due: 2400, no payment => Due is 3250
        var lines = new List<SalesLine>
        {
            new(ProductId: 1, Quantity: 1, UnitPrice: 850m)
        };

        var result = SalePricingCalculator.Calculate(
            lines: lines,
            orderDiscount: 0m,
            payments: [],
            legacyPayment: 0m,
            legacyMethod: PaymentMethod.Cash,
            customerDue: 2400m);

        await result.TotalAmount.Should().BeEqualTo(850m);
        await result.PaidAmount.Should().BeEqualTo(0m);
        await result.DueAmount.Should().BeEqualTo(3250m);
    }

    [Test]
    public async Task Calculate_WhenCustomerDueSpecified_AndOrderPaid_DueAmountIsPastDue()
    {
        // Line total: 850, customer past due: 2400, customer pays 850 => Due is 2400
        var lines = new List<SalesLine>
        {
            new(ProductId: 1, Quantity: 1, UnitPrice: 850m)
        };

        var result = SalePricingCalculator.Calculate(
            lines: lines,
            orderDiscount: 0m,
            payments: [new PaymentInput(PaymentMethod.Cash, 850m)],
            legacyPayment: 0m,
            legacyMethod: PaymentMethod.Cash,
            customerDue: 2400m);

        await result.TotalAmount.Should().BeEqualTo(850m);
        await result.PaidAmount.Should().BeEqualTo(850m);
        await result.DueAmount.Should().BeEqualTo(2400m);
    }

    [Test]
    public async Task Calculate_WhenCustomerDueSpecified_WithDiscount_DueAmountCalculatesCorrectly()
    {
        // Line total: 850, discount: 50 => Total: 800, past due: 2400, payment: 800 => Due: 2400
        // Formula: previous due + new order - discount - pay = 2400 + 850 - 50 - 800 = 2400
        var lines = new List<SalesLine>
        {
            new(ProductId: 1, Quantity: 1, UnitPrice: 850m)
        };

        var result = SalePricingCalculator.Calculate(
            lines: lines,
            orderDiscount: 50m,
            payments: [new PaymentInput(PaymentMethod.Cash, 800m)],
            legacyPayment: 0m,
            legacyMethod: PaymentMethod.Cash,
            customerDue: 2400m);

        await result.TotalAmount.Should().BeEqualTo(800m);
        await result.PaidAmount.Should().BeEqualTo(800m);
        await result.DueAmount.Should().BeEqualTo(2400m);
    }

    [Test]
    public async Task Calculate_WhenRefundSale_CalculatesNegativeTotalAndZeroDueWhenRefunded()
    {
        // Refund of 550 with -550 cash payment
        var lines = new List<SalesLine>
        {
            new(ProductId: 1, Quantity: -1, UnitPrice: 550m)
        };

        var result = SalePricingCalculator.Calculate(
            lines: lines,
            orderDiscount: 0m,
            payments: [new PaymentInput(PaymentMethod.Cash, -550m)],
            legacyPayment: 0m,
            legacyMethod: PaymentMethod.Cash,
            allowRefund: true);

        await result.TotalAmount.Should().BeEqualTo(-550m);
        await result.PaidAmount.Should().BeEqualTo(-550m);
        await result.DueAmount.Should().BeEqualTo(0m);
    }
}
