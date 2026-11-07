using System.Net;
using Microsoft.Extensions.DependencyInjection;
using POSApp.Client.Security;
using POSApp.Services;
using POSApp.Tests.Integration.Infrastructure;

namespace POSApp.Tests.Integration.Services;

public class UserAdministrationIntegrationTests : PosIntegrationTestBase
{
    [Test]
    public async Task FindAsync_LoadsUser2Details_WithoutException()
    {
        await using var scope = CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<UserAdministration>();

        var user = await admin.FindAsync(AdminUser, 2);

        await Assert.That(user).IsNotNull();
        await Assert.That(user!.Id).IsEqualTo(2);
    }

    [Test]
    public async Task GetUserEditPage_ReturnsSuccessStatusCode_AndContainsUserForm()
    {
        var response = await AdminClient.GetAsync("/users/2/edit");
        var content = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(content).DoesNotContain("We couldn't load this user");
        await Assert.That(content).Contains("Edit team member");
    }
}
