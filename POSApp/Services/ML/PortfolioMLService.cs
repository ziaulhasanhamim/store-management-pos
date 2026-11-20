using Microsoft.Extensions.Caching.Memory;
using Microsoft.ML;
using Microsoft.ML.Data;
using POSApp.Client.DTOs;

namespace POSApp.Services.ML;

/// <summary>
/// Raw product sales statistics used as input for ML clustering and trend velocity.
/// </summary>
public sealed record ProductRawStat(
    int ProductId,
    string ProductName,
    string CategoryName,
    decimal Volume,
    decimal Revenue,
    decimal Cost,
    decimal Profit,
    decimal MarginPercent,
    decimal UnitCost,
    decimal SellingPrice,
    int OrderCount,
    decimal RecentVolume = 0m,
    decimal PreviousVolume = 0m,
    decimal RecentMargin = 0m,
    decimal PreviousMargin = 0m);

/// <summary>
/// Machine learning feature vector for product portfolio clustering.
/// </summary>
public sealed class ProductClusterInput
{
    public float Volume { get; set; }
    public float Revenue { get; set; }
    public float Margin { get; set; }
    public float Profit { get; set; }
    public float UnitCost { get; set; }
    public float SellingPrice { get; set; }
    public float OrderCount { get; set; }
}

/// <summary>
/// ML.NET K-Means cluster assignment prediction.
/// </summary>
public sealed class ProductClusterPrediction
{
    [ColumnName("PredictedLabel")]
    public uint SelectedClusterId { get; set; }

    [ColumnName("Score")]
    public float[]? Distances { get; set; }
}

/// <summary>
/// Isolated Machine Learning service providing K-Means portfolio clustering
/// and trend velocity forecasting using ML.NET.
/// 
/// Runs strictly on-demand for the Profit Margin Matrix report; does not introduce
/// background overhead or dependencies to the rest of the application.
/// </summary>
public sealed class PortfolioMLService
{
    private readonly IMemoryCache? _cache;
    private const int MinimumProductsForClustering = 10;

    public PortfolioMLService(IMemoryCache? cache = null)
    {
        _cache = cache;
    }

    /// <summary>
    /// Analyzes the product portfolio using ML.NET K-Means clustering and trend velocity projection.
    /// Falls back gracefully to rule-based median calculation if data is sparse (fewer than 10 products).
    /// </summary>
    public MarginMatrixReportData AnalyzePortfolio(
        DateOnly startDate,
        DateOnly endDate,
        int? locationId,
        IReadOnlyList<StockLocationOption> locations,
        IReadOnlyList<ProductRawStat> stats,
        string mode = "ai")
    {
        if (stats.Count == 0)
        {
            return new MarginMatrixReportData(
                startDate, endDate, locationId, 0m, 0m, 0, 0, 0, 0, locations, [],
                IsMlPowered: false, IsFallback: false, AnalysisMode: mode);
        }

        // Calculate robust statistical thresholds across volume, margin, profit and transaction frequency
        var sortedVols = stats.Select(p => p.Volume).OrderBy(v => v).ToList();
        var sortedMargins = stats.Select(p => p.MarginPercent).OrderBy(m => m).ToList();
        var sortedProfits = stats.Select(p => p.Profit).OrderBy(p => p).ToList();
        var sortedOrders = stats.Select(p => (decimal)p.OrderCount).OrderBy(o => o).ToList();

        // 70th percentile for volume with a floor of at least 3 units
        var thresholdVol = Math.Max(3m, Percentile(sortedVols, 0.70m));
        var thresholdMargin = Math.Max(20m, Percentile(sortedMargins, 0.50m));
        var thresholdProfit = Math.Max(100m, Percentile(sortedProfits, 0.65m));
        var medianOrders = Math.Max(2m, Percentile(sortedOrders, 0.50m));

        // If classic mode requested or not enough data to cluster meaningfully, use calibrated rule-based approach
        var isClassic = string.Equals(mode, "classic", StringComparison.OrdinalIgnoreCase);
        var hasEnoughData = stats.Count >= MinimumProductsForClustering;

        if (isClassic || !hasEnoughData)
        {
            var fallbackItems = stats.Select(p =>
            {
                var quadrant = ComputeQuadrant(p, thresholdVol, thresholdMargin, thresholdProfit, medianOrders);
                var (forecastVol, forecastMargin, trend, forecastQuad) = ComputeTrend(p, thresholdVol, thresholdMargin, thresholdProfit, medianOrders);

                return new MarginMatrixRow(
                    p.ProductId,
                    p.ProductName,
                    p.CategoryName,
                    p.Volume,
                    p.Revenue,
                    p.Cost,
                    p.Profit,
                    p.MarginPercent,
                    quadrant,
                    forecastVol,
                    forecastMargin,
                    trend,
                    forecastQuad,
                    ClusterDistance: 0f
                );
            })
            .OrderByDescending(x => x.Quadrant == MarginQuadrant.Star)
            .ThenByDescending(x => x.TotalProfit)
            .ToList();

            return new MarginMatrixReportData(
                startDate,
                endDate,
                locationId,
                thresholdVol,
                thresholdMargin,
                fallbackItems.Count(x => x.Quadrant == MarginQuadrant.Star),
                fallbackItems.Count(x => x.Quadrant == MarginQuadrant.CashCow),
                fallbackItems.Count(x => x.Quadrant == MarginQuadrant.Opportunity),
                fallbackItems.Count(x => x.Quadrant == MarginQuadrant.Laggard),
                locations,
                fallbackItems,
                IsMlPowered: false,
                IsFallback: !isClassic && !hasEnoughData,
                AnalysisMode: mode
            );
        }

        // ML.NET K-Means Clustering (K=4) with log-scale feature normalization
        try
        {
            var mlItems = ExecuteKMeansClustering(stats, thresholdVol, thresholdMargin, thresholdProfit, medianOrders);

            return new MarginMatrixReportData(
                startDate,
                endDate,
                locationId,
                thresholdVol,
                thresholdMargin,
                mlItems.Count(x => x.Quadrant == MarginQuadrant.Star),
                mlItems.Count(x => x.Quadrant == MarginQuadrant.CashCow),
                mlItems.Count(x => x.Quadrant == MarginQuadrant.Opportunity),
                mlItems.Count(x => x.Quadrant == MarginQuadrant.Laggard),
                locations,
                mlItems,
                IsMlPowered: true,
                IsFallback: false,
                AnalysisMode: "ai"
            );
        }
        catch
        {
            // Fail-safe: if any ML exception occurs during clustering, fallback seamlessly to classic
            return AnalyzePortfolio(startDate, endDate, locationId, locations, stats, mode: "classic");
        }
    }

    private List<MarginMatrixRow> ExecuteKMeansClustering(
        IReadOnlyList<ProductRawStat> stats,
        decimal thresholdVol,
        decimal thresholdMargin,
        decimal thresholdProfit,
        decimal medianOrders)
    {
        var mlContext = new MLContext(seed: 42); // Deterministic seed for reproducible clusters

        var inputs = stats.Select(p => new ProductClusterInput
        {
            Volume = (float)Math.Log(1.0 + (double)p.Volume),
            Revenue = (float)Math.Log(1.0 + (double)p.Revenue),
            Margin = (float)p.MarginPercent,
            Profit = (float)Math.Log(1.0 + (double)Math.Max(0m, p.Profit)),
            UnitCost = (float)Math.Log(1.0 + (double)p.UnitCost),
            SellingPrice = (float)Math.Log(1.0 + (double)p.SellingPrice),
            OrderCount = (float)Math.Log(1.0 + (double)p.OrderCount)
        }).ToList();

        var dataView = mlContext.Data.LoadFromEnumerable(inputs);

        var pipeline = mlContext.Transforms.Concatenate(
                "Features",
                nameof(ProductClusterInput.Volume),
                nameof(ProductClusterInput.Revenue),
                nameof(ProductClusterInput.Margin),
                nameof(ProductClusterInput.Profit),
                nameof(ProductClusterInput.UnitCost),
                nameof(ProductClusterInput.SellingPrice),
                nameof(ProductClusterInput.OrderCount))
            .Append(mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(mlContext.Clustering.Trainers.KMeans(
                featureColumnName: "Features",
                numberOfClusters: 4));

        var model = pipeline.Fit(dataView);
        var predictionsData = model.Transform(dataView);
        var predictions = mlContext.Data.CreateEnumerable<ProductClusterPrediction>(predictionsData, reuseRowObject: false).ToList();

        var rows = new List<MarginMatrixRow>(stats.Count);
        for (var i = 0; i < stats.Count; i++)
        {
            var p = stats[i];
            var pred = predictions[i];
            var quadrant = ComputeQuadrant(p, thresholdVol, thresholdMargin, thresholdProfit, medianOrders);
            var (forecastVol, forecastMargin, trend, forecastQuad) = ComputeTrend(p, thresholdVol, thresholdMargin, thresholdProfit, medianOrders);

            var distance = pred.Distances != null && pred.Distances.Length >= pred.SelectedClusterId && pred.SelectedClusterId > 0
                ? pred.Distances[pred.SelectedClusterId - 1]
                : 0f;

            rows.Add(new MarginMatrixRow(
                p.ProductId,
                p.ProductName,
                p.CategoryName,
                p.Volume,
                p.Revenue,
                p.Cost,
                p.Profit,
                p.MarginPercent,
                quadrant,
                forecastVol,
                forecastMargin,
                trend,
                forecastQuad,
                distance
            ));
        }

        return rows
            .OrderByDescending(x => x.Quadrant == MarginQuadrant.Star)
            .ThenByDescending(x => x.TotalProfit)
            .ToList();
    }

    private static decimal Percentile(List<decimal> sorted, decimal percentile)
    {
        if (sorted.Count == 0) return 0m;
        if (sorted.Count == 1) return sorted[0];
        var index = (sorted.Count - 1) * percentile;
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        if (lower == upper) return sorted[lower];
        var weight = index - lower;
        return decimal.Round(sorted[lower] * (1m - weight) + sorted[upper] * weight, 2, MidpointRounding.AwayFromZero);
    }

    public static MarginQuadrant ComputeQuadrant(
        ProductRawStat p,
        decimal thresholdVol,
        decimal thresholdMargin,
        decimal thresholdProfit,
        decimal medianOrders)
    {
        // High Volume condition: genuine volume above upper-tier threshold or high order frequency
        var isHighVol = p.Volume >= thresholdVol || (p.OrderCount >= medianOrders && p.Volume >= Math.Max(2m, thresholdVol * 0.7m));

        // High Profitability condition: balances Margin % AND Absolute Gross Profit Taka
        // A 2000tk item with 25% margin generating 500tk profit qualifies as high profit,
        // while a 100tk item with 70% margin (70tk) qualifies if its profit is also above floor.
        var isHighProfit = p.Profit >= thresholdProfit
            || (p.MarginPercent >= thresholdMargin && p.Profit >= Math.Max(50m, thresholdProfit * 0.35m));

        if (isHighVol && isHighProfit)
            return MarginQuadrant.Star;
        if (isHighVol && !isHighProfit)
            return MarginQuadrant.CashCow;
        if (!isHighVol && isHighProfit)
            return MarginQuadrant.Opportunity;
        return MarginQuadrant.Laggard;
    }

    private static (decimal ForecastVol, decimal ForecastMargin, ProductTrendStatus Trend, MarginQuadrant ForecastQuad) ComputeTrend(
        ProductRawStat p,
        decimal thresholdVol,
        decimal thresholdMargin,
        decimal thresholdProfit,
        decimal medianOrders)
    {
        var currentQuad = ComputeQuadrant(p, thresholdVol, thresholdMargin, thresholdProfit, medianOrders);
        var hasHistory = p.PreviousVolume > 0m || p.RecentVolume > 0m;
        if (!hasHistory)
        {
            return (p.Volume, p.MarginPercent, ProductTrendStatus.NewOrInsufficientData, currentQuad);
        }

        var baseline = Math.Max(1m, p.PreviousVolume);
        var velocity = (p.RecentVolume - p.PreviousVolume) / baseline;
        var clampedVelocity = Math.Clamp(velocity, -0.6m, 0.6m);

        var forecastVol = Math.Max(0m, Math.Round(p.Volume * (1m + clampedVelocity), 1, MidpointRounding.AwayFromZero));
        var forecastMargin = p.RecentMargin > 0m ? p.RecentMargin : p.MarginPercent;
        var forecastProfit = p.Volume > 0 ? (p.Profit / p.Volume) * forecastVol : p.Profit;

        var forecastStat = p with { Volume = forecastVol, MarginPercent = forecastMargin, Profit = forecastProfit };
        var forecastQuad = ComputeQuadrant(forecastStat, thresholdVol, thresholdMargin, thresholdProfit, medianOrders);

        ProductTrendStatus trend;
        if ((currentQuad != MarginQuadrant.Star && forecastQuad == MarginQuadrant.Star) || (velocity >= 0.25m && forecastMargin >= thresholdMargin))
        {
            trend = ProductTrendStatus.RisingToStar;
        }
        else if ((currentQuad != MarginQuadrant.Laggard && forecastQuad == MarginQuadrant.Laggard) || (velocity <= -0.25m && currentQuad == MarginQuadrant.CashCow))
        {
            trend = ProductTrendStatus.SlippingToLaggard;
        }
        else if (forecastMargin >= p.MarginPercent + 3m)
        {
            trend = ProductTrendStatus.ImprovingMargin;
        }
        else if (velocity <= -0.15m)
        {
            trend = ProductTrendStatus.DecliningVolume;
        }
        else
        {
            trend = ProductTrendStatus.Stable;
        }

        return (forecastVol, forecastMargin, trend, forecastQuad);
    }
}
