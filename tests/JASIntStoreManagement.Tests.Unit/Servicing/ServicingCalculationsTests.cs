using JASIntStoreManagement.Client.DTOs;

namespace JASIntStoreManagement.Tests.Unit.Servicing;

public class ServicingCalculationsTests
{
    [Test]
    public async Task JobProfit_StandardCase_CalculatesProfitAndEvenSplit()
    {
        // Arrange: 50% split on 2000 profit (3000 bill - 1000 material)
        var input = new ServicingJobInput
        {
            BillAmount = 3000m,
            MaterialCost = 1000m,
            CommissionRate = 50m
        };

        // Assert
        await input.JobProfit.Should().BeEqualTo(2000m);
        await input.TechnicianCut.Should().BeEqualTo(1000m);
        await input.ShopProfit.Should().BeEqualTo(1000m);
        await (input.TechnicianCut + input.ShopProfit).Should().BeEqualTo(input.JobProfit);
    }

    [Test]
    public async Task JobProfit_ZeroMaterialCost_CalculatesFullProfit()
    {
        // Arrange: Pure service / labor without spare parts
        var input = new ServicingJobInput
        {
            BillAmount = 800m,
            MaterialCost = 0m,
            CommissionRate = 50m
        };

        // Assert
        await input.JobProfit.Should().BeEqualTo(800m);
        await input.TechnicianCut.Should().BeEqualTo(400m);
        await input.ShopProfit.Should().BeEqualTo(400m);
    }

    [Test]
    public async Task JobProfit_CustomCommissionRate_CalculatesAccurately()
    {
        // Arrange: 40% commission rate on 1500 profit
        var input = new ServicingJobInput
        {
            BillAmount = 2500m,
            MaterialCost = 1000m,
            CommissionRate = 40m
        };

        // Assert
        await input.JobProfit.Should().BeEqualTo(1500m);
        await input.TechnicianCut.Should().BeEqualTo(600m);
        await input.ShopProfit.Should().BeEqualTo(900m);
        await (input.TechnicianCut + input.ShopProfit).Should().BeEqualTo(input.JobProfit);
    }

    [Test]
    public async Task JobProfit_RoundingAwayFromZero_PreservesTotalBalance()
    {
        // Arrange: 33.33% commission on 100 profit
        var input = new ServicingJobInput
        {
            BillAmount = 100m,
            MaterialCost = 0m,
            CommissionRate = 33.33m
        };

        // 100 * 0.3333 = 33.33
        await input.JobProfit.Should().BeEqualTo(100m);
        await input.TechnicianCut.Should().BeEqualTo(33.33m);
        await input.ShopProfit.Should().BeEqualTo(66.67m);
        await (input.TechnicianCut + input.ShopProfit).Should().BeEqualTo(input.JobProfit);
    }

    [Test]
    public async Task JobProfit_FullTechnicianCut_ShopProfitIsZero()
    {
        // Arrange: 100% technician commission
        var input = new ServicingJobInput
        {
            BillAmount = 1200m,
            MaterialCost = 400m,
            CommissionRate = 100m
        };

        await input.JobProfit.Should().BeEqualTo(800m);
        await input.TechnicianCut.Should().BeEqualTo(800m);
        await input.ShopProfit.Should().BeEqualTo(0m);
    }

    [Test]
    public async Task JobProfit_ZeroTechnicianCommission_ShopGetsFullProfit()
    {
        // Arrange: 0% commission (in-house store work)
        var input = new ServicingJobInput
        {
            BillAmount = 1500m,
            MaterialCost = 500m,
            CommissionRate = 0m
        };

        await input.JobProfit.Should().BeEqualTo(1000m);
        await input.TechnicianCut.Should().BeEqualTo(0m);
        await input.ShopProfit.Should().BeEqualTo(1000m);
    }

    [Test]
    public async Task JobProfit_AtCost_ProfitsAreZero()
    {
        // Arrange: Customer was charged exactly parts cost
        var input = new ServicingJobInput
        {
            BillAmount = 750m,
            MaterialCost = 750m,
            CommissionRate = 50m
        };

        await input.JobProfit.Should().BeEqualTo(0m);
        await input.TechnicianCut.Should().BeEqualTo(0m);
        await input.ShopProfit.Should().BeEqualTo(0m);
    }

    [Test]
    [Arguments(100, 20, 50)]
    [Arguments(250.50, 100.25, 45)]
    [Arguments(1299.99, 450.50, 60)]
    [Arguments(5000, 1250, 35)]
    [Arguments(87.50, 12.25, 50)]
    public async Task TechnicianCutAndShopProfit_AlwaysSumToJobProfit(double bill, double material, double commission)
    {
        var input = new ServicingJobInput
        {
            BillAmount = (decimal)bill,
            MaterialCost = (decimal)material,
            CommissionRate = (decimal)commission
        };

        var sum = input.TechnicianCut + input.ShopProfit;
        await sum.Should().BeEqualTo(input.JobProfit);
    }
}
