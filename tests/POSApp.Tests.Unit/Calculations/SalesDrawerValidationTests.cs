using System.ComponentModel.DataAnnotations;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using TUnit.Assertions.Should;

namespace POSApp.Tests.Unit.Calculations;

public class SalesDrawerValidationTests
{
    [Test]
    public async Task SalesOptions_CarriesDefaultLocationAndOpenLocations()
    {
        var locations = new[] { new Lookup(1, "Main Store"), new Lookup(2, "North Store") };
        var options = new SalesOptions(locations, [], [])
        {
            DefaultLocationId = 2,
            OpenLocationIds = [1]
        };

        await options.DefaultLocationId.Should().BeEqualTo(2);
        await options.OpenLocationIds.Should().BeEquivalentTo(new[] { 1 });
    }

    [Test]
    public async Task CashDrawerPage_CarriesClosedTodaySession()
    {
        var closedSession = new CashSessionRow(
            Id: 42,
            Cashier: "staff1",
            OpenedAt: DateTimeOffset.UtcNow.AddHours(-8),
            ClosedAt: DateTimeOffset.UtcNow.AddMinutes(-10),
            Opening: 2000m,
            Expected: 15400m,
            Actual: 15400m,
            Difference: 0m,
            Notes: "Clean shift");

        var page = new CashDrawerPage(
            Current: null,
            Viewed: closedSession,
            Movements: [],
            Breakdown: [],
            Recent: [],
            Admin: true)
        {
            SelectedLocationId = 2,
            ClosedTodaySession = closedSession
        };

        await page.Current.Should().BeNull();
        await page.ClosedTodaySession.Should().NotBeNull();
        await page.ClosedTodaySession!.Id.Should().BeEqualTo(42);
        await page.ClosedTodaySession.Opening.Should().BeEqualTo(2000m);
        await page.ClosedTodaySession.Expected.Should().BeEqualTo(15400m);
    }
}
