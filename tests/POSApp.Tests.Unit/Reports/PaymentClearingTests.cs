using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using TUnit.Assertions.Should;

namespace POSApp.Tests.Unit.Reports;

public class PaymentClearingTests
{
    [Test]
    public async Task AwaitingPayoutSummary_ComputesTotalAndCounts()
    {
        var oldest = new DateOnly(2026, 11, 1);
        var summary = new AwaitingPayoutSummary(
            CardAmount: 12500m,
            MobileWalletAmount: 3500m,
            UnsettledCount: 14,
            OldestDate: oldest);

        await summary.CardAmount.Should().BeEqualTo(12500m);
        await summary.MobileWalletAmount.Should().BeEqualTo(3500m);
        await summary.TotalAmount.Should().BeEqualTo(16000m);
        await summary.UnsettledCount.Should().BeEqualTo(14);
        await summary.OldestDate.Should().BeEqualTo(oldest);
    }

    [Test]
    public async Task PayoutCalculation_CalculatesFeeAndPercentageAccurately()
    {
        decimal gross = 50000m;
        decimal net = 49000m;
        decimal fee = gross - net;
        decimal feePct = Math.Round((fee / gross) * 100m, 2);

        await fee.Should().BeEqualTo(1000m);
        await feePct.Should().BeEqualTo(2.00m);
    }

    [Test]
    public async Task ClearingAgeing_CorrectlySumsTotalAcrossBuckets()
    {
        var ageing = new ClearingAgeingDto(
            LocationId: 1,
            LocationName: "Main Store",
            Method: PaymentMethod.Card,
            Under3Days: 4500m,
            From3To7Days: 2000m,
            Over7Days: 500m,
            Total: 7000m);

        await ageing.Total.Should().BeEqualTo(ageing.Under3Days + ageing.From3To7Days + ageing.Over7Days);
        await ageing.Over7Days.Should().BeEqualTo(500m);
        await ageing.LocationName.Should().BeEqualTo("Main Store");
    }

    [Test]
    public async Task ClearingSaleItem_TracksDaysWaiting()
    {
        var item = new ClearingSaleItemDto(
            Id: 101,
            PaymentId: 205,
            SaleId: 301,
            BusinessDate: new DateOnly(2026, 11, 2),
            SoldAt: new DateTimeOffset(2026, 11, 2, 14, 30, 0, TimeSpan.Zero),
            StockLocationId: 1,
            StockLocationName: "Main Store",
            Method: PaymentMethod.Card,
            GrossAmount: 1500m,
            DaysWaiting: 5);

        await item.GrossAmount.Should().BeEqualTo(1500m);
        await item.DaysWaiting.Should().BeEqualTo(5);
        await item.StockLocationName.Should().BeEqualTo("Main Store");
    }
}
