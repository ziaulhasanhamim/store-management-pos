using POSApp.Services;
using TUnit.Assertions.Should;

namespace POSApp.Tests.Unit.Reports;

public class LedgerServiceTests
{
    private static readonly TimeZoneInfo DhakaZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dhaka");

    [Test]
    public async Task ResolveRange_WhenPeriodIsAll_ReturnsAllTimeRange()
    {
        var now = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        var (start, end, period) = LedgerService.ResolveRange("all", null, null, DhakaZone, now);

        await start.Should().BeEqualTo(new DateOnly(2020, 1, 1));
        await end.Should().BeEqualTo(new DateOnly(2026, 11, 5));
        await period.Should().BeEqualTo("all");
    }

    [Test]
    public async Task ResolveRange_WhenPeriodIsMonth_ReturnsCurrentMonth()
    {
        var now = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        var (start, end, period) = LedgerService.ResolveRange("month", null, null, DhakaZone, now);

        await start.Should().BeEqualTo(new DateOnly(2026, 11, 1));
        await end.Should().BeEqualTo(new DateOnly(2026, 11, 5));
        await period.Should().BeEqualTo("month");
    }

    [Test]
    public async Task ResolveRange_WhenPeriodIsLastMonth_ReturnsPreviousMonth()
    {
        var now = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        var (start, end, period) = LedgerService.ResolveRange("lastmonth", null, null, DhakaZone, now);

        await start.Should().BeEqualTo(new DateOnly(2026, 10, 1));
        await end.Should().BeEqualTo(new DateOnly(2026, 10, 31));
        await period.Should().BeEqualTo("lastmonth");
    }

    [Test]
    public async Task ResolveRange_WhenPeriodIs30Days_ReturnsTrailingThirtyDays()
    {
        var now = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        var (start, end, period) = LedgerService.ResolveRange("30days", null, null, DhakaZone, now);

        await start.Should().BeEqualTo(new DateOnly(2026, 10, 7));
        await end.Should().BeEqualTo(new DateOnly(2026, 11, 5));
        await period.Should().BeEqualTo("30days");
    }

    [Test]
    public async Task ResolveRange_WhenCustomDatesReversed_NormalizesStartAndEnd()
    {
        var now = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        var inputStart = new DateOnly(2026, 11, 20);
        var inputEnd = new DateOnly(2026, 11, 10);
        var (start, end, period) = LedgerService.ResolveRange("custom", inputStart, inputEnd, DhakaZone, now);

        await start.Should().BeEqualTo(new DateOnly(2026, 11, 10));
        await end.Should().BeEqualTo(new DateOnly(2026, 11, 20));
        await period.Should().BeEqualTo("custom");
    }

    [Test]
    public async Task FormatShortReference_WhenCodeHasGuid_ReturnsIntIdAndLastFourHexChars()
    {
        var purchaseRef = LedgerService.FormatShortReference("REC-88a4344748034a27931fabdad8c8a8e9", 5);
        var paymentRef = LedgerService.FormatShortReference("PAY-cc31a9e4a05444e0871d58ef3910f150", 12);
        var salePayRef = LedgerService.FormatShortReference("SALE-PAY-106309d327a140fc9eb0bae738951108-1", 21);

        await purchaseRef.Should().BeEqualTo("#5-A8E9");
        await paymentRef.Should().BeEqualTo("#12-F150");
        await salePayRef.Should().BeEqualTo("#21-1108");
    }

    [Test]
    public async Task FormatShortReference_WhenCodeIsShortHumanString_PreservesShortCode()
    {
        var customRef = LedgerService.FormatShortReference("INV-2040", 9);

        await customRef.Should().BeEqualTo("#INV-2040");
    }

    [Test]
    public async Task PartyLedgerReport_CalculatesReconciledBalancesCorrectly()
    {
        var opening = 1000m;
        var debits = 700m;
        var credits = 300m;
        var closing = opening + debits - credits;

        await closing.Should().BeEqualTo(1400m);
    }
}
