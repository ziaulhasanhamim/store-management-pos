using System.ComponentModel.DataAnnotations;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Persistence.Entities;
using POSApp.Services;

namespace POSApp.Tests.Unit.Calculations;

public class PaymentAllocationTests
{
    [Test]
    public async Task Allocate_WhenLegacyAmountSpecified_ReturnsSinglePayment()
    {
        var result = PaymentAllocation.Allocate(
            total: 100m,
            payments: null,
            legacyAmount: 100m,
            legacyMethod: PaymentMethod.Cash,
            allowChange: true);

        await result.Length.Should().BeEqualTo(1);
        await result[0].Method.Should().BeEqualTo(PaymentMethod.Cash);
        await result[0].Amount.Should().BeEqualTo(100m);
        await result[0].Tendered.Should().BeEqualTo(100m);
    }

    [Test]
    public async Task Allocate_WhenZeroPayments_ReturnsEmptyArray()
    {
        var result = PaymentAllocation.Allocate(
            total: 100m,
            payments: [],
            legacyAmount: 0m,
            legacyMethod: PaymentMethod.Cash,
            allowChange: true);

        await result.Length.Should().BeEqualTo(0);
    }

    [Test]
    public async Task Allocate_WhenMoreThanTwentyPayments_ThrowsValidationException()
    {
        var payments = Enumerable.Range(1, 21)
            .Select(_ => new PaymentInput(PaymentMethod.Cash, 10m))
            .ToArray();

        Assert.Throws<ValidationException>(() =>
            PaymentAllocation.Allocate(
                total: 210m,
                payments: payments,
                legacyAmount: 0m,
                legacyMethod: PaymentMethod.Cash,
                allowChange: true));
    }

    [Test]
    public async Task Allocate_WhenPaymentAmountIsZeroOrNegative_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() =>
            PaymentAllocation.Allocate(
                total: 100m,
                payments: [new PaymentInput(PaymentMethod.Cash, 0m)],
                legacyAmount: 0m,
                legacyMethod: PaymentMethod.Cash,
                allowChange: true));

        Assert.Throws<ValidationException>(() =>
            PaymentAllocation.Allocate(
                total: 100m,
                payments: [new PaymentInput(PaymentMethod.Cash, -50m)],
                legacyAmount: 0m,
                legacyMethod: PaymentMethod.Cash,
                allowChange: true));
    }

    [Test]
    public async Task Allocate_WhenCashPaymentExceedsRemainingWithChangeAllowed_CapsAppliedAndPreservesTendered()
    {
        var result = PaymentAllocation.Allocate(
            total: 150m,
            payments: [new PaymentInput(PaymentMethod.Cash, 200m)],
            legacyAmount: 0m,
            legacyMethod: PaymentMethod.Cash,
            allowChange: true);

        await result.Length.Should().BeEqualTo(1);
        await result[0].Amount.Should().BeEqualTo(150m);
        await result[0].Tendered.Should().BeEqualTo(200m);
    }

    [Test]
    public async Task Allocate_WhenNonCashPaymentExceedsRemaining_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() =>
            PaymentAllocation.Allocate(
                total: 100m,
                payments: [new PaymentInput(PaymentMethod.Card, 120m)],
                legacyAmount: 0m,
                legacyMethod: PaymentMethod.Card,
                allowChange: true));
    }

    [Test]
    public async Task Allocate_WhenPaymentAddedAfterOrderPaidInFull_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() =>
            PaymentAllocation.Allocate(
                total: 100m,
                payments:
                [
                    new PaymentInput(PaymentMethod.Cash, 100m),
                    new PaymentInput(PaymentMethod.Cash, 50m)
                ],
                legacyAmount: 0m,
                legacyMethod: PaymentMethod.Cash,
                allowChange: true));
    }

    [Test]
    public async Task Allocate_InRefundMode_HandlesNegativeTotals()
    {
        // Refund of 80 with -80 cash returned
        var result = PaymentAllocation.Allocate(
            total: -80m,
            payments: [new PaymentInput(PaymentMethod.Cash, -80m)],
            legacyAmount: 0m,
            legacyMethod: PaymentMethod.Cash,
            allowChange: false,
            allowRefund: true);

        await result.Length.Should().BeEqualTo(1);
        await result[0].Amount.Should().BeEqualTo(-80m);
        await result[0].Tendered.Should().BeEqualTo(-80m);
    }
}
