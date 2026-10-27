using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;

namespace POSApp.Services;

public sealed partial class ReportService
{
    public async Task<ProductSalesReport> ProductsAsync(ClaimsPrincipal actor, string? period, DateOnly? start, DateOnly? end,
        int page = 1, CancellationToken ct = default, string? search = null, string? sort = null, int? locationId = null, int pageSize = 30)
    {
        return await StoreOperation.RunAsync<ProductSalesReport>(async () =>
        {
            var limit = Math.Clamp(pageSize, 1, 10000);
            var range = ResolveRange(period, start, end, AccountingCalendar.Zone(configuration), DateTimeOffset.UtcNow);
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
            var locations = await LocationsAsync(db, locationId, ct);
            var sales = db.Sales.AsNoTracking().Where(sale => !sale.IsDeleted && sale.Status == SaleStatus.Completed
                && sale.SoldAt >= range.FromUtc && sale.SoldAt < range.ToUtc
                && (locationId == null || sale.StockLocationId == locationId));
            var snapshots = await ReadLinesAsync(sales, ct);
            var term = (search ?? "").Trim();
            var numericId = int.TryParse(term.TrimStart('#'), out var parsed) ? parsed : -1;
            var products = snapshots.GroupBy(row => new { row.ProductId, row.Unit })
                .Select(group => new ProductSalesRow(group.Key.ProductId, group.Last().Name, group.Last().Barcode, group.Key.Unit,
                    group.Sum(row => row.Quantity), group.Sum(row => row.Cost ?? 0), group.Sum(row => row.Subtotal),
                    group.Sum(row => row.Subtotal - row.Total), group.Sum(row => row.Total)))
                .Where(row => term.Length == 0 || row.ProductId == numericId || row.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || row.Barcode.Contains(term, StringComparison.OrdinalIgnoreCase)).ToArray();
            var ordered = sort switch
            {
                "id" => products.OrderBy(row => row.ProductId),
                "id-desc" => products.OrderByDescending(row => row.ProductId),
                "name-desc" => products.OrderByDescending(row => row.Name),
                "average-cost" => products.OrderBy(row => row.AverageCost),
                "average-cost-desc" => products.OrderByDescending(row => row.AverageCost),
                "average-price" => products.OrderBy(row => row.AveragePrice),
                "average-price-desc" => products.OrderByDescending(row => row.AveragePrice),
                "subtotal" => products.OrderBy(row => row.Subtotal),
                "subtotal-desc" => products.OrderByDescending(row => row.Subtotal),
                "discount" => products.OrderBy(row => row.Discount),
                "discount-desc" => products.OrderByDescending(row => row.Discount),
                "quantity" => products.OrderBy(row => row.Quantity),
                "quantity-desc" => products.OrderByDescending(row => row.Quantity),
                "cost" => products.OrderBy(row => row.Cost),
                "cost-desc" => products.OrderByDescending(row => row.Cost),
                "total" => products.OrderBy(row => row.Total),
                "total-desc" => products.OrderByDescending(row => row.Total),
                "profit" => products.OrderBy(row => row.Profit),
                "profit-desc" => products.OrderByDescending(row => row.Profit),
                _ => products.OrderBy(row => row.ProductId)
            };
            var current = Math.Clamp(page, 1, Math.Max(1, (products.Length + limit - 1) / limit));
            return new(ordered.ThenBy(row => row.ProductId).Skip((current - 1) * limit).Take(limit).ToArray(), range,
                products.Length, current, products.Sum(row => row.Cost), products.Sum(row => row.Subtotal),
                products.Sum(row => row.Discount), products.Sum(row => row.Total))
            {
                Locations = locations
            };
        }, ct);
    }

    private static Task<bool> CanViewCostsAsync(AppDbContext db, int actorId, CancellationToken ct) =>
        db.UserRoles.AnyAsync(membership => membership.UserId == actorId && db.Roles.Any(role => role.Id == membership.RoleId
            && (role.Name == AppRoles.Admin || role.Name == AppRoles.InventoryManager)), ct);

    private static async Task<SaleLineSnapshot[]> ReadLinesAsync(IQueryable<Sale> sales, CancellationToken ct)
    {
        var lines = await sales.SelectMany(sale => sale.Items).Select(item => new
        {
            item.SaleId,
            SaleTotal = item.Sale!.TotalAmount,
            item.Id,
            item.ProductId,
            item.ProductName,
            item.ProductSku,
            item.Unit,
            item.Quantity,
            item.UnitPrice,
            item.UnitDiscount,
            item.UnitCost
        }).OrderBy(row => row.SaleId).ThenBy(row => row.Id).ToArrayAsync(ct);
        var result = new List<SaleLineSnapshot>(lines.Length);
        foreach (var group in lines.GroupBy(row => row.SaleId))
        {
            var items = group.ToArray();
            decimal allocated = 0;
            for (var index = 0; index < items.Length; index++)
            {
                var item = items[index];
                var total = index == items.Length - 1 ? item.SaleTotal - allocated
                    : Round((item.UnitPrice - item.UnitDiscount) * item.Quantity);
                allocated += total;
                result.Add(new(item.SaleId, item.ProductId, item.ProductName, item.ProductSku, item.Unit,
                    item.Quantity, item.UnitPrice, item.UnitDiscount, Round(item.UnitPrice * item.Quantity),
                    total, item.UnitCost, Round(item.UnitCost * item.Quantity)));
            }
        }
        return result.ToArray();
    }

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private sealed record SaleLineSnapshot(int SaleId, int ProductId, string Name, string Barcode, string Unit,
        decimal Quantity, decimal UnitPrice, decimal UnitDiscount, decimal Subtotal, decimal Total,
        decimal UnitCost, decimal? Cost)
    {
        public SalesReportLine Line(bool costs) => new(ProductId, Name, Barcode, Unit, Quantity, UnitPrice,
            UnitDiscount, Subtotal, Total, costs ? UnitCost : null, costs ? Cost : null);
    }
}
