using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;

namespace POSApp.Services;

public sealed partial class StockTakeService(IServiceScopeFactory scopes, IConfiguration configuration)
{
    public async Task<IReadOnlyList<StockTakeRow>> HistoryAsync(ClaimsPrincipal actor, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
        var takes = await db.StockTakes.AsNoTracking().Include(take => take.StockLocation)
            .Include(take => take.Items.Where(item => item.IsIncluded || item.CountedQuantity != null)).OrderByDescending(take => take.Id).Take(100).ToArrayAsync(ct);
        return takes.Select(take => new StockTakeRow(take.Id, take.StockLocation!.Name, take.BusinessDate,
            take.Items.Count(item => item.CountedQuantity.HasValue), take.Items.Count,
            take.Items.Sum(item => Round(((item.CountedQuantity ?? item.ExpectedQuantity) - item.ExpectedQuantity) * item.UnitCost)),
            take.CompletedAt)).ToArray();
    }

    public async Task<PagedResult<StockTakeRow>> HistoryPageAsync(ClaimsPrincipal actor,
        int page = 1, int pageSize = 30, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
        var result = await db.StockTakes.AsNoTracking().Include(take => take.StockLocation)
            .Include(take => take.Items.Where(item => item.IsIncluded || item.CountedQuantity != null))
            .OrderByDescending(take => take.Id).ToPageAsync(page, pageSize, ct);
        return new(result.Items.Select(take => new StockTakeRow(take.Id, take.StockLocation!.Name, take.BusinessDate,
            take.Items.Count(item => item.CountedQuantity.HasValue), take.Items.Count,
            take.Items.Sum(item => Round(((item.CountedQuantity ?? item.ExpectedQuantity) - item.ExpectedQuantity) * item.UnitCost)),
            take.CompletedAt)).ToArray(), result.Pagination);
    }

    public async Task<int> StartAsync(ClaimsPrincipal actor, StockTakeStartInput input, CancellationToken ct = default)
    {
        return await StoreOperation.RunAsync<int>(async () =>
        {
            if (input.RequestId == Guid.Empty)
            {
                throw new ValidationException("A stock take reference is required.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var previous = await db.StockTakes.SingleOrDefaultAsync(take => take.RequestId == input.RequestId, ct);
            if (previous is not null)
            {
                if (previous.StockLocationId != input.LocationId)
                {
                    throw new ValidationException("This reference was used for another location.");
                }
                return previous.Id;
            }
            var date = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
            AccountingCalendar.ValidateExpected(date, WorkingDayRequest.Date);
            await AccountingCalendar.EnsureOpenAsync(db, date, ct);
            if (!await db.StockLocations.AnyAsync(location => location.Id == input.LocationId && !location.IsDeleted, ct))
            {
                throw new ValidationException("Choose an active stock location.");
            }
            var take = new StockTake
            {
                RequestId = input.RequestId,
                StockLocationId = input.LocationId,
                BusinessDate = date,
                CreatedByUserId = actorId
            };
            db.StockTakes.Add(take);

            var productIds = await db.Products.AsNoTracking()
                .Where(p => !p.IsNonStocked && (!p.IsDeleted || p.InventoryBalances.Any(b => b.StockLocationId == input.LocationId && b.QuantityOnHand != 0)))
                .Select(p => p.Id)
                .ToArrayAsync(ct);

            var snapshots = await SnapshotsAsync(db, input.LocationId, productIds, ct);
            foreach (var stock in snapshots.OrderBy(s => s.Name))
            {
                take.Items.Add(new StockTakeItem
                {
                    ProductId = stock.Id,
                    ProductName = stock.Name,
                    Barcode = stock.Barcode,
                    Unit = stock.Unit,
                    AllowFractionUnit = stock.AllowFraction,
                    ExpectedQuantity = stock.Quantity,
                    UnitCost = stock.Cost,
                    LastMovementId = stock.MovementId,
                    CountedQuantity = null,
                    IsIncluded = true
                });
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return take.Id;
        }, ct);
    }

    public async Task<StockTakePage> ReadAsync(ClaimsPrincipal actor, int id, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
        return await PageAsync(db, await LoadAsync(db, id, ct), ct);
    }

    public async Task<IReadOnlyList<StockTakeLine>> SearchAsync(ClaimsPrincipal actor, int id, string? search,
        CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
        var locationId = await db.StockTakes.Where(take => take.Id == id)
            .Select(take => (int?)take.StockLocationId).SingleOrDefaultAsync(ct)
            ?? throw new ValidationException("Stock take not found.");
        var term = search?.Trim() ?? "";
        if (term.Length == 0)
        {
            return [];
        }
        if (term.Length > 200)
        {
            throw new ValidationException("Keep product searches within 200 characters.");
        }
        int.TryParse(term.TrimStart('#'), out var productId);
        var normalized = NameNormalization.Normalize(term);
        var ids = await db.Products.AsNoTracking()
            .Where(product => !product.IsNonStocked && (!product.IsDeleted
                || product.InventoryBalances.Any(balance => balance.StockLocationId == locationId && balance.QuantityOnHand != 0)))
            .Where(product => product.Id == productId || product.NormalizedName.Contains(normalized)
                || product.Barcode != null && product.Barcode.ToUpper().Contains(normalized))
            .OrderByDescending(product => product.Barcode != null && product.Barcode.ToUpper() == normalized)
            .ThenByDescending(product => product.Id == productId)
            .ThenBy(product => product.Name)
            .ThenBy(product => product.Id)
            .Select(product => product.Id).Take(12).ToArrayAsync(ct);
        var matches = (await SnapshotsAsync(db, locationId, ids, ct)).ToDictionary(stock => stock.Id);
        return ids.Select(product => matches[product]).Select(stock => new StockTakeLine(
            stock.Id, stock.Name, stock.Barcode, stock.Unit, stock.AllowFraction, stock.Quantity, null, stock.Cost)
        {
            LastMovementId = stock.MovementId
        }).ToArray();
    }

    public async Task<StockTakePage> SaveAsync(ClaimsPrincipal actor, int id, StockTakeSave input, CancellationToken ct = default)
    {
        return await StoreOperation.RunAsync<StockTakePage>(async () =>
        {
            if (input.Reason is null || input.Reason.Length > 2000 || input.Counts is null
                || input.Counts.Any(count => count is null)
                || input.Counts.Select(count => count.ProductId).Distinct().Count() != input.Counts.Count)
            {
                throw new ValidationException("Enter valid counts and keep the reason within 2,000 characters.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var take = await LoadAsync(db, id, ct);
            await EnsureEditableAsync(db, take, actorId, ct);
            var counts = input.Counts.ToDictionary(count => count.ProductId, count => count.Quantity);
            var existingIds = take.Items.Select(item => item.ProductId).ToArray();
            var returningItems = await db.StockTakeItems.Where(item => item.StockTakeId == id
                && counts.Keys.ToArray().Contains(item.ProductId) && !existingIds.Contains(item.ProductId)).ToArrayAsync(ct);
            foreach (var item in returningItems)
            {
                if (!take.Items.Contains(item))
                {
                    take.Items.Add(item);
                }
            }
            var newIds = counts.Keys.Except(take.Items.Select(item => item.ProductId)).ToArray();
            var snapshotIds = newIds.Concat(returningItems.Select(item => item.ProductId)).ToArray();
            var snapshots = await SnapshotsAsync(db, take.StockLocationId, snapshotIds, ct);
            if (snapshots.Length != snapshotIds.Length)
            {
                throw new ValidationException("Choose stocked products available for this location.");
            }
            foreach (var stock in snapshots)
            {
                var preview = input.Counts.Single(count => count.ProductId == stock.Id).Snapshot;
                if (preview is not null && (preview.ProductId != stock.Id || preview.Expected != stock.Quantity
                    || preview.LastMovementId != stock.MovementId || preview.UnitCost != stock.Cost
                    || preview.AllowFractionUnit != stock.AllowFraction))
                {
                    throw new ValidationException($"Stock changed for {stock.Name}. Remove it from the counted list, search for it again and recount.");
                }
                var item = take.Items.SingleOrDefault(item => item.ProductId == stock.Id);
                if (item is null)
                {
                    item = new StockTakeItem
                    {
                        ProductId = stock.Id,
                        ProductName = stock.Name,
                        Unit = stock.Unit
                    };
                    take.Items.Add(item);
                }
                item.ProductName = stock.Name;
                item.Barcode = stock.Barcode;
                item.Unit = stock.Unit;
                item.AllowFractionUnit = stock.AllowFraction;
                item.ExpectedQuantity = stock.Quantity;
                item.UnitCost = stock.Cost;
                item.LastMovementId = stock.MovementId;
            }
            foreach (var item in take.Items.Where(item => counts.ContainsKey(item.ProductId)))
            {
                var count = counts[item.ProductId];
                if (count is { } quantity && !ValidCount(item, quantity))
                {
                    throw new ValidationException($"Enter a valid nonnegative count for {item.ProductName}. Fractional counts must be allowed by the product.");
                }
            }
            var reason = input.Reason.Trim();
            var same = newIds.Length == 0 && take.Reason == reason && take.Items.All(item =>
                item.IsIncluded == counts.ContainsKey(item.ProductId)
                && item.CountedQuantity == (counts.TryGetValue(item.ProductId, out var count) ? count : null));
            if (take.Revision != input.Revision && !same)
            {
                throw new ValidationException("Another user changed this stock take. Reload it before saving.");
            }
            if (!same)
            {
                foreach (var item in take.Items)
                {
                    item.CountedQuantity = counts.TryGetValue(item.ProductId, out var count) ? count : null;
                    item.IsIncluded = counts.ContainsKey(item.ProductId);
                }
                take.Reason = reason;
                Touch(take, actorId);
                await db.SaveChangesAsync(ct);
            }
            await transaction.CommitAsync(ct);
            return await PageAsync(db, take, ct);
        }, ct);
    }

    public async Task<StockTakePage> RefreshAsync(ClaimsPrincipal actor, int id, StockTakeRefresh input, CancellationToken ct = default)
    {
        return await StoreOperation.RunAsync<StockTakePage>(async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var take = await LoadAsync(db, id, ct);
            await EnsureEditableAsync(db, take, actorId, ct);
            CheckRevision(take, input.Revision);
            var snapshots = (await SnapshotsAsync(db, take.StockLocationId, take.Items.Select(item => item.ProductId).ToArray(), ct)).ToDictionary(stock => stock.Id);
            foreach (var item in take.Items)
            {
                if (!snapshots.TryGetValue(item.ProductId, out var stock))
                {
                    item.CountedQuantity = null;
                    continue;
                }
                item.ExpectedQuantity = stock.Quantity;
                item.LastMovementId = stock.MovementId;
                item.UnitCost = stock.Cost;
                item.AllowFractionUnit = stock.AllowFraction;
                if (item.CountedQuantity is { } count && !ValidCount(item, count))
                {
                    item.CountedQuantity = null;
                }
            }
            Touch(take, actorId);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return await PageAsync(db, take, ct);
        }, ct);
    }

    public async Task<StockTakePage> ApplyAsync(ClaimsPrincipal actor, int id, StockTakeApply input, CancellationToken ct = default)
    {
        return await StoreOperation.RunAsync<StockTakePage>(async () =>
        {
            if (!input.Confirmed || input.RequestId == Guid.Empty)
            {
                throw new ValidationException("Review and confirm the stock adjustments first.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var take = await LoadAsync(db, id, ct);
            if (take.CompletedAt is not null && take.CompletionRequestId == input.RequestId)
            {
                return await PageAsync(db, take, ct);
            }
            await EnsureEditableAsync(db, take, actorId, ct);
            CheckRevision(take, input.Revision);
            var counted = take.Items.Where(item => item.CountedQuantity.HasValue).ToArray();
            if (counted.Length == 0 || string.IsNullOrWhiteSpace(take.Reason))
            {
                throw new ValidationException("Count at least one item and give a reason for this stock take.");
            }
            var snapshots = (await SnapshotsAsync(db, take.StockLocationId, take.Items.Select(item => item.ProductId).ToArray(), ct)).ToDictionary(stock => stock.Id);
            foreach (var item in counted)
            {
                if (!snapshots.TryGetValue(item.ProductId, out var stock)
                    || !ValidCount(item, item.CountedQuantity!.Value))
                {
                    throw new ValidationException($"Invalid count for {item.ProductName}. Review the count before confirming.");
                }
            }

            var activeDate = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
            await AccountingCalendar.EnsureOpenAsync(db, activeDate, ct);
            take.BusinessDate = activeDate;
            var occurredAt = AccountingCalendar.At(activeDate, configuration);

            var countedIds = counted.Select(item => item.ProductId).ToArray();
            var balances = await db.InventoryBalances.Where(balance => balance.StockLocationId == take.StockLocationId
                    && countedIds.Contains(balance.ProductId))
                .ToDictionaryAsync(balance => balance.ProductId, ct);
            var now = DateTimeOffset.UtcNow;
            foreach (var item in counted.Where(item => item.CountedQuantity != item.ExpectedQuantity))
            {
                var difference = item.CountedQuantity!.Value - item.ExpectedQuantity;
                if (!balances.TryGetValue(item.ProductId, out var balance))
                {
                    balance = new InventoryBalance
                    {
                        ProductId = item.ProductId,
                        StockLocationId = take.StockLocationId,
                        CreatedByUserId = actorId,
                        QuantityOnHand = 0
                    };
                    db.InventoryBalances.Add(balance);
                }
                balance.QuantityOnHand += difference;
                balance.UpdatedAt = now;
                balance.UpdatedByUserId = actorId;
                db.InventoryMovements.Add(new InventoryMovement
                {
                    StockTakeItem = item,
                    ProductId = item.ProductId,
                    StockLocationId = take.StockLocationId,
                    Type = InventoryMovementType.Adjustment,
                    Quantity = difference,
                    UnitCost = item.UnitCost > 0 ? item.UnitCost : (snapshots.TryGetValue(item.ProductId, out var s) ? s.Cost : 0),
                    OccurredAt = occurredAt,
                    Notes = $"Stock take #{take.Id} · {(difference < 0 ? "Shortage" : "Extra stock")} · {take.Reason}",
                    CreatedByUserId = actorId
                });
            }
            take.CompletedAt = now;
            take.CompletedByUserId = actorId;
            take.CompletionRequestId = input.RequestId;
            Touch(take, actorId);
            db.RecordChanges.Add(new RecordChange
            {
                RecordType = "stocktake",
                RecordId = take.Id,
                Action = "Complete",
                Reason = take.Reason,
                Before = JsonSerializer.Serialize(counted.Select(item => new { item.ProductId, item.ExpectedQuantity, item.UnitCost })),
                After = JsonSerializer.Serialize(counted.Select(item => new { item.ProductId, item.CountedQuantity, item.UnitCost })),
                CreatedByUserId = actorId
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return await PageAsync(db, take, ct);
        }, ct);
    }

    private static Task EnsureEditableAsync(AppDbContext db, StockTake take, int actorId, CancellationToken ct)
    {
        if (take.CompletedAt is not null)
        {
            throw new ValidationException("This stock take is completed. Start a new stock take to record another count.");
        }
        if (take.StockLocation!.IsDeleted)
        {
            throw new ValidationException("This stock location is archived.");
        }
        return Task.CompletedTask;
    }

    private static void CheckRevision(StockTake take, long revision)
    {
        if (take.Revision != revision)
        {
            throw new ValidationException("This stock take changed. Reload it before continuing.");
        }
    }

    private static void Touch(StockTake take, int actorId)
    {
        take.Revision++;
        take.UpdatedAt = DateTimeOffset.UtcNow;
        take.UpdatedByUserId = actorId;
    }

    private static async Task<StockTake> LoadAsync(AppDbContext db, int id, CancellationToken ct)
    {
        var take = await db.StockTakes.Include(take => take.StockLocation)
            .Include(take => take.Items.Where(item => item.IsIncluded || item.CountedQuantity != null))
            .SingleOrDefaultAsync(take => take.Id == id, ct)
            ?? throw new ValidationException("Stock take not found.");
        // Existing counts predate explicit inclusion; retain them when a refresh clears their quantity.
        foreach (var item in take.Items)
        {
            item.IsIncluded = true;
        }
        return take;
    }

    private async Task<StockTakePage> PageAsync(AppDbContext db, StockTake take, CancellationToken ct) =>
        new(take.Id, take.StockLocationId, take.StockLocation!.Name, take.BusinessDate, take.Revision, take.Reason,
            take.CompletedAt, await db.Users.Where(user => user.Id == take.CompletedByUserId).Select(user => user.UserName).SingleOrDefaultAsync(ct),
            take.Items.Where(item => item.IsIncluded || item.CountedQuantity.HasValue).OrderBy(item => item.ProductName).ThenBy(item => item.ProductId).Select(item => new StockTakeLine(
                item.ProductId, item.ProductName, item.Barcode, item.Unit, item.AllowFractionUnit,
                item.ExpectedQuantity, item.CountedQuantity, item.UnitCost)
                {
                    LastMovementId = item.LastMovementId
                }).ToArray())
        {
            CanEdit = take.CompletedAt is null && !take.StockLocation.IsDeleted
        };

    private static Task<StockSnapshot[]> SnapshotsAsync(AppDbContext db, int locationId, int[] productIds, CancellationToken ct) =>
        db.Products.AsNoTracking().Where(product => productIds.Contains(product.Id) && !product.IsNonStocked
                && (!product.IsDeleted || product.InventoryBalances.Any(balance => balance.StockLocationId == locationId && balance.QuantityOnHand != 0)))
            .Select(product => new StockSnapshot(product.Id, product.Name, product.Barcode, product.Unit, product.AllowFractionUnit,
                product.InventoryBalances.Where(balance => balance.StockLocationId == locationId)
                    .Select(balance => (decimal?)balance.QuantityOnHand).Sum() ?? 0,
                product.CostPrice, db.InventoryMovements.Where(move => move.StockLocationId == locationId && move.ProductId == product.Id)
                    .Select(move => (int?)move.Id).Max() ?? 0)).ToArrayAsync(ct);

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static bool ValidCount(StockTakeItem item, decimal count) =>
        count >= 0 && count <= 999999999999999.999m && decimal.Round(count, 3) == count
        && (item.AllowFractionUnit || decimal.Truncate(count) == count)
        && (item.UnitCost == 0 || Math.Abs(count - item.ExpectedQuantity) <= 9999999999999999.99m / item.UnitCost);

    private sealed record StockSnapshot(int Id, string Name, string? Barcode, string Unit, bool AllowFraction,
        decimal Quantity, decimal Cost, int MovementId);
}
