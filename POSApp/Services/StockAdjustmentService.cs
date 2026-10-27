using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;

namespace POSApp.Services;

public sealed partial class StockAdjustmentService(IServiceScopeFactory scopes, IConfiguration configuration)
{
    private const decimal MaximumMoney = 9999999999999999.99m;
    private const decimal MaximumQuantity = 999999999999999.999m;

    public async Task<StockAdjustmentResult> CreateAsync(
        ClaimsPrincipal actor, StockAdjustmentCommand input, CancellationToken ct = default)
    {
        return await StoreOperation.RunAsync<StockAdjustmentResult>(async () =>
        {
            ValidateEntries(input.Items);
            if (input.RequestId == Guid.Empty)
            {
                throw new ValidationException("Reload the page before adding stock.");
            }
            if (input.Date < new DateOnly(1900, 1, 1) || input.Date > AccountingCalendar.Today(configuration))
            {
                throw new ValidationException("Choose an adjustment date between 1900 and today.");
            }
            if (input.Notes is null || input.Notes.Length > 2000)
            {
                throw new ValidationException("Keep notes within 2,000 characters.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var previous = await db.StockAdjustments.Include(adjustment => adjustment.Items)
                .SingleOrDefaultAsync(adjustment => adjustment.RequestId == input.RequestId, ct);
            if (previous is not null)
            {
                var savedItems = previous.Items.ToDictionary(item => item.ProductId);
                if (previous.CreatedByUserId != actorId || previous.StockLocationId != input.LocationId
                    || previous.Date != input.Date || previous.Notes != input.Notes.Trim()
                    || previous.Items.Count != input.Items.Count
                    || input.Items.Any(item => (!savedItems.TryGetValue(item.ProductId, out var saved)
                        || saved.Quantity != item.Quantity || saved.UnitCost != item.UnitCost)))
                {
                    throw new ValidationException("This request was already posted with different details. Start a new adjustment.");
                }
                return Result(previous);
            }
            await ValidateLocationAsync(db, input.LocationId, ct);
            await AccountingCalendar.EnsureOpenAsync(db, input.Date, ct);
            var month = new DateOnly(input.Date.Year, input.Date.Month, 1);
            if (await db.FinanceClosings.AnyAsync(closing => closing.ClosedAt != null
                && ((closing.Kind == ClosingKind.Day && closing.Date >= input.Date)
                    || (closing.Kind == ClosingKind.Month && closing.Date >= month)), ct))
            {
                throw new ValidationException("Stock adjustments would change a closed period. Reopen the affected closings first.");
            }
            var products = await ValidateProductsAsync(db, input.LocationId, input.Items, ct);
            var ids = products.Keys.ToArray();
            var stocks = await db.InventoryBalances.Where(stock => ids.Contains(stock.ProductId)).ToListAsync(ct);
            var totalQuantities = stocks.GroupBy(stock => stock.ProductId)
                .ToDictionary(group => group.Key, group => group.Sum(stock => stock.QuantityOnHand));
            var locationStocks = stocks.Where(stock => stock.StockLocationId == input.LocationId)
                .ToDictionary(stock => stock.ProductId);
            var occurredAt = AccountingCalendar.At(input.Date, configuration);
            var adjustment = new StockAdjustment
            {
                RequestId = input.RequestId,
                StockLocationId = input.LocationId,
                Date = input.Date,
                Notes = input.Notes.Trim(),
                CreatedByUserId = actorId
            };
            db.StockAdjustments.Add(adjustment);
            foreach (var entry in input.Items)
            {
                var product = products[entry.ProductId];
                var quantity = totalQuantities.GetValueOrDefault(product.Id);
                var resultingQuantity = quantity + entry.Quantity;
                var locationBalance = locationStocks.GetValueOrDefault(product.Id);
                var resultingLocationQuantity = (locationBalance?.QuantityOnHand ?? 0) + entry.Quantity;
                if (resultingQuantity < -MaximumQuantity || resultingQuantity > MaximumQuantity)
                {
                    throw new ValidationException($"Stock quantity for {product.Name} exceeds the supported range.");
                }
                if (resultingLocationQuantity < -MaximumQuantity || resultingLocationQuantity > MaximumQuantity)
                {
                    throw new ValidationException($"Stock quantity for {product.Name} at this location exceeds the supported range.");
                }
                if (entry.Quantity > 0)
                {
                    var oldCost = product.CostPrice;
                    product.CostPrice = quantity <= 0
                        ? entry.UnitCost
                        : decimal.Round(product.CostPrice + (entry.UnitCost - product.CostPrice)
                            * (entry.Quantity / resultingQuantity), 2, MidpointRounding.AwayFromZero);

                    if (product.CostPrice != oldCost)
                    {
                        db.ProductCostHistories.Add(new ProductCostHistory
                        {
                            ProductId = product.Id,
                            OldCostPrice = oldCost,
                            NewCostPrice = product.CostPrice,
                            Reason = $"Stock adjustment #{adjustment.Id}",
                            ReferenceType = "StockAdjustment",
                            ReferenceId = adjustment.Id,
                            CreatedByUserId = actorId,
                            CreatedAt = DateTimeOffset.UtcNow
                        });
                    }
                }
                if (product.CostPrice != 0 && Math.Abs(resultingQuantity) > MaximumMoney / product.CostPrice)
                {
                    throw new ValidationException($"Inventory value for {product.Name} exceeds the supported range.");
                }
                product.UpdatedAt = DateTimeOffset.UtcNow;
                product.UpdatedByUserId = actorId;
                var balance = locationBalance;
                if (balance is null)
                {
                    balance = new InventoryBalance
                    {
                        ProductId = product.Id,
                        StockLocationId = input.LocationId,
                        QuantityOnHand = 0,
                        CreatedByUserId = actorId
                    };
                    db.InventoryBalances.Add(balance);
                }
                balance.QuantityOnHand = resultingLocationQuantity;
                balance.UpdatedAt = product.UpdatedAt;
                balance.UpdatedByUserId = actorId;
                var item = new StockAdjustmentItem
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    Barcode = product.Barcode,
                    Unit = product.Unit,
                    Quantity = entry.Quantity,
                    UnitCost = entry.UnitCost
                };
                adjustment.Items.Add(item);
                db.InventoryMovements.Add(new InventoryMovement
                {
                    ProductId = product.Id,
                    StockLocationId = input.LocationId,
                    StockAdjustmentItem = item,
                    Type = InventoryMovementType.Adjustment,
                    Quantity = entry.Quantity,
                    UnitCost = entry.UnitCost,
                    OccurredAt = occurredAt,
                    Notes = "Stock adjustment" + (adjustment.Notes.Length > 0 ? $" · {adjustment.Notes}" : ""),
                    CreatedByUserId = actorId
                });
            }
            await db.SaveChangesAsync(ct);
            db.RecordChanges.Add(new RecordChange
            {
                RequestId = input.RequestId,
                RecordType = "stockAdjustment",
                RecordId = adjustment.Id,
                Action = "Create",
                Reason = adjustment.Notes.Length > 0 ? adjustment.Notes : "Stock added without a purchase",
                Before = "{}",
                After = JsonSerializer.Serialize(new { input.LocationId, input.Date, input.Items }),
                CreatedByUserId = actorId
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Result(adjustment);
        }, ct);
    }

    private static StockAdjustmentResult Result(StockAdjustment adjustment) =>
        new(adjustment.Id, adjustment.Items.Count, adjustment.Items.Sum(item => item.Quantity * item.UnitCost));
}
