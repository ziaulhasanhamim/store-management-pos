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

        // Calculate medians for reference and fallback
        var sortedVols = stats.Select(p => p.Volume).OrderBy(v => v).ToList();
        var sortedMargins = stats.Select(p => p.MarginPercent).OrderBy(m => m).ToList();
        var medianVol = sortedVols[sortedVols.Count / 2];
        var medianMargin = Math.Max(15m, sortedMargins[sortedMargins.Count / 2]);

        // If classic mode requested or not enough data to cluster meaningfully, use classic rule-based approach
        var isClassic = string.Equals(mode, "classic", StringComparison.OrdinalIgnoreCase);
        var hasEnoughData = stats.Count >= MinimumProductsForClustering;

        if (isClassic || !hasEnoughData)
        {
            var fallbackItems = stats.Select(p =>
            {
                var quadrant = ComputeClassicQuadrant(p.Volume, p.MarginPercent, medianVol, medianMargin);
                var (forecastVol, forecastMargin, trend, forecastQuad) = ComputeTrend(p, medianVol, medianMargin);

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
                medianVol,
                medianMargin,
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

        // ML.NET K-Means Clustering (K=4)
        try
        {
            var mlItems = ExecuteKMeansClustering(stats, medianVol, medianMargin);

            return new MarginMatrixReportData(
                startDate,
                endDate,
                locationId,
                medianVol,
                medianMargin,
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
        decimal medianVol,
        decimal medianMargin)
    {
        var mlContext = new MLContext(seed: 42); // Deterministic seed for reproducible clusters

        var inputs = stats.Select(p => new ProductClusterInput
        {
            Volume = (float)p.Volume,
            Revenue = (float)p.Revenue,
            Margin = (float)p.MarginPercent,
            UnitCost = (float)p.UnitCost,
            SellingPrice = (float)p.SellingPrice,
            OrderCount = (float)p.OrderCount
        }).ToList();

        var dataView = mlContext.Data.LoadFromEnumerable(inputs);

        var pipeline = mlContext.Transforms.Concatenate(
                "Features",
                nameof(ProductClusterInput.Volume),
                nameof(ProductClusterInput.Revenue),
                nameof(ProductClusterInput.Margin),
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

        // Profile the 4 cluster centroids to align them with retail portfolio quadrants
        var clusterProfiles = new Dictionary<uint, (decimal AvgVol, decimal AvgMargin, int Count)>();

        for (uint c = 1; c <= 4; c++)
        {
            var clusterItems = stats.Where((_, idx) => predictions[idx].SelectedClusterId == c).ToList();
            if (clusterItems.Count > 0)
            {
                var avgVol = clusterItems.Average(x => x.Volume);
                var avgMargin = clusterItems.Average(x => x.MarginPercent);
                clusterProfiles[c] = (avgVol, avgMargin, clusterItems.Count);
            }
            else
            {
                clusterProfiles[c] = (0m, 0m, 0);
            }
        }

        // Map each cluster ID to the appropriate MarginQuadrant
        var clusterToQuadrant = MapClustersToQuadrants(clusterProfiles, medianVol, medianMargin);

        var rows = new List<MarginMatrixRow>(stats.Count);
        for (var i = 0; i < stats.Count; i++)
        {
            var p = stats[i];
            var pred = predictions[i];
            var quadrant = clusterToQuadrant.TryGetValue(pred.SelectedClusterId, out var q)
                ? q
                : ComputeClassicQuadrant(p.Volume, p.MarginPercent, medianVol, medianMargin);

            var (forecastVol, forecastMargin, trend, forecastQuad) = ComputeTrend(p, medianVol, medianMargin);

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

    /// <summary>
    /// Profiles the 4 cluster centers based on relative volume and margin and maps them to BCG quadrants.
    /// </summary>
    private static Dictionary<uint, MarginQuadrant> MapClustersToQuadrants(
        Dictionary<uint, (decimal AvgVol, decimal AvgMargin, int Count)> profiles,
        decimal medianVol,
        decimal medianMargin)
    {
        var mapping = new Dictionary<uint, MarginQuadrant>();
        var activeClusters = profiles.Where(kvp => kvp.Value.Count > 0).ToList();

        if (activeClusters.Count == 0)
        {
            return mapping;
        }

        // If clusters have distinct profiles, assign based on centroid relation to overall medians
        var unassigned = new HashSet<uint>(activeClusters.Select(c => c.Key));

        // 1. Identify Star: Highest combined normalized volume and margin
        var starCandidate = activeClusters
            .OrderByDescending(c => (c.Value.AvgVol >= medianVol ? 2 : 0) + (c.Value.AvgMargin >= medianMargin ? 2 : 0))
            .ThenByDescending(c => c.Value.AvgVol * c.Value.AvgMargin)
            .First();
        mapping[starCandidate.Key] = MarginQuadrant.Star;
        unassigned.Remove(starCandidate.Key);

        // 2. Identify Cash Cow: High volume, but lower margin
        var cowCandidate = activeClusters
            .Where(c => unassigned.Contains(c.Key))
            .OrderByDescending(c => c.Value.AvgVol)
            .ThenBy(c => c.Value.AvgMargin)
            .FirstOrDefault();
        if (cowCandidate.Key != 0)
        {
            mapping[cowCandidate.Key] = MarginQuadrant.CashCow;
            unassigned.Remove(cowCandidate.Key);
        }

        // 3. Identify Opportunity: High margin, but lower volume
        var oppCandidate = activeClusters
            .Where(c => unassigned.Contains(c.Key))
            .OrderByDescending(c => c.Value.AvgMargin)
            .ThenBy(c => c.Value.AvgVol)
            .FirstOrDefault();
        if (oppCandidate.Key != 0)
        {
            mapping[oppCandidate.Key] = MarginQuadrant.Opportunity;
            unassigned.Remove(oppCandidate.Key);
        }

        // 4. Remaining clusters map to Laggards
        foreach (var remaining in unassigned)
        {
            mapping[remaining] = MarginQuadrant.Laggard;
        }

        return mapping;
    }

    private static MarginQuadrant ComputeClassicQuadrant(
        decimal volume,
        decimal margin,
        decimal medianVol,
        decimal medianMargin)
    {
        if (volume >= medianVol && margin >= medianMargin)
            return MarginQuadrant.Star;
        if (volume >= medianVol && margin < medianMargin)
            return MarginQuadrant.CashCow;
        if (volume < medianVol && margin >= medianMargin)
            return MarginQuadrant.Opportunity;
        return MarginQuadrant.Laggard;
    }

    private static (decimal ForecastVol, decimal ForecastMargin, ProductTrendStatus Trend, MarginQuadrant ForecastQuad) ComputeTrend(
        ProductRawStat p,
        decimal medianVol,
        decimal medianMargin)
    {
        // Periodic velocity comparison between recent and previous sales windows
        var hasHistory = p.PreviousVolume > 0m || p.RecentVolume > 0m;
        if (!hasHistory)
        {
            var fallbackQuad = ComputeClassicQuadrant(p.Volume, p.MarginPercent, medianVol, medianMargin);
            return (p.Volume, p.MarginPercent, ProductTrendStatus.NewOrInsufficientData, fallbackQuad);
        }

        var baseline = Math.Max(1m, p.PreviousVolume);
        var velocity = (p.RecentVolume - p.PreviousVolume) / baseline;
        var clampedVelocity = Math.Clamp(velocity, -0.6m, 0.6m);

        var forecastVol = Math.Max(0m, Math.Round(p.Volume * (1m + clampedVelocity), 1, MidpointRounding.AwayFromZero));
        var forecastMargin = p.RecentMargin > 0m ? p.RecentMargin : p.MarginPercent;
        var forecastQuad = ComputeClassicQuadrant(forecastVol, forecastMargin, medianVol, medianMargin);

        ProductTrendStatus trend;
        if ((p.Volume < medianVol && forecastVol >= medianVol && forecastMargin >= medianMargin) || (velocity >= 0.25m && forecastMargin >= medianMargin))
        {
            trend = ProductTrendStatus.RisingToStar;
        }
        else if ((p.Volume >= medianVol && forecastVol < medianVol) || (velocity <= -0.25m && p.Volume >= medianVol))
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
