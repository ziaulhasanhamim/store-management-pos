using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using POSApp.Client.DTOs;
using POSApp.Client.Security;
using POSApp.Persistence;
using POSApp.Persistence.Entities;
using POSApp.Services.ML;

namespace POSApp.Services;

public sealed class StockIntelligenceService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly HybridCache _cache;
    private readonly DeadStockMLService _mlService;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

    public StockIntelligenceService(
        IServiceScopeFactory scopes,
        HybridCache cache,
        DeadStockMLService? mlService = null)
    {
        _scopes = scopes;
        _cache = cache;
        _mlService = mlService ?? new DeadStockMLService();
    }

    public async Task<StockIntelligenceReportData> GetStockIntelligenceAsync(
        ClaimsPrincipal actor,
        int? locationId = null,
        bool forceRefresh = false,
        CancellationToken ct = default)
    {
        var cacheKey = $"StockIntelligence_AllHistory_Loc_{locationId?.ToString() ?? "All"}";

        if (forceRefresh)
        {
            await _cache.RemoveAsync(cacheKey, ct);
        }

        var entryOptions = new HybridCacheEntryOptions
        {
            Expiration = CacheDuration,
            LocalCacheExpiration = CacheDuration
        };

        var tags = new[] { "stock-intelligence", $"stock-intelligence-loc-{locationId?.ToString() ?? "all"}" };

        var report = await _cache.GetOrCreateAsync(
            cacheKey,
            async token => await ComputeStockIntelligenceAsync(actor, locationId, token),
            entryOptions,
            tags,
            ct);

        return report with { IsCached = !forceRefresh };
    }

    private async Task<StockIntelligenceReportData> ComputeStockIntelligenceAsync(
        ClaimsPrincipal actor,
        int? locationId,
        CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);
        var canSeeCost = await RolePermissionService.HasPolicyAsync(db, actorId, AppPolicies.ViewCostPrice, ct);

        var now = DateTimeOffset.UtcNow;

        var locations = await db.StockLocations.AsNoTracking()
            .Where(l => !l.IsDeleted && l.CanSell)
            .OrderBy(l => l.Name)
            .Select(l => new StockLocationOption(l.Id, l.Name))
            .ToListAsync(ct);

        var categories = await db.Categories.AsNoTracking()
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.Name)
            .Select(c => new CategoryOption(c.Id, c.Name))
            .ToListAsync(ct);

        // 1. Fetch current inventory balances
        var balancesQuery = db.InventoryBalances.AsNoTracking()
            .Include(b => b.Product)
            .ThenInclude(p => p!.Category)
            .Include(b => b.Product)
            .ThenInclude(p => p!.Supplier)
            .Where(b => b.Product != null && !b.Product.IsDeleted);

        if (locationId.HasValue)
        {
            balancesQuery = balancesQuery.Where(b => b.StockLocationId == locationId.Value);
        }

        var balances = await balancesQuery.ToListAsync(ct);

        // Map product balances
        var productBalances = balances
            .GroupBy(b => b.ProductId)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    Product = g.First().Product!,
                    TotalStock = g.Sum(x => x.QuantityOnHand)
                });

        // 2. Fetch full lifetime sales items for these products (NO date filter)
        var salesQuery = db.SaleItems.AsNoTracking()
            .Where(i => !i.Sale!.IsDeleted && i.Sale.Status == SaleStatus.Completed);

        if (locationId.HasValue)
        {
            salesQuery = salesQuery.Where(i => i.Sale!.StockLocationId == locationId.Value);
        }

        var salesAggregates = await salesQuery
            .GroupBy(i => i.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                TotalQuantity = g.Sum(x => x.Quantity),
                TotalRevenue = g.Sum(x => x.TotalAmount),
                TotalCost = g.Sum(x => x.UnitCost * x.Quantity),
                OrderCount = g.Select(x => x.SaleId).Distinct().Count(),
                FirstSaleAt = g.Min(x => (DateTimeOffset?)x.Sale!.SoldAt),
                LastSaleAt = g.Max(x => (DateTimeOffset?)x.Sale!.SoldAt)
            })
            .ToDictionaryAsync(x => x.ProductId, ct);

        // 3. Fetch purchase history for lead time & restock cadence
        var purchaseAggregates = await db.Purchases.AsNoTracking()
            .Where(p => !p.IsDeleted && p.Status == PurchaseStatus.Received && p.ReceivedAt.HasValue)
            .SelectMany(p => p.Items.Select(item => new
            {
                item.ProductId,
                LeadDays = (p.ReceivedAt!.Value - p.OrderedAt).TotalDays,
                p.ReceivedAt
            }))
            .GroupBy(x => x.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                RestockCount = g.Count(),
                AvgLeadDays = g.Average(x => x.LeadDays),
                LastReceivedAt = g.Max(x => x.ReceivedAt)
            })
            .ToDictionaryAsync(x => x.ProductId, ct);

        // 4. Fetch user AI feedback / overrides
        var userFeedbacks = await db.ProductAiFeedbacks.AsNoTracking()
            .ToDictionaryAsync(f => f.ProductId, f => f.IsDeadStockOverride, ct);

        // 5. Build raw metrics and feature profiles for all products
        var productProfiles = new List<ProductDeadStockProfile>();
        var intermediateData = new List<IntermediateItem>();

        foreach (var (productId, info) in productBalances)
        {
            var p = info.Product;
            var currentStock = Math.Max(0m, info.TotalStock);
            var costPrice = canSeeCost ? p.CostPrice : 0m;
            var sales = salesAggregates.GetValueOrDefault(productId);
            var purchaseInfo = purchaseAggregates.GetValueOrDefault(productId);
            var userOverride = userFeedbacks.GetValueOrDefault(productId);

            var totalQtySold = sales?.TotalQuantity ?? 0m;
            var totalRev = sales?.TotalRevenue ?? 0m;
            var totalCost = canSeeCost ? (sales?.TotalCost ?? 0m) : 0m;
            var totalProfit = canSeeCost ? totalRev - totalCost : 0m;

            // Actual realized selling price (never default MRP)
            var realizedPrice = totalQtySold > 0 ? (totalRev / totalQtySold) : p.SellingPrice;
            var realizedCost = totalQtySold > 0 && canSeeCost ? (totalCost / totalQtySold) : costPrice;
            var realizedUnitProfit = canSeeCost ? (realizedPrice - realizedCost) : 0m;
            var realizedMargin = canSeeCost && totalRev > 0 ? (totalProfit / totalRev * 100m) : 0m;

            // Days calculation
            int daysSinceLastSale;
            decimal dailyVelocity;

            if (sales?.LastSaleAt is not null)
            {
                daysSinceLastSale = Math.Max(0, (int)(now - sales.LastSaleAt.Value).TotalDays);
                var activeDays = Math.Max(1.0, (sales.LastSaleAt.Value - (sales.FirstSaleAt ?? sales.LastSaleAt.Value)).TotalDays);
                dailyVelocity = totalQtySold / (decimal)Math.Max(7.0, activeDays);
            }
            else
            {
                daysSinceLastSale = Math.Max(30, (int)(now - p.CreatedAt).TotalDays);
                dailyVelocity = 0m;
            }

            var tiedUpCapital = canSeeCost ? currentStock * costPrice : 0m;
            var restockCount = purchaseInfo?.RestockCount ?? 0;
            var leadTimeDays = purchaseInfo?.AvgLeadDays > 0 ? Math.Clamp((int)Math.Round(purchaseInfo.AvgLeadDays), 1, 30) : 3;

            productProfiles.Add(new ProductDeadStockProfile(
                productId,
                p.Name,
                daysSinceLastSale,
                dailyVelocity,
                totalQtySold,
                tiedUpCapital,
                realizedUnitProfit,
                realizedMargin,
                restockCount,
                userOverride));

            intermediateData.Add(new IntermediateItem(
                p,
                currentStock,
                costPrice,
                realizedPrice,
                realizedMargin,
                realizedUnitProfit,
                totalQtySold,
                totalRev,
                totalProfit,
                totalCost,
                dailyVelocity,
                daysSinceLastSale,
                tiedUpCapital,
                leadTimeDays,
                userOverride,
                sales?.OrderCount ?? 0));
        }

        // 6. ML Dead Stock Classification
        var mlClassifications = _mlService.ClassifyCatalog(productProfiles);

        // 7. Statistical cutoffs for portfolio role assignment
        var profits = intermediateData.Select(x => x.TotalProfit).OrderBy(x => x).ToList();
        var gmrois = intermediateData.Select(x => x.TiedUpCapital > 0 ? x.TotalProfit / x.TiedUpCapital : x.TotalProfit).OrderBy(x => x).ToList();
        var velocities = intermediateData.Select(x => x.DailyVelocity).OrderBy(x => x).ToList();

        var topProfitCutoff = GetPercentile(profits, 0.70m);
        var topGmroiCutoff = GetPercentile(gmrois, 0.70m);
        var highVelocityCutoff = Math.Max(0.10m, GetPercentile(velocities, 0.65m));

        var rows = new List<StockIntelligenceRow>(intermediateData.Count);

        foreach (var item in intermediateData)
        {
            var p = item.Product;
            var mlResult = mlClassifications.GetValueOrDefault(item.Product.Id);
            var isDeadStock = mlResult?.IsDeadStock ?? false;
            var deadProb = mlResult?.Probability ?? 0f;
            var isOverridden = mlResult?.IsUserOverridden ?? false;

            // GMROI = Total Gross Profit / Average Working Capital Invested
            var gmroi = item.TiedUpCapital > 0 ? decimal.Round(item.TotalProfit / item.TiedUpCapital, 2, MidpointRounding.AwayFromZero) : item.TotalProfit;
            var profitPerTk = item.TotalCost > 0 ? decimal.Round(item.TotalProfit / item.TotalCost, 2, MidpointRounding.AwayFromZero) : 0m;

            // Days of supply
            var daysOfSupply = item.DailyVelocity > 0m
                ? decimal.Round(item.CurrentStock / item.DailyVelocity, 1, MidpointRounding.AwayFromZero)
                : (item.CurrentStock > 0 ? 999m : 0m);

            // Product Role / Segment Classification
            StockIntelligenceSegment segment;
            if (isDeadStock)
            {
                segment = StockIntelligenceSegment.DeadStockTrap;
            }
            else if (item.DailyVelocity >= highVelocityCutoff || (item.OrderCount >= 8 && item.DailyVelocity >= 0.05m))
            {
                segment = StockIntelligenceSegment.CashFlowAnchor; // High frequency essential / foot-traffic driver
            }
            else if (gmroi >= topGmroiCutoff || profitPerTk >= 0.35m || item.TotalProfit >= topProfitCutoff)
            {
                segment = StockIntelligenceSegment.CapitalCompounder; // Top capital efficiency
            }
            else if (item.RealizedUnitProfit >= 300m || item.RealizedMarginPercent >= 40m)
            {
                segment = StockIntelligenceSegment.RareGem; // Lucrative slow mover
            }
            else
            {
                segment = StockIntelligenceSegment.StandardActive;
            }

            // Dynamic Procurement Reorder Recommendation (STRICTLY NO ReorderLevel used)
            int bufferDays;
            decimal targetStock;
            StockReorderUrgency urgency;
            decimal recommendedOrderQty = 0m;

            if (segment == StockIntelligenceSegment.DeadStockTrap)
            {
                bufferDays = 0;
                targetStock = 0m;
                urgency = StockReorderUrgency.Liquidate;
                recommendedOrderQty = 0m;
            }
            else if (segment == StockIntelligenceSegment.RareGem)
            {
                // Slow but highly profitable: maintain small safety stock (1-2 pieces)
                bufferDays = Math.Max(2, item.LeadTimeDays);
                targetStock = item.CurrentStock <= 0 ? 1m : 1m;
                if (item.CurrentStock <= 0)
                {
                    recommendedOrderQty = 1m;
                    urgency = StockReorderUrgency.Critical;
                }
                else
                {
                    urgency = StockReorderUrgency.Healthy;
                }
            }
            else
            {
                bufferDays = segment switch
                {
                    StockIntelligenceSegment.CashFlowAnchor => Math.Max(7, item.LeadTimeDays * 2), // Protect staple availability
                    StockIntelligenceSegment.CapitalCompounder => Math.Max(5, (int)(item.LeadTimeDays * 1.5)),
                    _ => Math.Max(3, item.LeadTimeDays)
                };

                targetStock = Math.Ceiling(item.DailyVelocity * (item.LeadTimeDays + bufferDays));

                // Always ensure at least 1 unit buffer for active items with sales history
                if (targetStock < 1m && item.TotalUnitsSold > 0)
                {
                    targetStock = 1m;
                }

                recommendedOrderQty = Math.Max(0m, targetStock - item.CurrentStock);

                if (recommendedOrderQty > 0)
                {
                    if (item.CurrentStock <= 0 || daysOfSupply <= item.LeadTimeDays)
                    {
                        urgency = StockReorderUrgency.Critical;
                    }
                    else
                    {
                        urgency = StockReorderUrgency.OrderSoon;
                    }
                }
                else if (daysOfSupply >= 90)
                {
                    urgency = StockReorderUrgency.Overstocked;
                }
                else
                {
                    urgency = StockReorderUrgency.Healthy;
                }
            }

            var estOrderCost = canSeeCost ? recommendedOrderQty * item.CostPrice : 0m;
            var expProfitReturn = canSeeCost ? recommendedOrderQty * item.RealizedUnitProfit : 0m;

            rows.Add(new StockIntelligenceRow(
                p.Id,
                p.Name,
                p.Barcode,
                p.Category?.Name ?? "Uncategorized",
                p.Supplier?.Name ?? "Default",
                item.CurrentStock,
                item.CostPrice,
                decimal.Round(item.RealizedPrice, 2, MidpointRounding.AwayFromZero),
                decimal.Round(item.RealizedMarginPercent, 1, MidpointRounding.AwayFromZero),
                decimal.Round(item.RealizedUnitProfit, 2, MidpointRounding.AwayFromZero),
                item.TotalUnitsSold,
                item.TotalRevenue,
                item.TotalProfit,
                decimal.Round(item.DailyVelocity, 3, MidpointRounding.AwayFromZero),
                daysOfSupply,
                item.DaysSinceLastSale,
                item.TiedUpCapital,
                gmroi,
                profitPerTk,
                segment,
                urgency,
                recommendedOrderQty,
                estOrderCost,
                expProfitReturn,
                isDeadStock,
                item.UserOverride,
                deadProb,
                item.LeadTimeDays));
        }

        // Order rows by urgency and opportunity
        var sortedRows = rows
            .OrderBy(r => r.ReorderUrgency == StockReorderUrgency.Critical ? 0 :
                          r.ReorderUrgency == StockReorderUrgency.OrderSoon ? 1 :
                          r.Segment == StockIntelligenceSegment.CapitalCompounder ? 2 :
                          r.Segment == StockIntelligenceSegment.CashFlowAnchor ? 3 :
                          r.ReorderUrgency == StockReorderUrgency.Liquidate ? 4 : 5)
            .ThenByDescending(r => r.EstimatedOrderCost)
            .ToList();

        var totalTiedCapital = rows.Sum(r => r.TiedUpCapital);
        var urgentReorderValue = rows.Where(r => r.ReorderUrgency == StockReorderUrgency.Critical || r.ReorderUrgency == StockReorderUrgency.OrderSoon).Sum(r => r.EstimatedOrderCost);
        var urgentSkuCount = rows.Count(r => r.ReorderUrgency == StockReorderUrgency.Critical || r.ReorderUrgency == StockReorderUrgency.OrderSoon);
        var deadCapitalValue = rows.Where(r => r.IsDeadStock).Sum(r => r.TiedUpCapital);
        var deadSkuCount = rows.Count(r => r.IsDeadStock);
        var cashFlowCount = rows.Count(r => r.Segment == StockIntelligenceSegment.CashFlowAnchor);
        var compounderCount = rows.Count(r => r.Segment == StockIntelligenceSegment.CapitalCompounder);
        var rareGemCount = rows.Count(r => r.Segment == StockIntelligenceSegment.RareGem);

        return new StockIntelligenceReportData(
            totalTiedCapital,
            urgentReorderValue,
            urgentSkuCount,
            deadCapitalValue,
            deadSkuCount,
            cashFlowCount,
            compounderCount,
            rareGemCount,
            now,
            IsCached: false,
            sortedRows,
            locations,
            categories);
    }

    public async Task SetUserDeadStockOverrideAsync(
        ClaimsPrincipal actor,
        int productId,
        bool? isDeadStockOverride,
        string? reason = null,
        CancellationToken ct = default)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);

        var existing = await db.ProductAiFeedbacks
            .FirstOrDefaultAsync(f => f.ProductId == productId, ct);

        if (existing == null)
        {
            if (isDeadStockOverride.HasValue)
            {
                db.ProductAiFeedbacks.Add(new ProductAiFeedback
                {
                    ProductId = productId,
                    IsDeadStockOverride = isDeadStockOverride.Value,
                    Reason = reason,
                    MarkedBy = actor.Identity?.Name ?? "User"
                });
            }
        }
        else
        {
            if (isDeadStockOverride.HasValue)
            {
                existing.IsDeadStockOverride = isDeadStockOverride.Value;
                existing.Reason = reason;
                existing.MarkedBy = actor.Identity?.Name ?? "User";
                existing.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                // Reset override to let ML classify
                db.ProductAiFeedbacks.Remove(existing);
            }
        }

        await db.SaveChangesAsync(ct);
        await InvalidateCacheAsync(ct);
    }

    public async Task InvalidateCacheAsync(CancellationToken ct = default)
    {
        await _cache.RemoveByTagAsync("stock-intelligence", ct);
        await _cache.RemoveAsync("StockIntelligence_AllHistory_Loc_All", ct);
    }

    private static decimal GetPercentile(List<decimal> sorted, decimal percentile)
    {
        if (sorted.Count == 0) return 0m;
        var index = (int)Math.Clamp(Math.Floor((sorted.Count - 1) * (double)percentile), 0, sorted.Count - 1);
        return sorted[index];
    }

    private sealed record IntermediateItem(
        Product Product,
        decimal CurrentStock,
        decimal CostPrice,
        decimal RealizedPrice,
        decimal RealizedMarginPercent,
        decimal RealizedUnitProfit,
        decimal TotalUnitsSold,
        decimal TotalRevenue,
        decimal TotalProfit,
        decimal TotalCost,
        decimal DailyVelocity,
        int DaysSinceLastSale,
        decimal TiedUpCapital,
        int LeadTimeDays,
        bool? UserOverride,
        int OrderCount);
}
