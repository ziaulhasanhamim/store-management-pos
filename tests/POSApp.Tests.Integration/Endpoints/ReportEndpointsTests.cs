using System.Net;
using POSApp.Tests.Integration.Infrastructure;

namespace POSApp.Tests.Integration.Endpoints;

public class ReportEndpointsTests : PosIntegrationTestBase
{
    [Test]
    public async Task ExportReport_WhenAnonymous_ReturnsUnauthorized()
    {
        var response = await AnonymousClient.GetAsync("/api/reports/export/products");

        await response.StatusCode.Should().BeEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task ExportProductsReport_WhenAdmin_ReturnsCsvReport()
    {
        var response = await AdminClient.GetAsync("/api/reports/export/products");

        await response.StatusCode.Should().BeEqualTo(HttpStatusCode.OK);
        var csv = await response.Content.ReadAsStringAsync();
        await csv.Should().NotBeNull();
        await csv.Contains("Item ID").Should().BeEqualTo(true);
        await csv.Contains("Selling price").Should().BeEqualTo(true);
    }

    [Test]
    public async Task ExportSalesReport_WhenAdmin_ReturnsCsvReport()
    {
        var response = await AdminClient.GetAsync("/api/reports/export/sales?period=today");

        await response.StatusCode.Should().BeEqualTo(HttpStatusCode.OK);
        var csv = await response.Content.ReadAsStringAsync();
        await csv.Should().NotBeNull();
        await csv.Contains("Receipt").Should().BeEqualTo(true);
    }
}
