using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;
using POSApp.Persistence.Entities;

namespace POSApp.Services;

public sealed class InventoryValuation(IServiceScopeFactory scopes)
{
    public async Task<InventoryValueReport> ReadAsync(ClaimsPrincipal actor, int? supplierId = null, int? locationId = null,
        string? search = null, int page = 1, CancellationToken ct = default, string? sort = null, int pageSize = 30)
    {
        return await StoreOperation.RunAsync<InventoryValueReport>(async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
            var products = db.Products.AsNoTracking().TagWith("Inventory.Valuation").Where(product => (!product.IsDeleted || product.InventoryBalances.Any(stock => stock.QuantityOnHand != 0))
                && (supplierId == null || product.SupplierId == supplierId));
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                var normalizedTerm = NameNormalization.Normalize(term);
                products = products.Where(product => product.NormalizedName.Contains(normalizedTerm)
                    || (product.Barcode != null && product.Barcode.ToLower().Contains(term.ToLower())));
            }
            var query = products.Select(product => new ValueQuery
            {
                Id = product.Id,
                Name = product.Name,
                Barcode = product.Barcode,
                Unit = product.Unit,
                Supplier = product.Supplier!.Name,
                Quantity = product.InventoryBalances.Where(stock => locationId == null || stock.StockLocationId == locationId)
                    .Select(stock => (decimal?)stock.QuantityOnHand).Sum() ?? 0,
                UnitCost = product.CostPrice,
                Value = product.CostPrice * (product.InventoryBalances.Where(stock => locationId == null || stock.StockLocationId == locationId)
                    .Select(stock => (decimal?)stock.QuantityOnHand).Sum() ?? 0),
                Archived = product.IsDeleted
            });
            var aggregate = await query.GroupBy(_ => 1)
                .Select(g => new
                {
                    Count = g.Count(),
                    Value = g.Sum(row => (decimal?)row.Value) ?? 0m
                })
                .FirstOrDefaultAsync(ct);

            var count = aggregate?.Count ?? 0;
            var value = aggregate?.Value ?? 0m;
            var size = pageSize > 250 ? Math.Clamp(pageSize, 1, 10001) : QueryPagination.Size(pageSize);
            var currentPage = Math.Clamp(page, 1, Math.Max(1, (count + size - 1) / size));
            var ordered = sort switch
            {
                "id" => query.OrderBy(row => row.Id),
                "id-desc" => query.OrderByDescending(row => row.Id),
                "name-desc" => query.OrderByDescending(row => row.Name),
                "supplier" => query.OrderBy(row => row.Supplier),
                "supplier-desc" => query.OrderByDescending(row => row.Supplier),
                "quantity" => query.OrderBy(row => row.Quantity),
                "quantity-desc" => query.OrderByDescending(row => row.Quantity),
                "cost" => query.OrderBy(row => row.UnitCost),
                "cost-desc" => query.OrderByDescending(row => row.UnitCost),
                "value" => query.OrderBy(row => row.Value),
                "value-desc" => query.OrderByDescending(row => row.Value),
                _ => query.OrderBy(row => row.Id)
            };
            var rows = await ordered.ThenBy(row => row.Id).Skip((currentPage - 1) * size).Take(size).ToListAsync(ct);
            decimal? due = supplierId is null ? null : await db.SupplierBalances.Where(balance => balance.SupplierId == supplierId)
                .Select(balance => (decimal?)balance.CurrentBalance).SingleOrDefaultAsync(ct) ?? 0;
            var suppliers = await db.Suppliers.Where(supplier => !supplier.IsDeleted || supplier.Id == supplierId)
                .OrderBy(supplier => supplier.Name).Select(supplier => new Lookup(supplier.Id, supplier.Name)).ToArrayAsync(ct);
            var locations = await db.StockLocations.Where(location => !location.IsDeleted || location.Id == locationId)
                .OrderBy(location => location.Name).Select(location => new Lookup(location.Id, location.Name)).ToArrayAsync(ct);
            return new(rows.Select(row => new InventoryValueRow(row.Id, row.Name, row.Barcode, row.Unit, row.Supplier,
                row.Quantity, row.UnitCost, row.Value, row.Archived)).ToArray(), count, currentPage, value, due, suppliers, locations);
        }, ct);
    }

    private sealed class ValueQuery
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Barcode { get; set; }
        public string Unit { get; set; } = "";
        public string Supplier { get; set; } = "";
        public decimal Quantity { get; set; }
        public decimal UnitCost { get; set; }
        public decimal Value { get; set; }
        public bool Archived { get; set; }
    }
}
