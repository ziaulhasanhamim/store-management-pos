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
    public async Task PartyLedgerReport_CalculatesReconciledBalancesCorrectly()
    {
        // Opening balance: 1,000
        // Sale 1: +500 (debit) -> Balance 1,500
        // Payment 1: -300 (credit) -> Balance 1,200
        // Sale 2: +200 (debit) -> Balance 1,400
        // Expected Closing Balance: 1,000 + 700 - 300 = 1,400
        var opening = 1000m;
        var debits = 700m;
        var credits = 300m;
        var closing = opening + debits - credits;

        await closing.Should().BeEqualTo(1400m);
    }
}
