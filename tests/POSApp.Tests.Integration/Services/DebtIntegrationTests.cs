using POSApp.Services;
using POSApp.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace POSApp.Tests.Integration.Services;

public class DebtIntegrationTests : PosIntegrationTestBase
{
    [Test]
    public async Task ReadAsync_RealPostgres_DoesNotThrow()
    {
        await using var scope = CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<DebtService>();
        
        var page = await service.ReadAsync(AdminUser, historyPage: 1);
        await page.Should().NotBeNull();
    }

    [Test]
    public async Task DebtPayments_Endpoint_GetAsync_ReturnsOk()
    {
        var response = await AdminClient.GetAsync("/finance/debt/payments");
        await response.IsSuccessStatusCode.Should().BeTrue();
    }
}
