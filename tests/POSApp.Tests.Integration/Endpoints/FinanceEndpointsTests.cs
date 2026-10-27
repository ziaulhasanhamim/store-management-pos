using System.Net;
using POSApp.Services;
using POSApp.Tests.Integration.Infrastructure;

namespace POSApp.Tests.Integration.Endpoints;

public class FinanceEndpointsTests : PosIntegrationTestBase
{
    [Test]
    public async Task ExportClosingCsv_IsRemoved_ReturnsNotFound()
    {
        var today = AccountingCalendar.Today(null);
        var response = await AdminClient.GetAsync($"/api/finance/closing.csv?date={today:yyyy-MM-dd}");

        await response.StatusCode.Should().BeEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ExportClosingExcel_WhenAnonymous_ReturnsUnauthorized()
    {
        var today = AccountingCalendar.Today(null);
        var response = await AnonymousClient.GetAsync($"/api/finance/closing.xlsx?date={today:yyyy-MM-dd}");

        await response.StatusCode.Should().BeEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task ExportClosingExcel_WhenAdminAndDay_ReturnsXlsxFile()
    {
        var today = AccountingCalendar.Today(null);
        var response = await AdminClient.GetAsync($"/api/finance/closing.xlsx?date={today:yyyy-MM-dd}");

        await response.StatusCode.Should().BeEqualTo(HttpStatusCode.OK);
        await response.Content.Headers.ContentType!.MediaType.Should().BeEqualTo("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        await bytes.Length.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task ExportClosingExcel_WhenAdminAndMonth_ReturnsXlsxFileWithLocationsSheet()
    {
        var today = AccountingCalendar.Today(null);
        var response = await AdminClient.GetAsync($"/api/finance/closing.xlsx?date={today:yyyy-MM-01}&period=month");

        await response.StatusCode.Should().BeEqualTo(HttpStatusCode.OK);
        await response.Content.Headers.ContentType!.MediaType.Should().BeEqualTo("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        await bytes.Length.Should().BeGreaterThan(0);

        using var stream = new MemoryStream(bytes);
        using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
        await workbook.Worksheets.Contains("Overview").Should().BeEqualTo(true);
        await workbook.Worksheets.Contains("Working Days").Should().BeEqualTo(true);
        await workbook.Worksheets.Contains("Suppliers").Should().BeEqualTo(true);
    }

    [Test]
    public async Task ExportClosingExcel_WhenAdminWithLocationFilter_ReturnsLocationScopedXlsx()
    {
        var today = AccountingCalendar.Today(null);
        var response = await AdminClient.GetAsync($"/api/finance/closing.xlsx?date={today:yyyy-MM-01}&period=month&locationId=1");

        await response.StatusCode.Should().BeEqualTo(HttpStatusCode.OK);
        await response.Content.Headers.ContentType!.MediaType.Should().BeEqualTo("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        await bytes.Length.Should().BeGreaterThan(0);
    }
}
