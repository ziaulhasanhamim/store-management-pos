using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Client.Security;
using POSApp.Persistence;

namespace POSApp.Services;

public sealed class AnalyticsService(IServiceScopeFactory scopes, IConfiguration configuration)
{
    private DateTimeOffset Utc(DateOnly date)
    {
        var zone = AccountingCalendar.Zone(configuration);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone), TimeSpan.Zero);
    }

    public async Task<CategoryPerformanceReportData> GetCategoryPerformanceAsync(
        ClaimsPrincipal actor,
        DateOnly startDate,
        DateOnly endDate,
        int? locationId = null,
        CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppPolicies.MakeSales, ct);

        var fromUtc = Utc(startDate);
        var throughUtc = Utc(endDate.AddDays(1));

        var locations = await db.StockLocations.AsNoTracking()
            .Where(l => !l.IsDeleted && l.CanSell)
            .OrderBy(l => l.Name)
            .Select(l => new StockLocationOption(l.Id, l.Name))
            .ToListAsync(ct);

        var saleItems = await db.SaleItems.AsNoTracking()
            .Where(i => !i.Sale!.IsDeleted && i.Sale.Status == SaleStatus.Completed
                && i.Sale.SoldAt >= fromUtc && i.Sale.SoldAt < throughUtc
                && (locationId == null || i.Sale.StockLocationId == locationId.Value))
            .Select(i => new
            {
                i.ProductId,
                i.ProductName,
                i.Quantity,
                i.UnitCost,
                i.TotalAmount,
                CategoryId = i.Product != null ? i.Product.CategoryId : 0,
                CategoryName = i.Product != null && i.Product.Category != null ? i.Product.Category.Name : "Uncategorized"
            })
            .ToListAsync(ct);

        var overallRevenue = saleItems.Sum(i => i.TotalAmount);
        var overallCost = saleItems.Sum(i => decimal.Round(i.UnitCost * i.Quantity, 2, MidpointRounding.AwayFromZero));
        var overallProfit = overallRevenue - overallCost;
        var overallMargin = overallRevenue > 0 ? (overallProfit / overallRevenue * 100) : 0m;

        var categories = saleItems
            .GroupBy(i => new { i.CategoryId, i.CategoryName })
            .Select(g =>
            {
                var revenue = g.Sum(x => x.TotalAmount);
                var cost = g.Sum(x => decimal.Round(x.UnitCost * x.Quantity, 2, MidpointRounding.AwayFromZero));
                var profit = revenue - cost;
                var margin = revenue > 0 ? (profit / revenue * 100) : 0m;
                var share = overallRevenue > 0 ? (revenue / overallRevenue * 100) : 0m;
                var topProduct = g.GroupBy(x => x.ProductName)
                    .OrderByDescending(px => px.Sum(p => p.TotalAmount))
                    .Select(px => px.Key)
                    .FirstOrDefault() ?? "—";

                return new CategoryPerformanceRow(
                    g.Key.CategoryId,
                    g.Key.CategoryName,
                    g.Select(x => x.ProductId).Distinct().Count(),
                    g.Sum(x => x.Quantity),
                    revenue,
                    cost,
                    profit,
                    decimal.Round(margin, 1),
                    decimal.Round(share, 1),
                    topProduct
                );
            })
            .OrderByDescending(c => c.TotalRevenue)
            .ToList();

        return new CategoryPerformanceReportData(
            startDate,
            endDate,
            locationId,
            overallRevenue,
            overallCost,
            overallProfit,
            decimal.Round(overallMargin, 1),
            locations,
            categories
        );
    }

    public async Task<SupplierSalesPerformanceReportData> GetSupplierSalesPerformanceAsync(
        ClaimsPrincipal actor,
        DateOnly startDate,
        DateOnly endDate,
        int? locationId = null,
        CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppPolicies.MakeSales, ct);

        var fromUtc = Utc(startDate);
        var throughUtc = Utc(endDate.AddDays(1));

        var locations = await db.StockLocations.AsNoTracking()
            .Where(l => !l.IsDeleted && l.CanSell)
            .OrderBy(l => l.Name)
            .Select(l => new StockLocationOption(l.Id, l.Name))
            .ToListAsync(ct);

        var saleItems = await db.SaleItems.AsNoTracking()
            .Where(i => !i.Sale!.IsDeleted && i.Sale.Status == SaleStatus.Completed
                && i.Sale.SoldAt >= fromUtc && i.Sale.SoldAt < throughUtc
                && (locationId == null || i.Sale.StockLocationId == locationId.Value))
            .Select(i => new
            {
                i.ProductId,
                i.Quantity,
                i.UnitCost,
                i.TotalAmount,
                SupplierId = i.Product != null ? i.Product.SupplierId : 0,
                SupplierName = i.Product != null && i.Product.Supplier != null ? i.Product.Supplier.Name : "Default Supplier"
            })
            .ToListAsync(ct);

        var stockBySupplier = await db.InventoryBalances.AsNoTracking()
            .Where(b => b.Product != null && !b.Product.IsDeleted
                && (locationId == null || b.StockLocationId == locationId.Value))
            .GroupBy(b => b.Product!.SupplierId)
            .Select(g => new
            {
                SupplierId = g.Key,
                Units = g.Sum(b => b.QuantityOnHand),
                Value = g.Sum(b => b.QuantityOnHand * b.Product!.CostPrice)
            })
            .ToDictionaryAsync(x => x.SupplierId, ct);

        var allSupplierIds = saleItems.Select(x => x.SupplierId)
            .Concat(stockBySupplier.Keys)
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        var supplierNames = await db.Suppliers.AsNoTracking()
            .Where(s => allSupplierIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var overallRevenue = saleItems.Sum(i => i.TotalAmount);
        var overallCost = saleItems.Sum(i => decimal.Round(i.UnitCost * i.Quantity, 2, MidpointRounding.AwayFromZero));
        var overallProfit = overallRevenue - overallCost;
        var overallStockVal = stockBySupplier.Values.Sum(s => s.Value);

        var suppliers = allSupplierIds
            .Select(supId =>
            {
                var items = saleItems.Where(x => x.SupplierId == supId).ToList();
                var rev = items.Sum(x => x.TotalAmount);
                var cost = items.Sum(x => decimal.Round(x.UnitCost * x.Quantity, 2, MidpointRounding.AwayFromZero));
                var profit = rev - cost;
                var margin = rev > 0 ? (profit / rev * 100) : 0m;
                var stock = stockBySupplier.GetValueOrDefault(supId);

                return new SupplierSalesRow(
                    supId,
                    supplierNames.GetValueOrDefault(supId, "Supplier #" + supId),
                    items.Select(x => x.ProductId).Distinct().Count(),
                    items.Sum(x => x.Quantity),
                    rev,
                    cost,
                    profit,
                    decimal.Round(margin, 1),
                    stock?.Units ?? 0m,
                    stock?.Value ?? 0m
                );
            })
            .Where(s => s.TotalRevenue > 0 || s.CurrentStockUnits > 0)
            .OrderByDescending(s => s.TotalRevenue)
            .ThenByDescending(s => s.CurrentStockValue)
            .ToList();

        return new SupplierSalesPerformanceReportData(
            startDate,
            endDate,
            locationId,
            overallRevenue,
            overallProfit,
            overallStockVal,
            locations,
            suppliers
        );
    }

    public async Task<InventoryVelocityReportData> GetInventoryVelocityAsync(
        ClaimsPrincipal actor,
        int daysThreshold = 60,
        int? locationId = null,
        int? categoryId = null,
        CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppPolicies.ManageInventory, ct);

        var threshold = Math.Clamp(daysThreshold, 14, 365);
        var now = DateTimeOffset.UtcNow;
        var cutoff = now.AddDays(-threshold);
        var zone = AccountingCalendar.Zone(configuration);

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

        // Active products with stock
        var stockQuery = db.InventoryBalances.AsNoTracking()
            .Include(b => b.Product)
            .ThenInclude(p => p!.Category)
            .Include(b => b.Product)
            .ThenInclude(p => p!.Supplier)
            .Where(b => b.Product != null && !b.Product.IsDeleted
                && b.QuantityOnHand > 0
                && (locationId == null || b.StockLocationId == locationId.Value)
                && (categoryId == null || b.Product.CategoryId == categoryId.Value));

        var balances = await stockQuery.ToListAsync(ct);

        var productIds = balances.Select(b => b.ProductId).Distinct().ToList();

        // Get last sale date and recent sales for these products
        var salesHistory = await db.SaleItems.AsNoTracking()
            .Where(i => productIds.Contains(i.ProductId)
                && !i.Sale!.IsDeleted && i.Sale.Status == SaleStatus.Completed)
            .GroupBy(i => i.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                LastSoldAt = g.Max(i => (DateTimeOffset?)i.Sale!.SoldAt),
                RecentQuantity = g.Where(i => i.Sale!.SoldAt >= cutoff).Sum(i => (decimal?)i.Quantity) ?? 0m
            })
            .ToDictionaryAsync(x => x.ProductId, ct);

        // Group by product
        var items = balances
            .GroupBy(b => b.ProductId)
            .Select(g =>
            {
                var first = g.First();
                var p = first.Product!;
                var totalQty = g.Sum(x => x.QuantityOnHand);
                var tiedUp = totalQty * p.CostPrice;
                var history = salesHistory.GetValueOrDefault(p.Id);

                int daysSinceSale;
                DateOnly? lastSoldDate = null;

                if (history?.LastSoldAt is { } lastSold)
                {
                    var localLast = TimeZoneInfo.ConvertTime(lastSold, zone);
                    lastSoldDate = DateOnly.FromDateTime(localLast.DateTime);
                    daysSinceSale = Math.Max(0, (int)(now - lastSold).TotalDays);
                }
                else
                {
                    // Never sold, days since creation
                    daysSinceSale = Math.Max(threshold, (int)(now - p.CreatedAt).TotalDays);
                }

                var recentSold = history?.RecentQuantity ?? 0m;

                StockVelocityTier tier;
                if (daysSinceSale >= threshold)
                {
                    tier = StockVelocityTier.Dead;
                }
                else if (daysSinceSale >= 45)
                {
                    tier = StockVelocityTier.Slow;
                }
                else if (recentSold >= 5 || daysSinceSale <= 7)
                {
                    tier = StockVelocityTier.Fast;
                }
                else
                {
                    tier = StockVelocityTier.Normal;
                }

                return new InventoryVelocityRow(
                    p.Id,
                    p.Name,
                    p.Barcode,
                    p.Category?.Name ?? "Uncategorized",
                    p.Supplier?.Name ?? "Default",
                    totalQty,
                    p.CostPrice,
                    p.SellingPrice,
                    tiedUp,
                    lastSoldDate,
                    daysSinceSale,
                    recentSold,
                    tier
                );
            })
            .OrderByDescending(i => i.Tier == StockVelocityTier.Dead)
            .ThenByDescending(i => i.TiedUpCapital)
            .ToList();

        var totalDeadCapital = items.Where(i => i.Tier == StockVelocityTier.Dead).Sum(i => i.TiedUpCapital);
        var deadCount = items.Count(i => i.Tier == StockVelocityTier.Dead);
        var fastCount = items.Count(i => i.Tier == StockVelocityTier.Fast);

        return new InventoryVelocityReportData(
            threshold,
            locationId,
            categoryId,
            totalDeadCapital,
            deadCount,
            fastCount,
            locations,
            categories,
            items
        );
    }

    public async Task<MarginMatrixReportData> GetMarginMatrixAsync(
        ClaimsPrincipal actor,
        DateOnly startDate,
        DateOnly endDate,
        int? locationId = null,
        CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppPolicies.MakeSales, ct);

        var fromUtc = Utc(startDate);
        var throughUtc = Utc(endDate.AddDays(1));

        var locations = await db.StockLocations.AsNoTracking()
            .Where(l => !l.IsDeleted && l.CanSell)
            .OrderBy(l => l.Name)
            .Select(l => new StockLocationOption(l.Id, l.Name))
            .ToListAsync(ct);

        var saleItems = await db.SaleItems.AsNoTracking()
            .Where(i => !i.Sale!.IsDeleted && i.Sale.Status == SaleStatus.Completed
                && i.Sale.SoldAt >= fromUtc && i.Sale.SoldAt < throughUtc
                && (locationId == null || i.Sale.StockLocationId == locationId.Value))
            .Select(i => new
            {
                i.ProductId,
                i.ProductName,
                i.Quantity,
                i.UnitCost,
                i.TotalAmount,
                CategoryName = i.Product != null && i.Product.Category != null ? i.Product.Category.Name : "Uncategorized"
            })
            .ToListAsync(ct);

        var productStats = saleItems
            .GroupBy(i => i.ProductId)
            .Select(g =>
            {
                var first = g.First();
                var vol = g.Sum(x => x.Quantity);
                var rev = g.Sum(x => x.TotalAmount);
                var cost = g.Sum(x => decimal.Round(x.UnitCost * x.Quantity, 2, MidpointRounding.AwayFromZero));
                var profit = rev - cost;
                var margin = rev > 0 ? (profit / rev * 100) : 0m;

                return new
                {
                    ProductId = g.Key,
                    first.ProductName,
                    first.CategoryName,
                    Volume = vol,
                    Revenue = rev,
                    Cost = cost,
                    Profit = profit,
                    Margin = decimal.Round(margin, 1)
                };
            })
            .ToList();

        if (productStats.Count == 0)
        {
            return new MarginMatrixReportData(
                startDate, endDate, locationId, 0, 0, 0, 0, 0, 0, locations, []);
        }

        // Calculate medians for volume and margin
        var sortedVols = productStats.Select(p => p.Volume).OrderBy(v => v).ToList();
        var sortedMargins = productStats.Select(p => p.Margin).OrderBy(m => m).ToList();

        var medianVol = sortedVols[sortedVols.Count / 2];
        var medianMargin = Math.Max(15m, sortedMargins[sortedMargins.Count / 2]);

        var items = productStats.Select(p =>
        {
            MarginQuadrant quadrant;
            if (p.Volume >= medianVol && p.Margin >= medianMargin)
            {
                quadrant = MarginQuadrant.Star;
            }
            else if (p.Volume >= medianVol && p.Margin < medianMargin)
            {
                quadrant = MarginQuadrant.CashCow;
            }
            else if (p.Volume < medianVol && p.Margin >= medianMargin)
            {
                quadrant = MarginQuadrant.Opportunity;
            }
            else
            {
                quadrant = MarginQuadrant.Laggard;
            }

            return new MarginMatrixRow(
                p.ProductId,
                p.ProductName,
                p.CategoryName,
                p.Volume,
                p.Revenue,
                p.Cost,
                p.Profit,
                p.Margin,
                quadrant
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
            items.Count(x => x.Quadrant == MarginQuadrant.Star),
            items.Count(x => x.Quadrant == MarginQuadrant.CashCow),
            items.Count(x => x.Quadrant == MarginQuadrant.Opportunity),
            items.Count(x => x.Quadrant == MarginQuadrant.Laggard),
            locations,
            items
        );
    }

    public async Task<SalesTimelineReportData> GetSalesTimelineAsync(
        ClaimsPrincipal actor,
        DateOnly startDate,
        DateOnly endDate,
        int? locationId = null,
        CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppPolicies.MakeSales, ct);

        var fromUtc = Utc(startDate);
        var throughUtc = Utc(endDate.AddDays(1));
        var zone = AccountingCalendar.Zone(configuration);

        var sales = await db.Sales.AsNoTracking()
            .Where(s => !s.IsDeleted && s.Status == SaleStatus.Completed
                && s.SoldAt >= fromUtc && s.SoldAt < throughUtc
                && (locationId == null || s.StockLocationId == locationId.Value))
            .Select(s => new
            {
                s.Id,
                s.SoldAt,
                s.TotalAmount
            })
            .ToListAsync(ct);

        var saleIds = sales.Select(s => s.Id).ToList();

        var itemCosts = await db.SaleItems.AsNoTracking()
            .Where(i => saleIds.Contains(i.SaleId))
            .Select(i => new
            {
                i.SaleId,
                Cost = decimal.Round(i.UnitCost * i.Quantity, 2, MidpointRounding.AwayFromZero)
            })
            .ToListAsync(ct);

        var costBySale = itemCosts
            .GroupBy(i => i.SaleId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Cost));

        var pointsByDate = sales
            .GroupBy(s => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(s.SoldAt, zone).DateTime))
            .ToDictionary(g => g.Key, g => new
            {
                Revenue = g.Sum(x => x.TotalAmount),
                Profit = g.Sum(x => x.TotalAmount - costBySale.GetValueOrDefault(x.Id)),
                Orders = g.Count()
            });

        var totalDays = Math.Max(1, endDate.DayNumber - startDate.DayNumber + 1);
        var points = Enumerable.Range(0, totalDays)
            .Select(offset =>
            {
                var d = startDate.AddDays(offset);
                var data = pointsByDate.GetValueOrDefault(d);
                return new SalesTimelinePoint(
                    d,
                    data?.Revenue ?? 0m,
                    data?.Profit ?? 0m,
                    data?.Orders ?? 0
                );
            })
            .ToList();

        return new SalesTimelineReportData(points);
    }
}
