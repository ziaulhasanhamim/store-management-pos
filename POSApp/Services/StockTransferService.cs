using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;

namespace POSApp.Services;

public sealed class StockTransferService(IServiceScopeFactory scopes, IConfiguration configuration)
{
    public async Task<IReadOnlyList<LocationRow>> LocationsAsync(ClaimsPrincipal actor, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
        return await db.StockLocations.Where(location => !location.IsDeleted).OrderBy(location => location.Name)
            .Select(location => new LocationRow(location.Id, location.Name, location.CanSell)).ToArrayAsync(ct);
    }

    public async Task<PagedResult<LocationRow>> LocationsPageAsync(ClaimsPrincipal actor,
        int page = 1, int pageSize = 30, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
        return await db.StockLocations.Where(location => !location.IsDeleted)
            .OrderBy(location => location.Name).ThenBy(location => location.Id)
            .Select(location => new LocationRow(location.Id, location.Name, location.CanSell))
            .ToPageAsync(page, pageSize, ct);
    }

    public async Task<LocationRow?> LocationAsync(ClaimsPrincipal actor, int id, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
        return await db.StockLocations.AsNoTracking().Where(location => location.Id == id && !location.IsDeleted)
            .Select(location => new LocationRow(location.Id, location.Name, location.CanSell)).SingleOrDefaultAsync(ct);
    }

    public async Task SetSellingAsync(ClaimsPrincipal actor, int id, bool canSell, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
        var location = await db.StockLocations.SingleOrDefaultAsync(location => location.Id == id && !location.IsDeleted, ct)
            ?? throw new ValidationException("Location not found.");
        if (!canSell && await db.CashDrawers.AnyAsync(drawer => drawer.StockLocationId == id
            && (drawer.CurrentCash != 0 || drawer.CurrentSessionId != null), ct))
        {
            throw new ValidationException("Transfer this location's remaining cash and close its drawer before converting it to a warehouse.");
        }
        location.CanSell = canSell;
        location.UpdatedAt = DateTimeOffset.UtcNow;
        location.UpdatedByUserId = actorId;
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> TransferAsync(ClaimsPrincipal actor, StockTransferInput input, CancellationToken ct = default)
    {
        var result = await TransferItemsAsync(actor, new TransferCommand(input.RequestId,
            input.SourceLocationId, input.DestinationLocationId,
            [new(input.ProductId, input.Quantity)], input.Notes), ct);
        return result.Id;
    }

    public async Task<TransferResult> TransferItemsAsync(
        ClaimsPrincipal actor, TransferCommand input, CancellationToken ct = default)
    {
        return await StoreOperation.RunAsync<TransferResult>(async () =>
        {
            if (input.RequestId == Guid.Empty || input.SourceLocationId == input.DestinationLocationId
                || input.Lines is null || input.Lines.Count == 0 || input.Lines.Count > 200
                || input.Lines.Any(line => line is null || line.Quantity <= 0 || line.Quantity > 999999999999999.999m
                    || decimal.Round(line.Quantity, 3) != line.Quantity)
                || input.Lines.Select(line => line.ProductId).Distinct().Count() != input.Lines.Count)
            {
                throw new ValidationException("Choose different locations and 1–200 distinct items with positive quantities (up to three decimal places).");
            }
            if (input.Notes?.Length > 2000)
            {
                throw new ValidationException("Keep transfer notes within 2,000 characters.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var previous = await db.StockTransfers.Include(transfer => transfer.Items)
                .SingleOrDefaultAsync(transfer => transfer.RequestId == input.RequestId, ct);
            if (previous is not null)
            {
                if (previous.CreatedByUserId != actorId || previous.SourceLocationId != input.SourceLocationId
                    || previous.DestinationLocationId != input.DestinationLocationId || previous.Notes != (input.Notes?.Trim() ?? "")
                    || !previous.Items.OrderBy(item => item.ProductId).Select(item => (item.ProductId, item.Quantity))
                        .SequenceEqual(input.Lines.OrderBy(line => line.ProductId).Select(line => (line.ProductId, line.Quantity))))
                {
                    throw new ValidationException("This transfer was already saved with different values. Start a new transfer.");
                }
                return new(previous.Id, previous.Items.Count);
            }
            var date = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
            AccountingCalendar.ValidateExpected(date, WorkingDayRequest.Date);
            AccountingCalendar.ValidateExpected(date, input.WorkingDate);
            await AccountingCalendar.EnsureOpenAsync(db, date, ct);
            if (await db.StockLocations.CountAsync(location => !location.IsDeleted
                && (location.Id == input.SourceLocationId || location.Id == input.DestinationLocationId), ct) != 2)
            {
                throw new ValidationException("Choose active source and destination locations.");
            }
            var ids = input.Lines.Select(line => line.ProductId).ToArray();
            var products = await db.Products.Where(product => ids.Contains(product.Id) && !product.IsDeleted)
                .ToDictionaryAsync(product => product.Id, ct);
            if (products.Count != ids.Length)
            {
                throw new ValidationException("An item is no longer available. Refresh the transfer.");
            }
            var stocks = await db.InventoryBalances.Where(stock => ids.Contains(stock.ProductId)
                    && (stock.StockLocationId == input.SourceLocationId || stock.StockLocationId == input.DestinationLocationId))
                .ToListAsync(ct);
            var transfer = new StockTransfer
            {
                RequestId = input.RequestId,
                SourceLocationId = input.SourceLocationId,
                DestinationLocationId = input.DestinationLocationId,
                OccurredAt = AccountingCalendar.At(date, configuration),
                Notes = input.Notes?.Trim() ?? "",
                CreatedByUserId = actorId
            };
            db.StockTransfers.Add(transfer);
            foreach (var line in input.Lines)
            {
                var product = products[line.ProductId];
                if (product.IsNonStocked)
                {
                    throw new ValidationException($"{product.Name} is non-stocked and cannot be transferred.");
                }
                if (!product.AllowFractionUnit && decimal.Truncate(line.Quantity) != line.Quantity)
                {
                    throw new ValidationException($"{product.Name} only allows whole quantities.");
                }
                var source = stocks.SingleOrDefault(stock => stock.ProductId == line.ProductId
                    && stock.StockLocationId == input.SourceLocationId);
                if (source is null || source.QuantityOnHand < line.Quantity)
                {
                    throw new ValidationException($"Not enough {product.Name} stock at the source location.");
                }
                var destination = stocks.SingleOrDefault(stock => stock.ProductId == line.ProductId
                    && stock.StockLocationId == input.DestinationLocationId);
                if (destination is null)
                {
                    destination = new InventoryBalance
                    {
                        ProductId = line.ProductId,
                        StockLocationId = input.DestinationLocationId,
                        QuantityOnHand = 0,
                        CreatedByUserId = actorId
                    };
                    db.InventoryBalances.Add(destination);
                }
                if (destination.QuantityOnHand + line.Quantity > 999999999999999.999m)
                {
                    throw new ValidationException($"Destination stock for {product.Name} exceeds the supported quantity.");
                }
                source.QuantityOnHand -= line.Quantity;
                destination.QuantityOnHand += line.Quantity;
                source.UpdatedAt = DateTimeOffset.UtcNow;
                destination.UpdatedAt = source.UpdatedAt;
                source.UpdatedByUserId = actorId;
                destination.UpdatedByUserId = actorId;
                transfer.Items.Add(new StockTransferItem
                {
                    ProductId = product.Id,
                    Quantity = line.Quantity,
                    UnitCost = product.CostPrice
                });
                foreach (var outgoing in new[] { true, false })
                {
                    db.InventoryMovements.Add(new InventoryMovement
                    {
                        ProductId = product.Id,
                        StockLocationId = outgoing ? source.StockLocationId : destination.StockLocationId,
                        StockTransfer = transfer,
                        Type = outgoing ? InventoryMovementType.TransferOut : InventoryMovementType.TransferIn,
                        Quantity = outgoing ? -line.Quantity : line.Quantity,
                        UnitCost = product.CostPrice,
                        OccurredAt = transfer.OccurredAt,
                        Notes = transfer.Notes,
                        CreatedByUserId = actorId
                    });
                }
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(transfer.Id, transfer.Items.Count);
        }, ct);
    }

    public async Task<PagedResult<TransferRow>> HistoryPageAsync(ClaimsPrincipal actor, int page = 1, int pageSize = 30, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
        return await db.StockTransferItems.OrderByDescending(item => item.StockTransferId).ThenBy(item => item.Id)
            .Select(item => new TransferRow(item.StockTransferId, item.StockTransfer!.OccurredAt, item.Product!.Name,
                item.StockTransfer.SourceLocation!.Name, item.StockTransfer.DestinationLocation!.Name,
                item.Quantity, item.StockTransfer.Notes)).ToPageAsync(page, pageSize, ct);
    }
    public async Task<IReadOnlyList<TransferRow>> HistoryAsync(ClaimsPrincipal actor, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
        return await db.StockTransferItems.OrderByDescending(item => item.StockTransferId).ThenBy(item => item.Id).Take(100)
            .Select(item => new TransferRow(item.StockTransferId, item.StockTransfer!.OccurredAt, item.Product!.Name,
                item.StockTransfer.SourceLocation!.Name, item.StockTransfer.DestinationLocation!.Name,
                item.Quantity, item.StockTransfer.Notes)).ToArrayAsync(ct);
    }
}
