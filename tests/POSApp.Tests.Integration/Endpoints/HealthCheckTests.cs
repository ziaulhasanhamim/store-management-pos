using System.Net;
using POSApp.Tests.Integration.Infrastructure;

namespace POSApp.Tests.Integration.Endpoints;

public class HealthCheckTests : PosIntegrationTestBase
{
    [Test]
    public async Task HealthCheck_ReturnsHealthy()
    {
        var response = await AnonymousClient.GetAsync("/health");

        await response.StatusCode.Should().BeEqualTo(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        await content.Should().BeEqualTo("Healthy");
    }
}
