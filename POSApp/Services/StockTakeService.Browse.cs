using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;

namespace POSApp.Services;

public sealed partial class StockTakeService
{
    public async Task<StockTakeList> BrowseAsync(ClaimsPrincipal actor, int id, string? search = null,
        string? status = null, int? categoryId = null, string? sort = null, int page = 1, int pageSize = 30,
        CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
        var take = await db.StockTakes.AsNoTracking().Where(take => take.Id == id)
            .Select(take => new { take.Id, take.StockLocationId, Location = take.StockLocation!.Name, take.BusinessDate, take.CompletedAt })
            .SingleOrDefaultAsync(ct)
            ?? throw new ValidationException("Stock take not found.");
        var saved = db.StockTakeItems.AsNoTracking()
            .Where(item => item.StockTakeId == id && (item.IsIncluded || item.CountedQuantity != null));
        var products = db.Products.AsNoTracking().Where(product => !product.IsNonStocked
            && (!product.IsDeleted
                || product.InventoryBalances.Any(balance => balance.StockLocationId == take.StockLocationId && balance.QuantityOnHand != 0)
                || saved.Any(item => item.ProductId == product.Id)));
        var categories = await products
            .Select(product => new { Id = product.CategoryId, Name = product.Category!.Name })
            .Distinct()
            .OrderBy(category => category.Name)
            .Select(category => new Lookup(category.Id, category.Name)).ToArrayAsync(ct);
        var query = from product in products
                    join item in saved on product.Id equals item.ProductId into counts
                    from item in counts.DefaultIfEmpty()
                    select new BrowseLine
                    {
                        Id = product.Id,
                        Name = item == null ? product.Name : item.ProductName,
                        NormalizedName = item == null ? product.NormalizedName : item.ProductName.ToUpper(),
                        Barcode = item == null ? product.Barcode : item.Barcode,
                        Unit = item == null ? product.Unit : item.Unit,
                        AllowFraction = item == null ? product.AllowFractionUnit : item.AllowFractionUnit,
                        CategoryId = product.CategoryId,
                        Category = product.Category == null ? null : product.Category.Name,
                        Added = item != null,
                        Expected = item == null
                            ? product.InventoryBalances.Where(balance => balance.StockLocationId == take.StockLocationId)
                                .Select(balance => (decimal?)balance.QuantityOnHand).Sum() ?? 0
                            : item.ExpectedQuantity,
                        Counted = item == null ? null : item.CountedQuantity,
                        Cost = item == null ? product.CostPrice : item.UnitCost,
                        MovementId = item == null
                            ? db.InventoryMovements.Where(move => move.StockLocationId == take.StockLocationId && move.ProductId == product.Id)
                                .Select(move => (int?)move.Id).Max() ?? 0
                            : item.LastMovementId
                    };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            if (term.Length > 200)
            {
                throw new ValidationException("Keep product searches within 200 characters.");
            }
            int.TryParse(term.TrimStart('#'), out var productId);
            var normalized = NameNormalization.Normalize(term);
            query = query.Where(row => row.Id == productId || row.NormalizedName.Contains(normalized)
                || row.Barcode != null && row.Barcode.ToUpper().Contains(normalized));
        }
        if (categoryId is not null)
        {
            query = query.Where(row => row.CategoryId == categoryId);
        }
        query = status switch
        {
            "added" => query.Where(row => row.Added),
            "differences" => query.Where(row => row.Counted != null && row.Counted != row.Expected),
            "counted" => query.Where(row => row.Counted != null),
            "uncounted" => query.Where(row => row.Counted == null),
            "recount" => query.Where(row => row.Added && row.Counted == null),
            "shortage" => query.Where(row => row.Counted < row.Expected),
            "extra" => query.Where(row => row.Counted > row.Expected),
            "matched" => query.Where(row => row.Counted != null && row.Counted == row.Expected),
            _ => query
        };
        var summary = await query.GroupBy(row => 1).Select(group => new
        {
            Count = group.Count(),
            Counted = group.Count(row => row.Counted != null),
            Shortages = group.Count(row => row.Counted < row.Expected),
            Extras = group.Count(row => row.Counted > row.Expected)
        }).SingleOrDefaultAsync(ct);
        var total = summary?.Count ?? 0;
        var size = pageSize is 30 or 50 or 100 ? pageSize : 30;
        var current = Math.Clamp(page, 1, Math.Max(1, (total + size - 1) / size));
        var ordered = sort switch
        {
            "id" => query.OrderBy(row => row.Id),
            "id-desc" => query.OrderByDescending(row => row.Id),
            "name-desc" => query.OrderByDescending(row => row.Name),
            "barcode" => query.OrderBy(row => row.Barcode),
            "barcode-desc" => query.OrderByDescending(row => row.Barcode),
            "category" => query.OrderBy(row => row.Category),
            "category-desc" => query.OrderByDescending(row => row.Category),
            "expected" => query.OrderBy(row => row.Expected),
            "expected-desc" => query.OrderByDescending(row => row.Expected),
            "counted" => query.OrderBy(row => row.Counted ?? -1),
            "counted-desc" => query.OrderByDescending(row => row.Counted ?? -1),
            "difference" => query.OrderBy(row => row.Counted - row.Expected),
            "difference-desc" => query.OrderByDescending(row => row.Counted - row.Expected),
            "cost" => query.OrderBy(row => row.Cost),
            "cost-desc" => query.OrderByDescending(row => row.Cost),
            "value" => query.OrderBy(row => (row.Counted - row.Expected) * row.Cost),
            "value-desc" => query.OrderByDescending(row => (row.Counted - row.Expected) * row.Cost),
            _ => query.OrderBy(row => row.Name)
        };
        var rows = await ordered.ThenBy(row => row.Id).Skip((current - 1) * size).Take(size).ToArrayAsync(ct);
        return new(take.Id, take.Location, take.BusinessDate, take.CompletedAt,
            rows.Select(row => new StockTakeListRow(new(row.Id, row.Name, row.Barcode, row.Unit,
                row.AllowFraction, row.Expected, row.Counted, row.Cost)
                {
                    LastMovementId = row.MovementId
                }, row.Category, row.Added)).ToArray(),
            categories, total, summary?.Counted ?? 0, summary?.Shortages ?? 0, summary?.Extras ?? 0, current, size);
    }

    private sealed class BrowseLine
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public string NormalizedName { get; init; } = "";
        public string? Barcode { get; init; }
        public string Unit { get; init; } = "";
        public bool AllowFraction { get; init; }
        public int? CategoryId { get; init; }
        public string? Category { get; init; }
        public bool Added { get; init; }
        public decimal Expected { get; init; }
        public decimal? Counted { get; init; }
        public decimal Cost { get; init; }
        public int MovementId { get; init; }
    }
}
