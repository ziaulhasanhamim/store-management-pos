using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;
using POSApp.Persistence.Entities;
using POSApp.Security;

namespace POSApp.Services;

// Each operation owns a short-lived scope/context, including calls from interactive circuits.
public sealed class InventoryService(IServiceScopeFactory scopes, IConfiguration? configuration = null)
{
    private static async Task<int> RequireActor(AppDbContext db, ClaimsPrincipal actor, bool manage, CancellationToken ct)
    {
        var (id, _) = await RequireActorWithPermissions(db, actor, manage, ct);
        return id;
    }

    private static async Task<(int Id, bool CanSeeCost)> RequireActorWithPermissions(AppDbContext db, ClaimsPrincipal actor, bool manage, CancellationToken ct)
    {
        if (actor.Identity?.IsAuthenticated != true ||
            !int.TryParse(actor.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            throw new UnauthorizedAccessException();
        }
        var roles = await db.UserRoles
            .Where(userRole => userRole.UserId == id)
            .Join(
                db.Roles,
                userRole => userRole.RoleId,
                role => role.Id,
                (userRole, role) => role.Name
            )
            .ToListAsync(ct);

        var canSeeCost = roles.Any(r => r == AppRoles.Admin || r == AppRoles.InventoryManager);
        if (!canSeeCost && (manage || !roles.Contains(AppRoles.SalesAssociate)))
        {
            throw new UnauthorizedAccessException();
        }

        return (id, canSeeCost);
    }

    private static async Task<bool> CanSeeCost(AppDbContext db, int id, CancellationToken ct) =>
    await db.UserRoles
        .Where(userRole => userRole.UserId == id)
        .Join(
            db.Roles,
            userRole => userRole.RoleId,
            role => role.Id,
            (userRole, role) => new { userRole, role }
        )
        .Where(joined => joined.role.Name == AppRoles.Admin || joined.role.Name == AppRoles.InventoryManager)
        .AnyAsync(ct);

    private static IQueryable<ItemRow> ItemQuery(IQueryable<Product> products, bool costs, int? locationId = null) => products
        .Select(product => new ItemRow(
            product.Id,
            product.Name,
            product.Barcode,
            product.Unit,
            product.Category == null ? null : product.Category.Name,
            product.SellingPrice,
            costs ? product.CostPrice : null,
            product.InventoryBalances.Where(balance => locationId == null || balance.StockLocationId == locationId)
                .Select(balance => (decimal?)balance.QuantityOnHand).Sum() ?? 0,
            product.AllowFractionUnit,
            product.SupplierId,
            product.Supplier!.Name,
            product.IsNonStocked));

    public async Task<ItemLocationOptions> ItemLocationsAsync(ClaimsPrincipal actor, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = await RequireActor(db, actor, false, ct);
        var locations = await db.StockLocations.AsNoTracking()
            .Where(location => !location.IsDeleted)
            .OrderBy(location => location.Name)
            .Select(location => new Lookup(location.Id, location.Name))
            .ToListAsync(ct);
        var selected = await db.Users
            .Where(user => user.Id == actorId && user.CashLocation != null && user.CashLocation.CanSell)
            .Select(user => user.CashLocationId)
            .SingleOrDefaultAsync(ct);

        if (!locations.Any(location => location.Id == selected))
        {
            selected = locations.FirstOrDefault()?.Id;
        }

        return new(locations, selected);
    }

    public async Task<IReadOnlyList<ItemRow>> ListAsync(ClaimsPrincipal actor, string? search = null, int page = 1, CancellationToken ct = default, int? locationId = null, bool stockedOnly = false, string? sort = null, int pageSize = 30)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (id, costs) = await RequireActorWithPermissions(db, actor, false, ct);
        var query = db.Products.AsNoTracking().TagWith("Inventory.List").Where(p => !p.IsDeleted);
        if (stockedOnly)
        {
            query = query.Where(product => !product.IsNonStocked);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var normalizedTerm = NameNormalization.Normalize(term);
            _ = int.TryParse(term, out var productId);
            query = query.Where(p => p.NormalizedName.Contains(normalizedTerm)
                || (p.Barcode != null && p.Barcode.ToLower().Contains(term.ToLower())) || p.Id == productId);
        }
        if (!costs && sort is "cost" or "cost-desc" or "value" or "value-desc")
        {
            throw new UnauthorizedAccessException("Cost sorting requires inventory access.");
        }
        var ordered = sort switch
        {
            "id" => query.OrderBy(product => product.Id),
            "id-desc" => query.OrderByDescending(product => product.Id),
            "name" => query.OrderBy(product => product.Name),
            "name-desc" => query.OrderByDescending(product => product.Name),
            "barcode" => query.OrderBy(product => product.Barcode),
            "barcode-desc" => query.OrderByDescending(product => product.Barcode),
            "supplier" => query.OrderBy(product => product.Supplier!.Name),
            "supplier-desc" => query.OrderByDescending(product => product.Supplier!.Name),
            "price" => query.OrderBy(product => product.SellingPrice),
            "price-desc" => query.OrderByDescending(product => product.SellingPrice),
            "cost" => query.OrderBy(product => product.CostPrice),
            "cost-desc" => query.OrderByDescending(product => product.CostPrice),
            "quantity" => query.OrderBy(product => product.InventoryBalances.Where(balance => locationId == null || balance.StockLocationId == locationId).Select(balance => (decimal?)balance.QuantityOnHand).Sum() ?? 0),
            "quantity-desc" => query.OrderByDescending(product => product.InventoryBalances.Where(balance => locationId == null || balance.StockLocationId == locationId).Select(balance => (decimal?)balance.QuantityOnHand).Sum() ?? 0),
            "value" => query.OrderBy(product => product.CostPrice * (product.InventoryBalances.Where(balance => locationId == null || balance.StockLocationId == locationId).Select(balance => (decimal?)balance.QuantityOnHand).Sum() ?? 0)),
            "value-desc" => query.OrderByDescending(product => product.CostPrice * (product.InventoryBalances.Where(balance => locationId == null || balance.StockLocationId == locationId).Select(balance => (decimal?)balance.QuantityOnHand).Sum() ?? 0)),
            _ => query.OrderBy(product => product.Id)
        };
        var size = pageSize > 250 ? Math.Clamp(pageSize, 1, 10001) : QueryPagination.Size(pageSize);
        var items = await ItemQuery(
                ordered.ThenBy(product => product.Id)
                    .Skip((Math.Clamp(page, 1, 100000) - 1) * size)
                    .Take(size), costs, locationId)
            .ToListAsync(ct);
        return await AddAttributesAsync(db, items, ct);
    }

    public async Task<int> CountAsync(ClaimsPrincipal actor, string? search = null, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await RequireActor(db, actor, false, ct);
        var query = db.Products.AsNoTracking().Where(product => !product.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var normalizedTerm = NameNormalization.Normalize(term);
            _ = int.TryParse(term, out var productId);
            query = query.Where(product => product.NormalizedName.Contains(normalizedTerm)
                || product.Barcode != null && product.Barcode.ToLower().Contains(term.ToLower())
                || product.Id == productId);
        }
        return await query.CountAsync(ct);
    }

    public Task<ItemDetail?> FindAsync(ClaimsPrincipal actor, int productId, CancellationToken ct = default)
    {
        return FindCoreAsync(actor, productId, false, null, ct);
    }

    public Task<ItemDetail?> FindForLocationAsync(
        ClaimsPrincipal actor,
        int productId,
        int? locationId,
        CancellationToken ct = default, int page = 1, int pageSize = 30)
    {
        return FindCoreAsync(actor, productId, true, locationId, ct, page, pageSize);
    }

    private async Task<ItemDetail?> FindCoreAsync(
        ClaimsPrincipal actor,
        int productId,
        bool filterHistoryByLocation,
        int? locationId,
        CancellationToken ct, int page = 1, int pageSize = 30)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (id, costs) = await RequireActorWithPermissions(db, actor, false, ct);
        var item = await ItemQuery(
                db.Products.AsNoTracking()
                    .TagWith("Inventory.Find")
                    .Where(product => !product.IsDeleted && product.Id == productId), costs)
            .SingleOrDefaultAsync(ct);
        if (item is null)
        {
            return null;
        }
        var itemAttributes = await AttributesForProductsAsync(db, [item.Id], ct);
        item = item with { Attributes = itemAttributes.Select(value => value.Attribute).ToArray() };

        var extra = await db.Products
            .Where(product => product.Id == productId)
            .Select(product => new { product.Description, product.CategoryId })
            .SingleAsync(ct);

        IReadOnlyList<ProductStockLocation> locations = [];
        int? selectedLocationId = null;
        var movements = db.InventoryMovements.AsNoTracking()
            .Where(movement => movement.ProductId == productId);

        if (filterHistoryByLocation)
        {
            // Keep archived locations selectable if this product has history there.
            locations = await db.StockLocations.AsNoTracking()
                .Where(location => !location.IsDeleted ||
                    db.InventoryMovements.Any(movement =>
                        movement.ProductId == productId && movement.StockLocationId == location.Id))
                .OrderBy(location => location.Name)
                .ThenBy(location => location.Id)
                .Select(location => new ProductStockLocation(
                    location.Id,
                    location.Name,
                    location.InventoryBalances.Where(balance => balance.ProductId == productId)
                        .Select(balance => (decimal?)balance.QuantityOnHand).Sum() ?? 0,
                    location.IsDeleted))
                .ToListAsync(ct);

            selectedLocationId = locations.FirstOrDefault(location => location.Id == locationId)?.Id
                ?? locations.FirstOrDefault()?.Id;
            movements = movements.Where(movement => movement.StockLocationId == selectedLocationId);
        }

        // Filter the location before counting and paging its movement history.
        var history = await movements
            .OrderByDescending(movement => movement.Id)
            .Select(movement => new StockEvent(
                movement.Id,
                movement.OccurredAt,
                movement.Type,
                movement.Quantity,
                costs ? movement.UnitCost : null,
                movement.StockLocation!.Name,
                movement.PurchaseItem == null ? null : movement.PurchaseItem.Purchase!.PurchaseNumber,
                movement.Notes))
            .ToPageAsync(page, pageSize, ct);

        IReadOnlyList<ProductCostHistoryRow> costHistory = [];
        if (costs)
        {
            costHistory = await db.ProductCostHistories.AsNoTracking()
                .Where(h => h.ProductId == productId)
                .OrderByDescending(h => h.Id)
                .Select(h => new ProductCostHistoryRow(
                    h.Id,
                    h.CreatedAt,
                    h.OldCostPrice,
                    h.NewCostPrice,
                    h.Reason,
                    h.CreatedByUser != null ? h.CreatedByUser.UserName : null))
                .ToListAsync(ct);
        }

        return new(item, extra.Description, extra.CategoryId, history.Items, locations, selectedLocationId)
        {
            HistoryPagination = history.Pagination,
            Attributes = item.Attributes,
            CostHistory = costHistory
        };
    }

    private static async Task<IReadOnlyList<ItemRow>> AddAttributesAsync(
        AppDbContext db,
        IReadOnlyList<ItemRow> items,
        CancellationToken ct)
    {
        if (items.Count == 0)
        {
            return items;
        }

        var ids = items.Select(item => item.Id).ToArray();
        var values = await AttributesForProductsAsync(db, ids, ct);
        var byProduct = values.GroupBy(value => value.ProductId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ItemAttributeDisplay>)group.Select(value => value.Attribute).ToArray());
        return items.Select(item => item with
        {
            Attributes = byProduct.GetValueOrDefault(item.Id, [])
        }).ToArray();
    }

    private static async Task<IReadOnlyList<(int ProductId, ItemAttributeDisplay Attribute)>> AttributesForProductsAsync(
        AppDbContext db,
        int[] productIds,
        CancellationToken ct)
    {
        var values = await db.ProductAttributeValues.AsNoTracking()
            .Where(value => productIds.Contains(value.ProductId) && !value.Definition!.IsDeleted)
            .OrderBy(value => value.Definition!.DisplayOrder)
            .ThenBy(value => value.Definition!.DisplayName)
            .Select(value => new
            {
                value.ProductId,
                Attribute = new ItemAttributeDisplay(
                    value.Definition!.Key,
                    value.Definition.DisplayName,
                    value.Definition.Kind == ProductAttributeKind.Checkbox
                        ? value.Value == "true" ? "Yes" : "No"
                        : value.DisplayValue ?? (value.Definition.Kind == ProductAttributeKind.Select
                            ? value.Definition.Options
                                .Where(option => option.Value == value.Value)
                                .Select(option => option.DisplayName)
                                .FirstOrDefault() ?? value.Value
                            : value.Value),
                    value.Definition.ShowInItems,
                    value.Definition.ShowInSales,
                    value.Definition.ShowInReceiving,
                    value.Definition.ShowInStockAdjustment,
                    value.Value)
            })
            .ToListAsync(ct);
        return values.Select(value => (value.ProductId, value.Attribute)).ToArray();
    }

    public async Task<IReadOnlyList<Lookup>> CategoriesAsync(ClaimsPrincipal actor, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await RequireActor(db, actor, true, ct);
        return await db.Categories
            .Where(category => !category.IsDeleted)
            .OrderBy(category => category.Name)
            .Select(category => new Lookup(category.Id, category.Name))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ProductAttributeDefinitionOption>> AttributeDefinitionsAsync(
        ClaimsPrincipal actor,
        CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await RequireActor(db, actor, true, ct);
        return await AttributeDefinitionQuery(db).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ItemAttributeColumn>> ItemAttributeColumnsAsync(
        ClaimsPrincipal actor,
        CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await RequireActor(db, actor, false, ct);
        return await db.ProductAttributeDefinitions.AsNoTracking()
            .Where(definition => !definition.IsDeleted && definition.ShowInItems)
            .OrderBy(definition => definition.DisplayOrder)
            .ThenBy(definition => definition.DisplayName)
            .Select(definition => new ItemAttributeColumn(definition.Key, definition.DisplayName))
            .ToListAsync(ct);
    }

    internal static IQueryable<ProductAttributeDefinitionOption> AttributeDefinitionQuery(AppDbContext db) =>
        db.ProductAttributeDefinitions.AsNoTracking()
            .Where(definition => !definition.IsDeleted)
            .OrderBy(definition => definition.DisplayOrder)
            .ThenBy(definition => definition.DisplayName)
            .Select(definition => new ProductAttributeDefinitionOption(
                definition.Id,
                definition.Key,
                definition.DisplayName,
                definition.Kind.ToString(),
                definition.AllowFraction,
                definition.NonNegative,
                definition.ShowInItems,
                definition.ShowInSales,
                definition.ShowInReceiving,
                definition.ShowInStockAdjustment,
                definition.Options.OrderBy(option => option.DisplayOrder)
                    .Select(option => new ProductAttributeChoice(option.Id, option.Value, option.DisplayName)).ToArray()));

    public async Task<int> SaveCategoryAsync(ClaimsPrincipal actor, CategoryInput input, CancellationToken ct = default)
    {
        Validator.ValidateObject(input, new ValidationContext(input), true);
        var name = input.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("Enter a category name.");
        }
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = await RequireActor(db, actor, true, ct);
        if (await db.Categories.AnyAsync(category => !category.IsDeleted
            && category.NormalizedName == NameNormalization.Normalize(name), ct))
        {
            throw new ValidationException("A category with this name already exists.");
        }
        var category = new Category
        {
            Name = name,
            NormalizedName = NameNormalization.Normalize(name),
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            CreatedByUserId = actorId
        };
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        return category.Id;
    }

    public async Task<int> SaveAttributeDefinitionAsync(
        ClaimsPrincipal actor,
        ProductAttributeDefinitionInput input,
        CancellationToken ct = default)
    {
        input.Options ??= [];
        Validator.ValidateObject(input, new ValidationContext(input), true);
        var key = input.Key.Trim().ToLowerInvariant();
        if (key.Length is < 1 or > 80 || key.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')))
        {
            throw new ValidationException("Use a lowercase attribute key with letters, numbers, hyphens or underscores.");
        }
        if (!Enum.TryParse<ProductAttributeKind>(input.Kind, true, out var kind))
        {
            throw new ValidationException("Select a valid attribute type.");
        }
        if (kind != ProductAttributeKind.Number && (input.AllowFraction || input.NonNegative))
        {
            throw new ValidationException("Number settings apply only to number attributes.");
        }
        var options = input.Options
            .Select(option => (Value: option.Value.Trim(), DisplayName: string.IsNullOrWhiteSpace(option.DisplayName)
                ? option.Value.Trim()
                : option.DisplayName.Trim()))
            .Where(option => option.Value.Length > 0)
            .ToArray();
        if (kind == ProductAttributeKind.Select && (options.Length == 0 || options.Any(option => option.Value.Length > 120 || option.DisplayName.Length > 120)))
        {
            throw new ValidationException("Add at least one select option with a value. Display names are optional.");
        }
        if (kind != ProductAttributeKind.Select && input.Options.Any(option => !string.IsNullOrWhiteSpace(option.Value)
            || !string.IsNullOrWhiteSpace(option.DisplayName)))
        {
            throw new ValidationException("Only select attributes can have options.");
        }
        if (options.Select(option => option.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count() != options.Length)
        {
            throw new ValidationException("Select options must be unique.");
        }
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = await RequireActor(db, actor, true, ct);
        if (input.Id < 0)
        {
            throw new ValidationException("Invalid attribute.");
        }

        var definition = input.Id == 0
            ? null
            : await db.ProductAttributeDefinitions
                .Include(attribute => attribute.Options)
                .SingleOrDefaultAsync(attribute => attribute.Id == input.Id && !attribute.IsDeleted, ct)
                ?? throw new ValidationException("This attribute no longer exists.");

        if (await db.ProductAttributeDefinitions.AnyAsync(attribute => !attribute.IsDeleted
            && attribute.Id != input.Id && attribute.Key.ToLower() == key, ct))
        {
            throw new ValidationException("An attribute with this key already exists.");
        }

        if (definition is not null)
        {
            var hasValues = await db.ProductAttributeValues.AnyAsync(value => value.ProductAttributeDefinitionId == definition.Id, ct);
            if (hasValues && definition.Kind != kind)
            {
                throw new ValidationException("This attribute is already used by products, so its value type cannot be changed.");
            }

            if (definition.Kind == ProductAttributeKind.Select && kind == ProductAttributeKind.Select)
            {
                var removedValues = definition.Options.Select(option => option.Value)
                    .Except(options.Select(option => option.Value), StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (removedValues.Length > 0 && await db.ProductAttributeValues.AnyAsync(value =>
                    value.ProductAttributeDefinitionId == definition.Id && removedValues.Contains(value.Value), ct))
                {
                    throw new ValidationException("One or more options are assigned to products. Change those product values before removing the options.");
                }
            }
        }
        else
        {
            definition = new ProductAttributeDefinition { CreatedByUserId = actorId, Key = key, DisplayName = input.DisplayName.Trim() };
            db.ProductAttributeDefinitions.Add(definition);
        }

        definition.Key = key;
        definition.DisplayName = input.DisplayName.Trim();
        definition.Kind = kind;
        definition.AllowFraction = input.AllowFraction;
        definition.NonNegative = input.NonNegative;
        definition.ShowInItems = input.ShowInItems;
        definition.ShowInSales = input.ShowInSales;
        definition.ShowInReceiving = input.ShowInReceiving;
        definition.ShowInStockAdjustment = input.ShowInStockAdjustment;

        var optionsByValue = options
            .Select((option, index) => (Option: option, Index: index))
            .ToDictionary(item => item.Option.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var existingOption in definition.Options.ToArray())
        {
            if (optionsByValue.Remove(existingOption.Value, out var updated))
            {
                existingOption.Value = updated.Option.Value;
                existingOption.DisplayName = updated.Option.DisplayName;
                existingOption.DisplayOrder = updated.Index;
            }
            else
            {
                db.ProductAttributeOptions.Remove(existingOption);
            }
        }

        foreach (var (option, index) in optionsByValue.Values.OrderBy(item => item.Index))
        {
            definition.Options.Add(new POSApp.Persistence.Entities.ProductAttributeOption
            {
                Value = option.Value,
                DisplayName = option.DisplayName,
                DisplayOrder = index
            });
        }

        await db.SaveChangesAsync(ct);
        return definition.Id;
    }

    public async Task<int> SaveProductAsync(ClaimsPrincipal actor, int? productId, ProductInput input, CancellationToken ct = default)
    {
        return await StoreOperation.RunAsync<int>(async () =>
        {
            input.Attributes ??= [];
            Validator.ValidateObject(input, new ValidationContext(input), true);
            if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Unit))
            {
                throw new ValidationException("Name and unit are required.");
            }

            Money(input.SellingPrice, "Selling price");
            Money(input.CostPrice, "Cost price");
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await RequireActor(db, actor, true, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var barcode = string.IsNullOrWhiteSpace(input.Barcode) ? null : input.Barcode.Trim();
            if (barcode is not null && barcode.Any(character => character < ' ' || character > '~'))
            {
                throw new ValidationException("Use printable English letters, numbers or symbols for a scannable barcode.");
            }
            if (barcode != null && await db.Products.AnyAsync(p => p.Barcode == barcode && p.Id != productId, ct))
            {
                throw new ValidationException("This barcode already belongs to an item.");
            }

            if (!await db.Suppliers.AnyAsync(supplier => supplier.Id == input.SupplierId && !supplier.IsDeleted, ct))
            {
                throw new ValidationException("Choose an active default supplier.");
            }

            if (input.CategoryId <= 0 || !await db.Categories.AnyAsync(c => c.Id == input.CategoryId && !c.IsDeleted, ct))
            {
                throw new ValidationException("Choose an active category.");
            }

            var attributeInputs = input.Attributes ?? [];
            if (attributeInputs.Select(attribute => attribute.DefinitionId).Distinct().Count() != attributeInputs.Count)
            {
                throw new ValidationException("Each product attribute can be added only once.");
            }
            var attributeIds = attributeInputs.Select(attribute => attribute.DefinitionId).ToArray();
            var definitions = await db.ProductAttributeDefinitions.Include(definition => definition.Options)
                .Where(definition => !definition.IsDeleted && attributeIds.Contains(definition.Id))
                .ToDictionaryAsync(definition => definition.Id, ct);
            if (definitions.Count != attributeIds.Length)
            {
                throw new ValidationException("One or more selected product attributes are unavailable.");
            }
            foreach (var attribute in attributeInputs)
            {
                ValidateAttributeValue(definitions[attribute.DefinitionId], attribute);
            }

            if (!input.AllowFractionUnit && productId is { } existingId)
            {
                var quantities = await db.InventoryBalances
                    .Where(balance => balance.ProductId == existingId)
                    .Select(balance => balance.QuantityOnHand)
                    .ToListAsync(ct);

                if (quantities.Any(quantity => decimal.Truncate(quantity) != quantity))
                {
                    throw new ValidationException("Fractional quantities cannot be disabled while this item has fractional stock.");
                }
            }

            var product = productId is { } id
                ? await db.Products.SingleOrDefaultAsync(candidate => candidate.Id == id && !candidate.IsDeleted, ct)
                    ?? throw new ValidationException("Item not found.")
                : new Product
                {
                    Name = input.Name.Trim(),
                    CreatedByUserId = actorId
                };

            if (product.IsNonStocked != input.IsNonStocked && productId is { } changedId
                && (await db.InventoryBalances.AnyAsync(balance => balance.ProductId == changedId && balance.QuantityOnHand != 0, ct)
                    || await db.InventoryMovements.AnyAsync(movement => movement.ProductId == changedId, ct)
                    || await db.SaleItems.AnyAsync(item => item.ProductId == changedId, ct)))
            {
                throw new ValidationException("Stock type cannot be changed after stock or sales activity. Create a separate item instead.");
            }

            product.IsNonStocked = input.IsNonStocked;
            var targetCostPrice = input.IsNonStocked ? 0 : input.CostPrice;
            product.SupplierId = input.SupplierId;
            product.Name = input.Name.Trim();
            product.NormalizedName = NameNormalization.Normalize(product.Name);
            product.Barcode = barcode;
            product.Unit = input.Unit.Trim();
            product.AllowFractionUnit = input.AllowFractionUnit;
            product.Description = input.Description?.Trim();
            product.CategoryId = input.CategoryId;
            product.SellingPrice = input.SellingPrice;
            if (productId is null)
            {
                product.CostPrice = targetCostPrice;
                db.Products.Add(product);
                db.ProductCostHistories.Add(new ProductCostHistory
                {
                    Product = product,
                    OldCostPrice = 0,
                    NewCostPrice = targetCostPrice,
                    Reason = "Initial cost price",
                    ReferenceType = "ProductCreate",
                    CreatedByUserId = actorId,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }
            else
            {
                if (product.CostPrice != targetCostPrice)
                {
                    var oldCost = product.CostPrice;
                    product.CostPrice = targetCostPrice;
                    db.ProductCostHistories.Add(new ProductCostHistory
                    {
                        ProductId = product.Id,
                        OldCostPrice = oldCost,
                        NewCostPrice = targetCostPrice,
                        Reason = "Cost price edited",
                        ReferenceType = "ProductEdit",
                        ReferenceId = product.Id,
                        CreatedByUserId = actorId,
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                }
                product.UpdatedAt = DateTimeOffset.UtcNow;
                product.UpdatedByUserId = actorId;
            }

            if (productId is { } savedProductId)
            {
                var oldValues = await db.ProductAttributeValues.Where(value => value.ProductId == savedProductId).ToArrayAsync(ct);
                db.ProductAttributeValues.RemoveRange(oldValues);
            }
            foreach (var attribute in attributeInputs)
            {
                var definition = definitions[attribute.DefinitionId];
                var (value, displayValue) = AttributeValue(definition, attribute);
                if (value is not null)
                {
                    product.AttributeValues.Add(new ProductAttributeValue
                    {
                        Product = product,
                        ProductAttributeDefinitionId = attribute.DefinitionId,
                        Value = value,
                        DisplayValue = displayValue
                    });
                }
            }

            await db.SaveChangesAsync(ct);
            if (product.Barcode is null && input.GenerateBarcodeWhenEmpty)
            {
                product.Barcode = BarcodeService.FromProductId(product.Id);
                if (await db.Products.AnyAsync(candidate => candidate.Id != product.Id && candidate.Barcode == product.Barcode, ct))
                {
                    throw new ValidationException("The generated barcode is already used. Enter a different barcode.");
                }

                await db.SaveChangesAsync(ct);
            }

            await transaction.CommitAsync(ct);
            return product.Id;
        }, ct);
    }

    private static void ValidateAttributeValue(ProductAttributeDefinition definition, ProductAttributeInput input)
    {
        if (definition.Kind == ProductAttributeKind.Number && input.NumberValue is { } number)
        {
            if ((definition.NonNegative && number < 0)
                || (!definition.AllowFraction && decimal.Truncate(number) != number))
            {
                throw new ValidationException($"Enter a valid {definition.DisplayName} value.");
            }
            return;
        }
        if (definition.Kind == ProductAttributeKind.Checkbox)
        {
            return;
        }
        var value = input.TextValue?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return;
        }
        if (value.Length > 2000)
        {
            throw new ValidationException($"{definition.DisplayName} cannot exceed 2,000 characters.");
        }
        if (definition.Kind == ProductAttributeKind.Select
            && !definition.Options.Any(option => option.Value == value))
        {
            throw new ValidationException($"Choose a valid {definition.DisplayName} option.");
        }
    }

    private static (string? Value, string? DisplayValue) AttributeValue(
        ProductAttributeDefinition definition,
        ProductAttributeInput input)
    {
        if (definition.Kind == ProductAttributeKind.Checkbox)
        {
            return (input.BooleanValue ? "true" : "false", null);
        }

        if (definition.Kind == ProductAttributeKind.Number)
        {
            return (input.NumberValue?.ToString(System.Globalization.CultureInfo.InvariantCulture), null);
        }

        var value = input.TextValue?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return (null, null);
        }

        if (definition.Kind == ProductAttributeKind.Select)
        {
            var option = definition.Options.FirstOrDefault(option => option.Value == value);
            return option is null ? (value, null) : (option.Value, option.DisplayName);
        }

        return (value, null);
    }

    public async Task<ReceivingOptions> OptionsAsync(ClaimsPrincipal actor, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = await RequireActor(db, actor, true, ct);

        var suppliers = await db.Suppliers
            .TagWith("Inventory.Options")
            .Where(supplier => !supplier.IsDeleted)
            .OrderBy(supplier => supplier.Name)
            .Select(supplier => new SupplierOption(
                supplier.Id,
                supplier.Name,
                supplier.Balance == null ? 0 : supplier.Balance.CurrentBalance))
            .ToListAsync(ct);

        var locations = await db.StockLocations
            .Where(location => !location.IsDeleted)
            .OrderBy(location => location.Name)
            .Select(location => new Lookup(location.Id, location.Name))
            .ToListAsync(ct);

        var definitions = await AttributeDefinitionQuery(db).ToListAsync(ct);
        return new(suppliers, locations)
        {
            WorkingDate = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct),
            Attributes = definitions
        };
    }

    public async Task<int> AddLocationAsync(ClaimsPrincipal actor, string name, CancellationToken ct = default, bool canSell = true)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            throw new ValidationException("Enter a location name (up to 200 characters).");
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = await RequireActor(db, actor, true, ct);
        var location = new StockLocation
        {
            CanSell = canSell,
            Code = Guid.NewGuid().ToString("N"),
            Name = name.Trim(),
            NormalizedName = NameNormalization.Normalize(name),
            CreatedByUserId = id
        };

        db.StockLocations.Add(location);
        await db.SaveChangesAsync(ct);
        return location.Id;
    }

    public async Task<int> AddSupplierAsync(ClaimsPrincipal actor, string name, string? phone, decimal openingDue, CancellationToken ct = default)
    {
        return await StoreOperation.RunAsync<int>(async () =>
        {
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
            {
                throw new ValidationException("Enter a supplier name (up to 200 characters).");
            }

            if (phone?.Length > 64)
            {
                throw new ValidationException("Phone is too long.");
            }

            Money(openingDue, "Opening balance", allowNegative: true);
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var id = await RequireActor(db, actor, true, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var date = await AccountingCalendar.SelectedAsync(db, id, configuration, ct);
            AccountingCalendar.ValidateExpected(date, WorkingDayRequest.Date);
            await AccountingCalendar.EnsureOpenAsync(db, date, ct);
            var now = AccountingCalendar.At(date, configuration);
            var supplier = new Supplier
            {
                Name = name.Trim(),
                NormalizedName = NameNormalization.Normalize(name),
                Phone = phone?.Trim(),
                CreatedByUserId = id
            };

            var balance = new SupplierBalance
            {
                SupplierId = 0,
                Supplier = supplier,
                OpeningBalance = openingDue,
                CurrentBalance = openingDue,
                OpenedAt = now,
                CreatedByUserId = id
            };

            balance.Entries.Add(new SupplierBalanceEntry
            {
                SupplierBalanceId = 0,
                SupplierBalance = balance,
                Type = BalanceEntryType.OpeningBalance,
                Amount = openingDue,
                BalanceAfter = openingDue,
                OccurredAt = now,
                CreatedByUserId = id
            });

            db.SupplierBalances.Add(balance);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return supplier.Id;
        }, ct);
    }

    public async Task<DashboardData> DashboardAsync(ClaimsPrincipal actor, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (id, costs) = await RequireActorWithPermissions(db, actor, false, ct);
        var zone = AccountingCalendar.Zone(configuration);
        DateTimeOffset Utc(DateOnly date) => new(TimeZoneInfo.ConvertTimeToUtc(
            date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone), TimeSpan.Zero);

        var businessDate = await AccountingCalendar.SelectedAsync(db, id, configuration, ct);
        var from = Utc(businessDate);
        var through = Utc(businessDate.AddDays(1));

        // Execute aggregated queries directly in PostgreSQL without loading entire catalogs into memory.
        var activeProducts = db.Products.Where(product => !product.IsDeleted);
        var productCount = await activeProducts.CountAsync(ct);
        var totalQuantity = await db.InventoryBalances
            .Where(balance => !balance.Product!.IsDeleted)
            .SumAsync(balance => (decimal?)balance.QuantityOnHand, ct) ?? 0m;
        var outOfStockCount = await activeProducts
            .Where(product => !product.IsNonStocked && product.InventoryBalances.Sum(balance => (decimal?)balance.QuantityOnHand ?? 0) <= 0)
            .CountAsync(ct);
        decimal? stockValuation = costs
            ? await db.InventoryBalances
                .Where(balance => !balance.Product!.IsDeleted)
                .SumAsync(balance => (decimal?)(balance.QuantityOnHand * balance.Product!.CostPrice), ct) ?? 0m
            : null;

        decimal? due = costs ? (await db.SupplierBalances.SumAsync(b => (decimal?)b.CurrentBalance, ct) ?? 0m) : null;
        var receipts = costs
            ? await ReceiptQuery(db.Purchases
                .Where(purchase => !purchase.IsDeleted && purchase.Status == PurchaseStatus.Received)
                .OrderByDescending(purchase => purchase.Id)
                .Take(6)).ToListAsync(ct)
            : [];

        // Today's trading / sales
        var salesQuery = db.Sales.AsNoTracking()
            .Where(s => !s.IsDeleted && s.Status == SaleStatus.Completed && s.SoldAt >= from && s.SoldAt < through);

        var todaySalesCount = await salesQuery.CountAsync(ct);
        var todaySalesAmount = await salesQuery.SumAsync(s => (decimal?)s.TotalAmount, ct) ?? 0m;

        decimal? todayProfit = null;
        if (costs)
        {
            var todayCogs = await db.SaleItems.AsNoTracking()
                .Where(item => salesQuery.Any(s => s.Id == item.SaleId))
                .SumAsync(item => (decimal?)(item.UnitCost * item.Quantity), ct) ?? 0m;
            todayProfit = todaySalesAmount - todayCogs;
        }

        var recentSales = await db.Sales.AsNoTracking()
            .Where(s => !s.IsDeleted && s.Status == SaleStatus.Completed)
            .OrderByDescending(s => s.SoldAt)
            .Take(6)
            .Select(s => new RecentSaleRow(
                s.Id,
                s.ReceiptNumber,
                s.SoldAt,
                s.Cashier != null ? (s.Cashier.UserName ?? "Cashier") : "Cashier",
                s.Customer != null ? s.Customer.Name : "Walk-in",
                s.TotalAmount,
                s.DraftPaymentMethod,
                s.Items.Count))
            .ToListAsync(ct);

        // Office cash & funds
        var officeCash = await db.CashDrawers.SumAsync(d => (decimal?)d.CurrentCash, ct) ?? 0m;
        decimal? bankBalance = null;
        if (actor.IsInRole(AppRoles.Admin))
        {
            var account = await db.BankAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.Code == "MAIN", ct);
            if (account != null)
            {
                var movements = await db.BankMovements.Where(m => m.Kind != CashMovementKind.OpeningBalance)
                    .SumAsync(m => (decimal?)m.Amount, ct) ?? 0m;
                bankBalance = account.OpeningBalance + movements;
            }
        }

        var openDrawers = await db.CashSessions.CountAsync(s => s.BusinessDate == businessDate && s.ClosedAt == null, ct);
        var totalDrawers = await db.CashDrawers.CountAsync(d => d.StockLocationId != null, ct);

        // Location of the user
        var locationName = await db.Users.AsNoTracking()
            .Where(u => u.Id == id && u.CashLocation != null)
            .Select(u => u.CashLocation!.Name)
            .FirstOrDefaultAsync(ct);

        // Critical stock alerts (items out of stock or low)
        var stockAlerts = await db.Products.AsNoTracking()
            .Where(p => !p.IsDeleted && !p.IsNonStocked
                && p.InventoryBalances.Sum(b => (decimal?)b.QuantityOnHand ?? 0) <= 5)
            .OrderBy(p => p.InventoryBalances.Sum(b => (decimal?)b.QuantityOnHand ?? 0))
            .ThenBy(p => p.Name)
            .Take(6)
            .Select(p => new StockAlertItem(
                p.Id,
                p.Name,
                p.Barcode,
                p.InventoryBalances.Sum(b => (decimal?)b.QuantityOnHand ?? 0),
                p.Unit,
                p.Supplier != null ? p.Supplier.Name : "Unassigned"))
            .ToListAsync(ct);

        return new(
            productCount,
            totalQuantity,
            outOfStockCount,
            stockValuation,
            due,
            receipts)
        {
            TodaySales = todaySalesAmount,
            TodayOrdersCount = todaySalesCount,
            TodayProfit = todayProfit,
            OfficeCash = officeCash,
            BankBalance = bankBalance,
            OpenDrawersCount = openDrawers,
            TotalDrawersCount = totalDrawers,
            BusinessDate = businessDate,
            LocationName = locationName,
            RecentSales = recentSales,
            StockAlerts = stockAlerts
        };
    }

    private static IQueryable<ReceiptRow> ReceiptQuery(IQueryable<Purchase> purchases) => purchases.AsNoTracking()
        .Select(purchase => new ReceiptRow(
            purchase.Id,
            purchase.PurchaseNumber,
            purchase.Supplier!.Name,
            purchase.ReceivedAt ?? purchase.OrderedAt,
            purchase.TotalAmount,
            purchase.Payments.Select(payment => (decimal?)payment.Amount).Sum() ?? 0));

    public async Task<IReadOnlyList<ReceiptRow>> ReceiptsAsync(ClaimsPrincipal actor, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await RequireActor(db, actor, true, ct);
        return await ReceiptQuery(db.Purchases
                .Where(purchase => !purchase.IsDeleted && purchase.Status == PurchaseStatus.Received)
                .OrderByDescending(purchase => purchase.Id)
                .Take(30))
            .ToListAsync(ct);
    }

    public async Task<ReceiveResult> ReceiveAsync(ClaimsPrincipal actor, ReceiveCommand command, CancellationToken ct = default)
    {
        return await StoreOperation.RunAsync<ReceiveResult>(async () =>
        {
            if (command.RequestId == Guid.Empty)
            {
                throw new ValidationException("A receipt reference is required.");
            }
            if (command.Lines.Count is < 1 or > 100)
            {
                throw new ValidationException("Add between 1 and 100 items.");
            }
            if (command.Lines.GroupBy(line => (line.ProductId, IsReturn: line.Quantity < 0)).Any(group => group.Count() > 1))
            {
                throw new ValidationException("Use one purchase line and one return line per item.");
            }
            if (!Enum.IsDefined(command.Method))
            {
                throw new ValidationException("Choose a payment method.");
            }
            if (command.Invoice?.Length > 64 || command.Notes?.Length > 2000)
            {
                throw new ValidationException("Invoice or notes are too long.");
            }
            foreach (var line in command.Lines)
            {
                if (line.Quantity == 0 || line.Quantity < -999999999999999.999m || line.Quantity > 999999999999999.999m || decimal.Round(line.Quantity, 3) != line.Quantity)
                {
                    throw new ValidationException("Quantity must be nonzero with at most 3 decimal places. Use negative quantities for returns.");
                }
                Money(line.UnitCost, "Unit cost");
                if (line.UnitCost > 0 && Math.Abs(line.Quantity) > 9999999999999999.99m / line.UnitCost)
                {
                    throw new ValidationException("An item total exceeds the supported limit.");
                }
            }
            var total = command.Lines.Sum(l => decimal.Round(l.Quantity * l.UnitCost, 2, MidpointRounding.AwayFromZero));
            Money(total, "Total", allowNegative: true);
            var paymentPlan = PaymentAllocation.Allocate(total, command.Payments, command.Payment, command.Method, allowChange: false);
            var paidAmount = paymentPlan.Sum(payment => payment.Amount);
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await RequireActor(db, actor, true, ct);
            // Serializable protects weighted averages, stock and supplier balances against overlapping receipts.
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var businessDate = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
            AccountingCalendar.ValidateExpected(businessDate, WorkingDayRequest.Date);
            var hash = StoreOperation.Fingerprint(new
            {
                command.RequestId, command.SupplierId, command.LocationId, command.Lines,
                command.Payment, command.Method, command.Invoice, command.Notes,
                command.Payments, command.AllowDuplicateInvoice
            });
            var number = $"REC-{command.RequestId:N}";
            var existing = await db.Purchases.Include(p => p.Payments).SingleOrDefaultAsync(p => p.PurchaseNumber == number, ct);
            if (existing?.IsDeleted == true)
            {
                throw new ValidationException("This receiving was deleted by an administrator. Start a new receipt.");
            }
            if (existing is not null)
            {
                if (existing.CreatedByUserId != actorId)
                {
                    throw new UnauthorizedAccessException();
                }
                if (existing.RequestHash is not null && existing.RequestHash != hash)
                {
                    throw new ValidationException("This receipt was already saved with different values. Start a new receipt.");
                }
                var previousBalance = await db.SupplierBalances.Where(b => b.SupplierId == existing.SupplierId).Select(b => b.CurrentBalance).SingleAsync(ct);
                return new(existing.Id, existing.PurchaseNumber, existing.TotalAmount, existing.Payments.Sum(p => p.Amount), previousBalance);
            }
            AccountingCalendar.ValidateExpected(businessDate, command.WorkingDate);
            await AccountingCalendar.EnsureOpenAsync(db, businessDate, ct);
            var effectiveAt = AccountingCalendar.At(businessDate, configuration);
            if (!string.IsNullOrWhiteSpace(command.Invoice))
            {
                var invoice = command.Invoice.Trim().ToUpperInvariant();
                var duplicate = await db.Purchases.Where(purchase => !purchase.IsDeleted
                        && purchase.SupplierId == command.SupplierId && purchase.SupplierInvoiceNumber != null
                        && purchase.SupplierInvoiceNumber.ToUpper() == invoice)
                    .Select(purchase => (int?)purchase.Id).FirstOrDefaultAsync(ct);
                if (duplicate is { } duplicateId && !command.AllowDuplicateInvoice)
                {
                    throw new ValidationException($"Invoice already recorded in receiving #{duplicateId}. Review it, or approve an additional delivery against this invoice.");
                }
                if (duplicate is not null && command.AllowDuplicateInvoice && string.IsNullOrWhiteSpace(command.Notes))
                {
                    throw new ValidationException("Add a note explaining the additional delivery against this invoice.");
                }
            }
            if (!await db.Suppliers.AnyAsync(s => s.Id == command.SupplierId && !s.IsDeleted, ct))
            {
                throw new ValidationException("Choose an active supplier.");
            }
            if (!await db.StockLocations.AnyAsync(s => s.Id == command.LocationId && !s.IsDeleted, ct))
            {
                throw new ValidationException("Choose an active stock location.");
            }
            var productIds = command.Lines.Select(l => l.ProductId).Distinct().ToArray();
            var products = await db.Products.Where(p => productIds.Contains(p.Id) && !p.IsDeleted).ToDictionaryAsync(p => p.Id, ct);
            if (products.Count != productIds.Length)
            {
                throw new ValidationException("An item is no longer available. Refresh the receipt.");
            }

            foreach (var line in command.Lines)
            {
                var product = products[line.ProductId];
                if (product.IsNonStocked)
                {
                    throw new ValidationException($"{product.Name} is non-stocked and cannot be received or returned to a supplier.");
                }
                if (!product.AllowFractionUnit && decimal.Truncate(line.Quantity) != line.Quantity)
                {
                    throw new ValidationException($"{product.Name} only accepts whole quantities.");
                }
            }

            var stocks = await db.InventoryBalances.Where(b => productIds.Contains(b.ProductId)).ToListAsync(ct);
            var now = DateTimeOffset.UtcNow;
            var purchase = new Purchase
            {
                RequestHash = hash,
                PurchaseNumber = number,
                StockLocationId = command.LocationId,
                SupplierId = command.SupplierId,
                Status = PurchaseStatus.Received,
                ReceivedAt = effectiveAt,
                OrderedAt = effectiveAt,
                Subtotal = total,
                TotalAmount = total,
                SupplierInvoiceNumber = command.Invoice?.Trim(),
                Notes = command.Notes?.Trim(),
                CreatedByUserId = actorId
            };

            foreach (var line in command.Lines.OrderBy(line => line.Quantity > 0))
            {
                var product = products[line.ProductId];
                var oldQuantity = stocks.Where(s => s.ProductId == line.ProductId).Sum(s => s.QuantityOnHand);
                var quantity = oldQuantity + line.Quantity;
                if (quantity is < -999999999999999.999m or > 999999999999999.999m)
                {
                    throw new ValidationException("Stock quantity exceeds the supported limit.");
                }
                if (line.Quantity > 0)
                {
                    var oldCost = product.CostPrice;
                    product.CostPrice = POSApp.Services.Calculations.InventoryValuationCalculator.CalculateMovingAverageCost(oldQuantity, product.CostPrice, line.Quantity, line.UnitCost);

                    if (product.CostPrice != oldCost)
                    {
                        var invoiceInfo = string.IsNullOrWhiteSpace(command.Invoice) ? "" : $" (Invoice: {command.Invoice.Trim()})";
                        db.ProductCostHistories.Add(new ProductCostHistory
                        {
                            ProductId = product.Id,
                            OldCostPrice = oldCost,
                            NewCostPrice = product.CostPrice,
                            Reason = $"Received {line.Quantity:0.###} {product.Unit} @ {line.UnitCost:N2}{invoiceInfo}",
                            ReferenceType = "Purchase",
                            ReferenceId = purchase.Id,
                            CreatedByUserId = actorId,
                            CreatedAt = now
                        });
                    }
                }

                product.UpdatedAt = now;
                product.UpdatedByUserId = actorId;
                var stock = stocks.SingleOrDefault(b => b.ProductId == product.Id && b.StockLocationId == command.LocationId);
                if (stock is null)
                {
                    stock = new InventoryBalance
                    {
                        ProductId = product.Id,
                        StockLocationId = command.LocationId,
                        QuantityOnHand = 0,
                        CreatedByUserId = actorId
                    };

                    db.InventoryBalances.Add(stock);
                    stocks.Add(stock);
                }

                if (line.Quantity < 0 && stock.QuantityOnHand + line.Quantity < 0)
                {
                    throw new ValidationException($"There is not enough {product.Name} stock at this location to return.");
                }
                stock.QuantityOnHand += line.Quantity;
                stock.UpdatedAt = now;
                stock.UpdatedByUserId = actorId;
                var item = new PurchaseItem
                {
                    PurchaseId = 0,
                    Purchase = purchase,
                    ProductId = product.Id,
                    ProductName = product.Name,
                    ProductSku = product.Barcode ?? product.Id.ToString(),
                    Unit = product.Unit,
                    Quantity = line.Quantity,
                    UnitCost = line.UnitCost,
                    TotalAmount = decimal.Round(line.Quantity * line.UnitCost, 2, MidpointRounding.AwayFromZero)
                };

                purchase.Items.Add(item);
                db.InventoryMovements.Add(new InventoryMovement
                {
                    ProductId = product.Id,
                    StockLocationId = command.LocationId,
                    PurchaseItem = item,
                    Type = line.Quantity < 0 ? InventoryMovementType.PurchaseReturn : InventoryMovementType.Purchase,
                    Quantity = line.Quantity,
                    UnitCost = line.UnitCost,
                    OccurredAt = effectiveAt,
                    Notes = command.Notes?.Trim(),
                    CreatedByUserId = actorId
                });
            }

            var balance = await db.SupplierBalances.SingleOrDefaultAsync(b => b.SupplierId == command.SupplierId, ct);
            if (balance is null)
            {
                balance = new SupplierBalance
                {
                    SupplierId = command.SupplierId,
                    OpenedAt = effectiveAt,
                    CreatedByUserId = actorId
                };

                db.SupplierBalances.Add(balance);
                balance.Entries.Add(new SupplierBalanceEntry
                {
                    SupplierBalanceId = 0,
                    SupplierBalance = balance,
                    Type = BalanceEntryType.OpeningBalance,
                    OccurredAt = effectiveAt,
                    CreatedByUserId = actorId
                });
            }

            balance.CurrentBalance += total;
            Money(balance.CurrentBalance, "Supplier balance", allowNegative: true);
            balance.Entries.Add(new SupplierBalanceEntry
            {
                SupplierBalanceId = balance.Id,
                SupplierBalance = balance,
                Type = BalanceEntryType.Invoice,
                Amount = total,
                BalanceAfter = balance.CurrentBalance,
                OccurredAt = effectiveAt,
                Purchase = purchase,
                CreatedByUserId = actorId
            });

            foreach (var allocation in paymentPlan)
            {
                var payment = new SupplierPayment
                {
                    SupplierBalanceId = balance.Id,
                    SupplierBalance = balance,
                    Purchase = purchase,
                    Amount = allocation.Amount,
                    Method = allocation.Method,
                    PaidAt = effectiveAt,
                    CreatedByUserId = actorId
                };

                await FinancialPosting.PostAsync(db, actorId, allocation.Method, -allocation.Amount,
                    CashMovementKind.SupplierPayment, $"Purchase payment · {number}", businessDate, ct, supplierPayment: payment);

                balance.CurrentBalance -= allocation.Amount;
                balance.Entries.Add(new SupplierBalanceEntry
                {
                    SupplierBalanceId = balance.Id,
                    SupplierBalance = balance,
                    Type = BalanceEntryType.Payment,
                    Amount = -allocation.Amount,
                    BalanceAfter = balance.CurrentBalance,
                    OccurredAt = effectiveAt,
                    SupplierPayment = payment,
                    CreatedByUserId = actorId
                });
            }

            balance.UpdatedAt = now;
            balance.UpdatedByUserId = actorId;
            db.Purchases.Add(purchase);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(purchase.Id, number, total, paidAmount, balance.CurrentBalance);
        }, ct);
    }
    private static void Money(decimal value, string label, bool allowNegative = false)
    {
        if ((!allowNegative && value < 0) || Math.Abs(value) > 9999999999999999.99m || decimal.Round(value, 2) != value)
            throw new ValidationException($"{label} must have at most two decimal places and be within the supported range.");
    }
}
