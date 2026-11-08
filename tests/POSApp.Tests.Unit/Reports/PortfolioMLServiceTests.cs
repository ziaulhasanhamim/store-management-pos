using POSApp.Client.DTOs;
using POSApp.Services.ML;

namespace POSApp.Tests.Unit.Reports;

public class PortfolioMLServiceTests
{
    private readonly PortfolioMLService _service = new();

    private static IReadOnlyList<ProductRawStat> CreateMockProductStats(int count)
    {
        var list = new List<ProductRawStat>();
        for (int i = 1; i <= count; i++)
        {
            decimal vol = i % 2 == 0 ? 100m + i * 10 : 5m + i;
            decimal margin = i % 3 == 0 ? 35m + (i % 5) : 8m + (i % 4);
            decimal price = 50m;
            decimal cost = price * (1m - margin / 100m);
            decimal rev = vol * price;
            decimal profit = rev - (vol * cost);

            list.Add(new ProductRawStat(
                ProductId: i,
                ProductName: $"Product {i}",
                CategoryName: i % 2 == 0 ? "Electronics" : "Accessories",
                Volume: vol,
                Revenue: rev,
                Cost: vol * cost,
                Profit: profit,
                MarginPercent: margin,
                UnitCost: cost,
                SellingPrice: price,
                OrderCount: (int)Math.Max(1, vol / 3),
                RecentVolume: vol * 0.6m,
                PreviousVolume: vol * 0.4m,
                RecentMargin: margin,
                PreviousMargin: margin
            ));
        }
        return list;
    }

    [Test]
    public async Task AnalyzePortfolio_WithEmptyStats_ReturnsEmptyReport()
    {
        var result = _service.AnalyzePortfolio(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31),
            locationId: null,
            locations: [],
            stats: [],
            mode: "ai");

        await result.Items.Count.Should().BeEqualTo(0);
        await result.IsMlPowered.Should().BeFalse();
        await result.IsFallback.Should().BeFalse();
    }

    [Test]
    public async Task AnalyzePortfolio_WithFewerThanTenProducts_TriggersGracefulFallback()
    {
        var stats = CreateMockProductStats(5);

        var result = _service.AnalyzePortfolio(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31),
            locationId: null,
            locations: [],
            stats: stats,
            mode: "ai");

        await result.Items.Count.Should().BeEqualTo(5);
        await result.IsMlPowered.Should().BeFalse();
        await result.IsFallback.Should().BeTrue();
    }

    [Test]
    public async Task AnalyzePortfolio_WithClassicMode_UsesMedianRuleBased()
    {
        var stats = CreateMockProductStats(15);

        var result = _service.AnalyzePortfolio(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31),
            locationId: null,
            locations: [],
            stats: stats,
            mode: "classic");

        await result.Items.Count.Should().BeEqualTo(15);
        await result.IsMlPowered.Should().BeFalse();
        await result.IsFallback.Should().BeFalse();
        await result.AnalysisMode.Should().BeEqualTo("classic");
    }

    [Test]
    public async Task AnalyzePortfolio_WithSufficientDataAndAiMode_PerformsKMeansClustering()
    {
        var stats = CreateMockProductStats(20);

        var result = _service.AnalyzePortfolio(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31),
            locationId: null,
            locations: [],
            stats: stats,
            mode: "ai");

        await result.Items.Count.Should().BeEqualTo(20);
        await result.IsMlPowered.Should().BeTrue();
        await result.IsFallback.Should().BeFalse();
        await result.AnalysisMode.Should().BeEqualTo("ai");
    }

    [Test]
    public async Task AnalyzePortfolio_ForecastAndTrend_IdentifiesRisingAndSlippingProducts()
    {
        var stats = new List<ProductRawStat>
        {
            // Rapidly rising product: previous volume low (2), recent volume very high (50), margin high (40%)
            new(1, "Rising Star Item", "Cat1", 52m, 2600m, 1560m, 1040m, 40m, 30m, 50m, 25,
                RecentVolume: 50m, PreviousVolume: 2m, RecentMargin: 40m, PreviousMargin: 38m),

            // Slipping product: previous volume high (100), recent volume collapsed (5), margin low (10%)
            new(2, "Slipping Item", "Cat1", 105m, 2100m, 1890m, 210m, 10m, 18m, 20m, 20,
                RecentVolume: 5m, PreviousVolume: 100m, RecentMargin: 9m, PreviousMargin: 11m)
        };

        // Add 10 filler items to avoid fallback
        for (int i = 3; i <= 15; i++)
        {
            stats.Add(new(i, $"Item {i}", "Cat1", 20m + i, 1000m, 700m, 300m, 30m, 15m, 25m, 10,
                RecentVolume: 10m, PreviousVolume: 10m, RecentMargin: 30m, PreviousMargin: 30m));
        }

        var result = _service.AnalyzePortfolio(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31),
            locationId: null,
            locations: [],
            stats: stats,
            mode: "ai");

        var rising = result.Items.FirstOrDefault(i => i.ProductId == 1);
        var slipping = result.Items.FirstOrDefault(i => i.ProductId == 2);

        await (rising != null).Should().BeTrue();
        await (slipping != null).Should().BeTrue();
        await rising!.TrendStatus.Should().BeEqualTo(ProductTrendStatus.RisingToStar);
    }
}
