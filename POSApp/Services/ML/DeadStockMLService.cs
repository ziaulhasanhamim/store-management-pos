using Microsoft.ML;
using Microsoft.ML.Data;

namespace POSApp.Services.ML;

public sealed class DeadStockFeatureInput
{
    public float DaysSinceLastSale { get; set; }
    public float DailyVelocity { get; set; }
    public float LifetimeUnitsSold { get; set; }
    public float TiedUpCapital { get; set; }
    public float RealizedUnitProfit { get; set; }
    public float RealizedMarginPercent { get; set; }
    public float RestockCount { get; set; }
    public bool Label { get; set; }
}

public sealed class DeadStockPrediction
{
    [ColumnName("PredictedLabel")]
    public bool PredictedLabel { get; set; }

    [ColumnName("Probability")]
    public float Probability { get; set; }

    [ColumnName("Score")]
    public float Score { get; set; }
}

public sealed record ProductDeadStockProfile(
    int ProductId,
    string ProductName,
    int DaysSinceLastSale,
    decimal DailyVelocity,
    decimal LifetimeUnitsSold,
    decimal TiedUpCapital,
    decimal RealizedUnitProfit,
    decimal RealizedMarginPercent,
    int RestockCount,
    bool? UserOverride);

public sealed record DeadStockClassificationResult(
    int ProductId,
    bool IsDeadStock,
    float Probability,
    bool IsUserOverridden);

public sealed class DeadStockMLService
{
    private const int MinimumTrainingSamples = 6;

    public IReadOnlyDictionary<int, DeadStockClassificationResult> ClassifyCatalog(
        IReadOnlyList<ProductDeadStockProfile> catalog)
    {
        var results = new Dictionary<int, DeadStockClassificationResult>(catalog.Count);
        if (catalog.Count == 0)
        {
            return results;
        }

        var medianUnitProfit = GetMedian(catalog.Select(x => x.RealizedUnitProfit).ToList());
        var highUnitProfit = GetPercentile(catalog.Select(x => x.RealizedUnitProfit).ToList(), 0.75m);

        // Build labeled samples for supervised / semi-supervised learning
        var trainingInputs = new List<DeadStockFeatureInput>();

        foreach (var p in catalog)
        {
            if (p.UserOverride.HasValue)
            {
                // Ground truth provided directly by store manager / user
                trainingInputs.Add(ToFeatureInput(p, p.UserOverride.Value));
            }
            else
            {
                // Multi-criteria boundary for pseudo-labeling:
                // Stagnant sales AND low profit AND low velocity
                if (p.DaysSinceLastSale >= 90 && p.DailyVelocity <= 0.02m && p.RealizedUnitProfit < medianUnitProfit)
                {
                    trainingInputs.Add(ToFeatureInput(p, true));
                }
                // Healthy or rare high-profit item
                else if (p.DailyVelocity >= 0.08m || p.DaysSinceLastSale <= 30 || (p.RealizedUnitProfit >= highUnitProfit && p.DaysSinceLastSale <= 75))
                {
                    trainingInputs.Add(ToFeatureInput(p, false));
                }
            }
        }

        // Check if we have positive and negative classes for training
        var hasPos = trainingInputs.Any(x => x.Label);
        var hasNeg = trainingInputs.Any(x => !x.Label);

        if (trainingInputs.Count < MinimumTrainingSamples || !hasPos || !hasNeg)
        {
            // Rule-based multi-criteria fallback
            foreach (var p in catalog)
            {
                if (p.UserOverride.HasValue)
                {
                    results[p.ProductId] = new DeadStockClassificationResult(
                        p.ProductId,
                        p.UserOverride.Value,
                        p.UserOverride.Value ? 1.0f : 0.0f,
                        IsUserOverridden: true);
                    continue;
                }

                // Rare gems with strong profit are protected from dead stock status
                var isRareGem = p.RealizedUnitProfit >= highUnitProfit && p.LifetimeUnitsSold > 0;
                var isDead = !isRareGem && (p.DaysSinceLastSale >= 90 && p.DailyVelocity <= 0.02m && p.RealizedUnitProfit <= medianUnitProfit);

                var prob = isDead ? 0.85f : (p.DaysSinceLastSale > 60 ? 0.40f : 0.05f);
                results[p.ProductId] = new DeadStockClassificationResult(p.ProductId, isDead, prob, IsUserOverridden: false);
            }

            return results;
        }

        try
        {
            var mlContext = new MLContext(seed: 42);
            var trainData = mlContext.Data.LoadFromEnumerable(trainingInputs);

            var pipeline = mlContext.Transforms.Concatenate(
                    "Features",
                    nameof(DeadStockFeatureInput.DaysSinceLastSale),
                    nameof(DeadStockFeatureInput.DailyVelocity),
                    nameof(DeadStockFeatureInput.LifetimeUnitsSold),
                    nameof(DeadStockFeatureInput.TiedUpCapital),
                    nameof(DeadStockFeatureInput.RealizedUnitProfit),
                    nameof(DeadStockFeatureInput.RealizedMarginPercent),
                    nameof(DeadStockFeatureInput.RestockCount))
                .Append(mlContext.Transforms.NormalizeMinMax("Features"))
                .Append(mlContext.BinaryClassification.Trainers.SdcaLogisticRegression(
                    labelColumnName: nameof(DeadStockFeatureInput.Label),
                    featureColumnName: "Features"));

            var model = pipeline.Fit(trainData);

            var allInputs = catalog.Select(p => ToFeatureInput(p, false)).ToList();
            var allDataView = mlContext.Data.LoadFromEnumerable(allInputs);
            var predictionsData = model.Transform(allDataView);
            var predictions = mlContext.Data.CreateEnumerable<DeadStockPrediction>(predictionsData, reuseRowObject: false).ToList();

            for (var i = 0; i < catalog.Count; i++)
            {
                var p = catalog[i];
                var pred = predictions[i];

                if (p.UserOverride.HasValue)
                {
                    results[p.ProductId] = new DeadStockClassificationResult(
                        p.ProductId,
                        p.UserOverride.Value,
                        p.UserOverride.Value ? 1.0f : 0.0f,
                        IsUserOverridden: true);
                }
                else
                {
                    // Rare gems protection: If item generates top-quartile profit per sale, do not mark dead
                    var isRareGem = p.RealizedUnitProfit >= highUnitProfit && p.LifetimeUnitsSold > 0 && p.DaysSinceLastSale <= 120;
                    var isDead = !isRareGem && (pred.Probability >= 0.50f || pred.PredictedLabel);

                    results[p.ProductId] = new DeadStockClassificationResult(
                        p.ProductId,
                        isDead,
                        pred.Probability,
                        IsUserOverridden: false);
                }
            }

            return results;
        }
        catch
        {
            // Fail-safe heuristic fallback if ML pipeline encounters matrix singularity
            foreach (var p in catalog)
            {
                var isOverridden = p.UserOverride.HasValue;
                var isDead = p.UserOverride ?? (p.DaysSinceLastSale >= 90 && p.DailyVelocity <= 0.02m && p.RealizedUnitProfit <= medianUnitProfit);
                results[p.ProductId] = new DeadStockClassificationResult(p.ProductId, isDead, isDead ? 0.9f : 0.1f, isOverridden);
            }

            return results;
        }
    }

    private static DeadStockFeatureInput ToFeatureInput(ProductDeadStockProfile p, bool label)
    {
        return new DeadStockFeatureInput
        {
            DaysSinceLastSale = p.DaysSinceLastSale,
            DailyVelocity = (float)p.DailyVelocity,
            LifetimeUnitsSold = (float)p.LifetimeUnitsSold,
            TiedUpCapital = (float)p.TiedUpCapital,
            RealizedUnitProfit = (float)p.RealizedUnitProfit,
            RealizedMarginPercent = (float)p.RealizedMarginPercent,
            RestockCount = p.RestockCount,
            Label = label
        };
    }

    private static decimal GetMedian(List<decimal> list)
    {
        if (list.Count == 0) return 0m;
        var sorted = list.OrderBy(x => x).ToList();
        return sorted[sorted.Count / 2];
    }

    private static decimal GetPercentile(List<decimal> list, decimal percentile)
    {
        if (list.Count == 0) return 0m;
        var sorted = list.OrderBy(x => x).ToList();
        var index = (int)Math.Clamp(Math.Floor((sorted.Count - 1) * (double)percentile), 0, sorted.Count - 1);
        return sorted[index];
    }
}
