using System.ComponentModel.DataAnnotations;
using POSApp.Services;

namespace POSApp.Tests.Unit.Reports;

public class AccountingCalendarTests
{
    private static readonly TimeZoneInfo DhakaZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dhaka");

    [Test]
    public async Task ResolveRange_WhenPeriodIsToday_ReturnsSingleDayRange()
    {
        // 2026-10-26 12:00:00 UTC is 18:00:00 in Dhaka (same date: 2026-10-26)
        var now = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
        var range = ReportService.ResolveRange(null, null, null, DhakaZone, now);

        await range.Start.Should().BeEqualTo(new DateOnly(2026, 10, 26));
        await range.End.Should().BeEqualTo(new DateOnly(2026, 10, 26));
    }

    [Test]
    public async Task ResolveRange_WhenPeriodIsYesterday_ReturnsDayBeforeToday()
    {
        var now = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
        var range = ReportService.ResolveRange("yesterday", null, null, DhakaZone, now);

        await range.Start.Should().BeEqualTo(new DateOnly(2026, 10, 25));
        await range.End.Should().BeEqualTo(new DateOnly(2026, 10, 25));
    }

    [Test]
    public async Task ResolveRange_WhenPeriodIsWeek_ReturnsSevenDaySpanEndingToday()
    {
        var now = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
        var range = ReportService.ResolveRange("week", null, null, DhakaZone, now);

        await range.Start.Should().BeEqualTo(new DateOnly(2026, 10, 20));
        await range.End.Should().BeEqualTo(new DateOnly(2026, 10, 26));
    }

    [Test]
    public async Task ResolveRange_WhenPeriodIsMonth_StartsAtFirstOfMonth()
    {
        var now = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
        var range = ReportService.ResolveRange("month", null, null, DhakaZone, now);

        await range.Start.Should().BeEqualTo(new DateOnly(2026, 10, 1));
        await range.End.Should().BeEqualTo(new DateOnly(2026, 10, 26));
    }

    [Test]
    public async Task ResolveRange_WhenPeriodIsLastMonth_CoversEntirePrecedingMonth()
    {
        var now = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
        var range = ReportService.ResolveRange("lastmonth", null, null, DhakaZone, now);

        await range.Start.Should().BeEqualTo(new DateOnly(2026, 9, 1));
        await range.End.Should().BeEqualTo(new DateOnly(2026, 9, 30));
    }

    [Test]
    public async Task ResolveRange_WhenPeriodIsYear_StartsJan1()
    {
        var now = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
        var range = ReportService.ResolveRange("year", null, null, DhakaZone, now);

        await range.Start.Should().BeEqualTo(new DateOnly(2026, 1, 1));
        await range.End.Should().BeEqualTo(new DateOnly(2026, 10, 26));
    }

    [Test]
    public async Task ResolveRange_WhenPeriodIsCustom_UsesProvidedDates()
    {
        var now = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
        var start = new DateOnly(2026, 10, 5);
        var end = new DateOnly(2026, 10, 15);
        var range = ReportService.ResolveRange("custom", start, end, DhakaZone, now);

        await range.Start.Should().BeEqualTo(start);
        await range.End.Should().BeEqualTo(end);
    }

    [Test]
    public async Task ResolveRange_WhenCustomEndBeforeStart_ThrowsValidationException()
    {
        var now = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
        var start = new DateOnly(2026, 10, 15);
        var end = new DateOnly(2026, 10, 5);

        Assert.Throws<ValidationException>(() =>
            ReportService.ResolveRange("custom", start, end, DhakaZone, now));
    }

    [Test]
    public async Task ValidateExpected_WhenCurrentMatchesExpected_DoesNotThrow()
    {
        var date = new DateOnly(2026, 10, 26);
        AccountingCalendar.ValidateExpected(date, date);
        AccountingCalendar.ValidateExpected(date, null);
    }

    [Test]
    public async Task ValidateExpected_WhenCurrentDiffersFromExpected_ThrowsValidationException()
    {
        var current = new DateOnly(2026, 10, 26);
        var expected = new DateOnly(2026, 10, 25);

        Assert.Throws<ValidationException>(() =>
            AccountingCalendar.ValidateExpected(current, expected));
    }
}
