using POSApp.Client.DTOs;

namespace POSApp.Tests.Unit.Reports;

public class BankAndSupplierPaymentTests
{
    [Test]
    public async Task BankPage_ContainsMonthOpeningAndMonthDate()
    {
        var history = Array.Empty<BankRow>();
        var page = new BankPage(
            Opening: 5000m,
            Balance: 12500m,
            OpeningDate: new DateOnly(2026, 1, 1),
            History: history,
            MonthOpening: 9500m,
            MonthDate: new DateOnly(2026, 10, 1));

        await page.Opening.Should().BeEqualTo(5000m);
        await page.Balance.Should().BeEqualTo(12500m);
        await page.MonthOpening.Should().BeEqualTo(9500m);
        await page.MonthDate.Should().BeEqualTo(new DateOnly(2026, 10, 1));
    }

    [Test]
    public async Task BankPage_DefaultsMonthOpeningToZeroWhenOmitted()
    {
        var page = new BankPage(1000m, 2000m, null, Array.Empty<BankRow>());
        await page.MonthOpening.Should().BeEqualTo(0m);
        await page.MonthDate.Should().BeNull();
    }
}
