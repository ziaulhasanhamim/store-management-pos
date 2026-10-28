using JASIntStoreManagement.Client.DTOs;

namespace JASIntStoreManagement.Tests.Unit.Servicing;

public class ServicingCalculationsTests
{
    [Test]
    public async Task DailyServicingProfit_StandardCase_CalculatesProfitAndEvenSplit()
    {
        // Arrange: 50% split on 8000 profit (12000 total servicing - 4000 parts cost)
        var input = new MobileServicingEntryInput
        {
            TotalAmount = 12000m,
            TotalCost = 4000m,
            CommissionRate = 50m
        };

        // Assert
        await input.Profit.Should().BeEqualTo(8000m);
        await input.TechnicianCut.Should().BeEqualTo(4000m);
        await input.ShopProfit.Should().BeEqualTo(4000m);
        await (input.TechnicianCut + input.ShopProfit).Should().BeEqualTo(input.Profit);
    }

    [Test]
    public async Task DailyServicingProfit_ZeroMaterialCost_CalculatesFullProfit()
    {
        // Arrange: Pure service / labor without spare parts cost
        var input = new MobileServicingEntryInput
        {
            TotalAmount = 2500m,
            TotalCost = 0m,
            CommissionRate = 50m
        };

        // Assert
        await input.Profit.Should().BeEqualTo(2500m);
        await input.TechnicianCut.Should().BeEqualTo(1250m);
        await input.ShopProfit.Should().BeEqualTo(1250m);
    }

    [Test]
    public async Task DailyServicingProfit_CustomCommissionRate_CalculatesAccurately()
    {
        // Arrange: 40% commission rate on 5000 profit
        var input = new MobileServicingEntryInput
        {
            TotalAmount = 8000m,
            TotalCost = 3000m,
            CommissionRate = 40m
        };

        // Assert
        await input.Profit.Should().BeEqualTo(5000m);
        await input.TechnicianCut.Should().BeEqualTo(2000m);
        await input.ShopProfit.Should().BeEqualTo(3000m);
        await (input.TechnicianCut + input.ShopProfit).Should().BeEqualTo(input.Profit);
    }

    [Test]
    public async Task DailyServicingProfit_RoundingAwayFromZero_PreservesTotalBalance()
    {
        // Arrange: 33.33% commission on 100 profit
        var input = new MobileServicingEntryInput
        {
            TotalAmount = 100m,
            TotalCost = 0m,
            CommissionRate = 33.33m
        };

        // 100 * 0.3333 = 33.33
        await input.Profit.Should().BeEqualTo(100m);
        await input.TechnicianCut.Should().BeEqualTo(33.33m);
        await input.ShopProfit.Should().BeEqualTo(66.67m);
        await (input.TechnicianCut + input.ShopProfit).Should().BeEqualTo(input.Profit);
    }

    [Test]
    public async Task DailyServicingProfit_FullTechnicianCut_ShopProfitIsZero()
    {
        // Arrange: 100% technician commission
        var input = new MobileServicingEntryInput
        {
            TotalAmount = 3000m,
            TotalCost = 1000m,
            CommissionRate = 100m
        };

        await input.Profit.Should().BeEqualTo(2000m);
        await input.TechnicianCut.Should().BeEqualTo(2000m);
        await input.ShopProfit.Should().BeEqualTo(0m);
    }

    [Test]
    public async Task DailyServicingProfit_ZeroTechnicianCommission_ShopGetsFullProfit()
    {
        // Arrange: 0% commission (in-house store work)
        var input = new MobileServicingEntryInput
        {
            TotalAmount = 4000m,
            TotalCost = 1500m,
            CommissionRate = 0m
        };

        await input.Profit.Should().BeEqualTo(2500m);
        await input.TechnicianCut.Should().BeEqualTo(0m);
        await input.ShopProfit.Should().BeEqualTo(2500m);
    }

    [Test]
    public async Task DailyServicingProfit_AtCost_ProfitsAreZero()
    {
        // Arrange: Servicing was done at exact parts cost
        var input = new MobileServicingEntryInput
        {
            TotalAmount = 1500m,
            TotalCost = 1500m,
            CommissionRate = 50m
        };

        await input.Profit.Should().BeEqualTo(0m);
        await input.TechnicianCut.Should().BeEqualTo(0m);
        await input.ShopProfit.Should().BeEqualTo(0m);
    }

    [Test]
    [Arguments(1000, 200, 50)]
    [Arguments(2500.50, 1000.25, 45)]
    [Arguments(12999.99, 4500.50, 60)]
    [Arguments(50000, 12500, 35)]
    [Arguments(875.50, 120.25, 50)]
    public async Task TechnicianCutAndShopProfit_AlwaysSumToDayProfit(double totalAmount, double totalCost, double commission)
    {
        var input = new MobileServicingEntryInput
        {
            TotalAmount = (decimal)totalAmount,
            TotalCost = (decimal)totalCost,
            CommissionRate = (decimal)commission
        };

        var sum = input.TechnicianCut + input.ShopProfit;
        await sum.Should().BeEqualTo(input.Profit);
    }
}
