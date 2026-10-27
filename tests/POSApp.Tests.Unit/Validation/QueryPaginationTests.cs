using POSApp.Services;

namespace POSApp.Tests.Unit.Validation;

public class QueryPaginationTests
{
    [Test]
    public async Task Size_WhenStandardSizeRequested_ReturnsSameSize()
    {
        await QueryPagination.Size(10).Should().BeEqualTo(10);
        await QueryPagination.Size(30).Should().BeEqualTo(30);
        await QueryPagination.Size(50).Should().BeEqualTo(50);
        await QueryPagination.Size(100).Should().BeEqualTo(100);
        await QueryPagination.Size(250).Should().BeEqualTo(250);
    }

    [Test]
    public async Task Size_WhenNonStandardSizeRequested_DefaultsToThirty()
    {
        await QueryPagination.Size(0).Should().BeEqualTo(30);
        await QueryPagination.Size(-5).Should().BeEqualTo(30);
        await QueryPagination.Size(25).Should().BeEqualTo(30);
        await QueryPagination.Size(500).Should().BeEqualTo(30);
    }
}
