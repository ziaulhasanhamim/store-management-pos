using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;
using POSApp.Persistence.Entities;

namespace POSApp.Services;

public sealed partial class StockAdjustmentService
{
    public async Task<StockAdjustmentOptions> OptionsAsync(ClaimsPrincipal actor, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
        var locations = await db.StockLocations.AsNoTracking()
            .Where(location => !location.IsDeleted)
            .OrderBy(location => location.Name)
            .Select(location => new Lookup(location.Id, location.Name))
            .ToArrayAsync(ct);
        var selected = await db.Users.Where(user => user.Id == actorId)
            .Select(user => user.CashLocationId).SingleAsync(ct);
        if (!locations.Any(location => location.Id == selected))
        {
            selected = locations.FirstOrDefault()?.Id;
        }
        var suppliers = await db.Suppliers.AsNoTracking().Where(supplier => !supplier.IsDeleted)
            .OrderBy(supplier => supplier.Name).Select(supplier => new Lookup(supplier.Id, supplier.Name)).ToArrayAsync(ct);
        var categories = await db.Categories.AsNoTracking().Where(category => !category.IsDeleted).OrderBy(category => category.Name)
            .Select(category => new Lookup(category.Id, category.Name)).ToArrayAsync(ct);
        var attributes = await InventoryService.AttributeDefinitionQuery(db).ToArrayAsync(ct);
        return new(locations, selected, await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct))
        {
            Suppliers = suppliers,
            Categories = categories,
            Attributes = attributes
        };
    }

    public async Task<IReadOnlyList<StockAdjustmentProduct>> SearchAsync(
        ClaimsPrincipal actor, int locationId, string? search, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
        await ValidateLocationAsync(db, locationId, ct);
        if (string.IsNullOrWhiteSpace(search))
        {
            return [];
        }
        var term = search.Trim();
        var normalizedTerm = NameNormalization.Normalize(term);
        _ = int.TryParse(term.TrimStart('#'), out var id);
        var products = await db.Products.AsNoTracking()
            .Where(product => !product.IsDeleted && !product.IsNonStocked
                && (product.Id == id || product.NormalizedName.Contains(normalizedTerm)
                    || (product.Barcode != null && product.Barcode.ToLower().Contains(term.ToLower()))))
            .OrderBy(product => product.Id == id || product.Barcode == search ? 0 : 1)
            .ThenBy(product => product.Name)
            .Take(12)
            .Select(product => new StockAdjustmentProduct(product.Id, product.Name, product.Barcode,
                product.Unit, product.AllowFractionUnit, product.CostPrice))
            .ToArrayAsync(ct);
        var productIds = products.Select(product => product.Id).ToArray();
        var attributes = await db.ProductAttributeValues.AsNoTracking()
            .Where(value => productIds.Contains(value.ProductId) && !value.Definition!.IsDeleted && value.Definition.ShowInStockAdjustment)
            .Select(value => new
            {
                value.ProductId,
                Attribute = new ItemAttributeDisplay(value.Definition!.Key, value.Definition.DisplayName,
                    value.Definition.Kind == ProductAttributeKind.Checkbox
                        ? value.Value == "true" ? "Yes" : "No"
                        : value.DisplayValue ?? (value.Definition.Kind == ProductAttributeKind.Select
                            ? value.Definition.Options.Where(option => option.Value == value.Value)
                                .Select(option => option.DisplayName).FirstOrDefault() ?? value.Value
                            : value.Value),
                    value.Definition.ShowInItems, value.Definition.ShowInSales, value.Definition.ShowInReceiving,
                    value.Definition.ShowInStockAdjustment, value.Value)
            }).ToListAsync(ct);
        var byProduct = attributes.GroupBy(value => value.ProductId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ItemAttributeDisplay>)group.Select(value => value.Attribute).ToArray());
        return products.Select(product => product with { Attributes = byProduct.GetValueOrDefault(product.Id, []) }).ToArray();
    }

    public async Task<IReadOnlyList<StockAdjustmentHistory>> HistoryAsync(ClaimsPrincipal actor, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
        return await db.StockAdjustments.AsNoTracking().OrderByDescending(adjustment => adjustment.Id).Take(100)
            .Select(adjustment => new StockAdjustmentHistory(adjustment.Id, adjustment.StockLocation!.Name, adjustment.Date,
                adjustment.Items.Count, adjustment.Items.Sum(item => item.Quantity * item.UnitCost)))
            .ToArrayAsync(ct);
    }

    public async Task<PagedResult<StockAdjustmentHistory>> HistoryPageAsync(ClaimsPrincipal actor, int page = 1, int pageSize = 30, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
        return await db.StockAdjustments.AsNoTracking().OrderByDescending(adjustment => adjustment.Id)
            .Select(adjustment => new StockAdjustmentHistory(adjustment.Id, adjustment.StockLocation!.Name, adjustment.Date,
                adjustment.Items.Count, adjustment.Items.Sum(item => item.Quantity * item.UnitCost)))
            .ToPageAsync(page, pageSize, ct);
    }

    public async Task<StockAdjustmentDetail> ReadAsync(ClaimsPrincipal actor, int id, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
        var adjustment = await db.StockAdjustments.AsNoTracking().Include(item => item.StockLocation).Include(item => item.Items)
            .SingleOrDefaultAsync(item => item.Id == id, ct) ?? throw new ValidationException("Stock adjustments not found.");
        var by = await db.Users.Where(user => user.Id == adjustment.CreatedByUserId).Select(user => user.UserName).SingleOrDefaultAsync(ct);
        var items = adjustment.Items.OrderBy(item => item.ProductName)
            .Select(item => new StockAdjustmentLine(new(item.ProductId, item.ProductName, item.Barcode, item.Unit,
                false, item.UnitCost), item.Quantity, item.UnitCost)).ToArray();
        return new(adjustment.Id, adjustment.StockLocation!.Name, adjustment.Date, adjustment.Notes, adjustment.CreatedAt, by ?? "Staff", items);
    }
}
