using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;
using POSApp.Persistence.Entities;
using POSApp.Services.Calculations;

namespace POSApp.Services;

public sealed class RecordAdministration(IServiceScopeFactory scopes, IConfiguration? configuration = null)
{
    private static string Revision(AuditableEntity entity) => (entity.UpdatedAt ?? entity.CreatedAt).ToString("O", CultureInfo.InvariantCulture);

    public async Task<EditableRecord?> FindAsync(ClaimsPrincipal actor, string kind, int id, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
        decimal? total = null;
        decimal? paid = null;
        IReadOnlyList<RecordStockEffect> stockEffects = [];
        string name;
        DateTimeOffset at;
        int? party;
        SoftDeletableEntity? entity;
        IReadOnlyList<Lookup> parties = [];
        switch (kind)
        {
            case "sale":
                var sale = await db.Sales.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, ct);
                if (sale is not null)
                {
                    total = sale.TotalAmount;
                    paid = await db.Payments.Where(payment => payment.SaleId == id).Select(payment => (decimal?)payment.Amount).SumAsync(ct) ?? 0;
                    stockEffects = await db.SaleItems.Where(item => item.SaleId == id && !item.Product!.IsNonStocked)
                        .Select(item => new RecordStockEffect(item.ProductName, item.Quantity, item.Unit)).ToArrayAsync(ct);
                }
                entity = sale;
                name = $"Sale #{id}";
                at = sale?.SoldAt ?? default;
                party = sale?.CustomerId;
                parties = await db.Customers.Where(item => !item.IsDeleted || item.Id == party)
                    .OrderBy(item => item.Name).Select(item => new Lookup(item.Id, item.Name)).ToArrayAsync(ct);
                break;
            case "purchase":
                var purchase = await db.Purchases.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, ct);
                if (purchase is not null)
                {
                    total = purchase.TotalAmount;
                    paid = await db.SupplierPayments.Where(payment => payment.PurchaseId == id).Select(payment => (decimal?)payment.Amount).SumAsync(ct) ?? 0;
                    stockEffects = await db.PurchaseItems.Where(item => item.PurchaseId == id)
                        .Select(item => new RecordStockEffect(item.ProductName, -item.Quantity, item.Unit)).ToArrayAsync(ct);
                }
                entity = purchase;
                name = $"Receiving #{id}";
                at = purchase?.ReceivedAt ?? purchase?.OrderedAt ?? default;
                party = purchase?.SupplierId;
                parties = await db.Suppliers.Where(item => !item.IsDeleted || item.Id == party)
                    .OrderBy(item => item.Name).Select(item => new Lookup(item.Id, item.Name)).ToArrayAsync(ct);
                break;
            case "product":
                var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, ct);
                entity = product;
                name = product?.Name ?? "Product";
                at = product?.CreatedAt ?? default;
                party = null;
                break;
            default:
                return null;
        }
        if (entity is null)
        {
            return null;
        }
        var changes = await db.RecordChanges.AsNoTracking().Where(change => change.RecordType == kind && change.RecordId == id)
            .OrderByDescending(change => change.Id).Take(20)
            .Select(change => new { change.CreatedAt, change.Action, change.Reason, change.CreatedByUserId }).ToListAsync(ct);
        var actors = await db.Users.Where(user => changes.Select(change => change.CreatedByUserId).Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.UserName ?? "User", ct);
        return new(kind, id, name, at, party, entity.IsDeleted, Revision(entity), parties,
            changes.Select(change => new RecordChangeRow(change.CreatedAt, change.Action, change.Reason,
                actors.GetValueOrDefault(change.CreatedByUserId ?? 0, "User"))).ToArray())
        {
            Total = total,
            Paid = paid,
            StockEffects = stockEffects
        };
    }

    public async Task ChangeAsync(ClaimsPrincipal actor, string kind, int id, RecordCorrectionInput input,
        bool delete, CancellationToken ct = default)
    {
        await StoreOperation.RunAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 2000)
            {
                throw new ValidationException("Enter a reason of up to 2,000 characters.");
            }
            if (!delete && kind == "product")
            {
                throw new ValidationException("Products cannot be edited here.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            var activeDate = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var now = DateTimeOffset.UtcNow;
            SoftDeletableEntity entity;
            object before;
            object after;
            int[] affectedProducts = [];
            string? costReason = null;
            string? costReferenceType = null;
            int? costReferenceId = null;
            var action = delete ? "Void" : "Edit";
            switch (kind)
            {
                case "sale":
                    var sale = await db.Sales.Include(item => item.Items).Include(item => item.Payments)
                        .SingleOrDefaultAsync(item => item.Id == id, ct) ?? throw new ValidationException("Sale not found.");
                    entity = sale;
                    if (delete && (sale.IsDeleted || sale.Status == SaleStatus.Voided))
                    {
                        return;
                    }
                    ValidateRevision(entity, input);
                    var saleDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(sale.SoldAt, AccountingCalendar.Zone(configuration)).DateTime);
                    if (!delete)
                    {
                        await AccountingCalendar.EnsureOpenAsync(db, saleDate, ct);
                    }
                    else
                    {
                        await AccountingCalendar.EnsureOpenAsync(db, activeDate, ct);
                    }
                    before = new { sale.CustomerId, sale.SoldAt, sale.Status, sale.IsDeleted };
                    var targetCustomer = delete ? null : input.PartyId;
                    if (targetCustomer is { } customerId && !await db.Customers.AnyAsync(customer => customer.Id == customerId && !customer.IsDeleted, ct))
                    {
                        throw new ValidationException("Choose an active customer.");
                    }
                    var paid = sale.Payments.Sum(payment => payment.Amount);
                    if (!delete && targetCustomer is null && sale.Status == SaleStatus.Completed && paid != sale.TotalAmount)
                    {
                        throw new ValidationException("An unpaid sale or customer credit must belong to a customer.");
                    }
                    var note = $"Sale {(delete ? "void" : "customer change")} #{id}: {input.Reason.Trim()}";
                    if (sale.Status == SaleStatus.Completed && (delete || sale.CustomerId != targetCustomer))
                    {
                        await CustomerAdjustmentAsync(db, sale.CustomerId, -(sale.TotalAmount - paid), note, actorId, now, ct);
                        if (!delete)
                        {
                            var targetBalance = await CustomerAdjustmentAsync(db, targetCustomer, sale.TotalAmount - paid, note, actorId, now, ct);
                            foreach (var payment in sale.Payments)
                            {
                                payment.CustomerBalance = targetBalance;
                                payment.CustomerBalanceId = targetBalance?.Id;
                            }
                        }
                    }
                    if (delete && sale.Status == SaleStatus.Completed)
                    {
                        sale.Status = SaleStatus.Voided;
                        await CashDrawerService.ReverseAsync(db, "sale", id, actorId, note, activeDate, sale.StockLocationId, ct);
                        await FinancialPosting.ReverseBankAsync(db, "sale", id, actorId, note, activeDate, ct);
                        affectedProducts = sale.Items.Select(item => item.ProductId).Distinct().ToArray();
                        costReason = $"Reversal of sale #{id}";
                        costReferenceType = "SaleReversal";
                        costReferenceId = id;
                        var nonStockedIds = await db.Products.Where(product => affectedProducts.Contains(product.Id) && product.IsNonStocked)
                            .Select(product => product.Id).ToArrayAsync(ct);
                        foreach (var item in sale.Items.Where(item => !nonStockedIds.Contains(item.ProductId)).OrderBy(item => item.Quantity < 0))
                        {
                            await ReverseStockAsync(db, item.ProductId, sale.StockLocationId, item.Quantity,
                                item.UnitCost, note, actorId, now, ct);
                        }
                    }
                    if (!delete)
                    {
                        sale.CustomerId = targetCustomer;
                    }
                    after = new { CustomerId = sale.CustomerId, SoldAt = sale.SoldAt, Status = sale.Status, IsDeleted = delete };
                    break;
                case "purchase":
                    var purchase = await db.Purchases.Include(item => item.Items).Include(item => item.Payments)
                        .SingleOrDefaultAsync(item => item.Id == id, ct) ?? throw new ValidationException("Receiving not found.");
                    entity = purchase;
                    if (delete && (purchase.IsDeleted || purchase.Status == PurchaseStatus.Cancelled))
                    {
                        return;
                    }
                    ValidateRevision(entity, input);
                    var purchaseDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(purchase.ReceivedAt ?? purchase.OrderedAt, AccountingCalendar.Zone(configuration)).DateTime);
                    if (!delete)
                    {
                        await AccountingCalendar.EnsureOpenAsync(db, purchaseDate, ct);
                    }
                    else
                    {
                        await AccountingCalendar.EnsureOpenAsync(db, activeDate, ct);
                    }
                    before = new { purchase.SupplierId, purchase.ReceivedAt, purchase.OrderedAt, purchase.Status, purchase.IsDeleted };
                    var targetSupplier = delete ? purchase.SupplierId : input.PartyId ?? 0;
                    if (!delete && !await db.Suppliers.AnyAsync(supplier => supplier.Id == targetSupplier && !supplier.IsDeleted, ct))
                    {
                        throw new ValidationException("Choose an active supplier.");
                    }
                    var supplierNote = $"Purchase {(delete ? "void" : "supplier change")} #{id}: {input.Reason.Trim()}";
                    var supplierPaid = purchase.Payments.Sum(payment => payment.Amount);
                    if (purchase.Status == PurchaseStatus.Received && (delete || purchase.SupplierId != targetSupplier))
                    {
                        await SupplierAdjustmentAsync(db, purchase.SupplierId, -(purchase.TotalAmount - supplierPaid), supplierNote, actorId, now, ct);
                        if (!delete)
                        {
                            var balance = await SupplierAdjustmentAsync(db, targetSupplier, purchase.TotalAmount - supplierPaid, supplierNote, actorId, now, ct);
                            foreach (var payment in purchase.Payments)
                            {
                                payment.SupplierBalance = balance;
                                payment.SupplierBalanceId = balance.Id;
                            }
                        }
                    }
                    if (delete && purchase.Status == PurchaseStatus.Received)
                    {
                        purchase.Status = PurchaseStatus.Cancelled;
                        await CashDrawerService.ReverseAsync(db, "purchase", id, actorId, supplierNote, activeDate, purchase.StockLocationId, ct);
                        await FinancialPosting.ReverseBankAsync(db, "purchase", id, actorId, supplierNote, activeDate, ct);
                        affectedProducts = purchase.Items.Select(item => item.ProductId).Distinct().ToArray();
                        var purchaseRef = !string.IsNullOrWhiteSpace(purchase.PurchaseNumber) && !(purchase.PurchaseNumber.StartsWith("REC-", StringComparison.OrdinalIgnoreCase) && purchase.PurchaseNumber.Length > 20)
                            ? purchase.PurchaseNumber
                            : $"#{id}";
                        var invoiceInfo = string.IsNullOrWhiteSpace(purchase.SupplierInvoiceNumber) ? "" : $" (Invoice: {purchase.SupplierInvoiceNumber.Trim()})";
                        costReason = $"Reversal of purchase {purchaseRef}{invoiceInfo}";
                        costReferenceType = "PurchaseReversal";
                        costReferenceId = id;
                        foreach (var item in purchase.Items.OrderBy(item => item.Quantity > 0))
                        {
                            await ReverseStockAsync(db, item.ProductId, purchase.StockLocationId, -item.Quantity,
                                item.UnitCost, supplierNote, actorId, now, ct);
                        }
                    }
                    if (!delete)
                    {
                        purchase.SupplierId = targetSupplier;
                    }
                    after = new { purchase.SupplierId, purchase.ReceivedAt, purchase.OrderedAt, Status = purchase.Status, IsDeleted = delete };
                    break;
                case "product" when delete:
                    var product = await db.Products.SingleOrDefaultAsync(item => item.Id == id, ct)
                        ?? throw new ValidationException("Product not found.");
                    entity = product;
                    if (product.IsDeleted)
                    {
                        return;
                    }
                    ValidateRevision(entity, input);
                    before = new { product.Name, product.IsDeleted };
                    after = new { product.Name, IsDeleted = true };
                    break;
                default:
                    throw new ValidationException("Record not found.");
            }
            entity.IsDeleted = delete;
            entity.DeletedAt = delete ? now : null;
            entity.UpdatedAt = now;
            entity.UpdatedByUserId = actorId;
            db.RecordChanges.Add(new RecordChange
            {
                RecordType = kind,
                RecordId = id,
                Action = action,
                Reason = input.Reason.Trim(),
                Before = JsonSerializer.Serialize(before),
                After = JsonSerializer.Serialize(after),
                CreatedAt = now,
                CreatedByUserId = actorId
            });
            await db.SaveChangesAsync(ct);
            foreach (var productId in affectedProducts)
            {
                await RecalculateCostAsync(db, productId, actorId, costReason, costReferenceType, costReferenceId, ct);
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }, ct);
    }

    private static void ValidateRevision(SoftDeletableEntity entity, RecordCorrectionInput input)
    {
        if (entity.IsDeleted || Revision(entity) != input.ExpectedRevision)
        {
            throw new ValidationException("This record has changed or was deleted. Reload before editing it.");
        }
    }

    private static async Task ReverseStockAsync(AppDbContext db, int productId, int locationId, decimal quantity,
        decimal cost, string note, int actorId, DateTimeOffset now, CancellationToken ct)
    {
        var stock = db.InventoryBalances.Local.SingleOrDefault(item => item.ProductId == productId && item.StockLocationId == locationId)
            ?? await db.InventoryBalances.SingleOrDefaultAsync(item => item.ProductId == productId && item.StockLocationId == locationId, ct);
        if (stock is null)
        {
            stock = new InventoryBalance { ProductId = productId, StockLocationId = locationId, QuantityOnHand = 0 };
            db.InventoryBalances.Add(stock);
        }
        var resultingQuantity = stock.QuantityOnHand + quantity;
        if (resultingQuantity < -999999999999999.999m || resultingQuantity > 999999999999999.999m)
        {
            throw new ValidationException("Deletion would exceed supported inventory limits.");
        }
        stock.QuantityOnHand = resultingQuantity;
        stock.UpdatedAt = DateTimeOffset.UtcNow;
        stock.UpdatedByUserId = actorId;
        db.InventoryMovements.Add(new InventoryMovement
        {
            ProductId = productId,
            StockLocationId = locationId,
            Type = InventoryMovementType.TransactionReversal,
            Quantity = quantity,
            UnitCost = cost,
            Notes = note,
            OccurredAt = now,
            CreatedByUserId = actorId
        });
    }

    private static async Task RecalculateCostAsync(AppDbContext db, int productId, int? actorId,
        string? reason = null, string? referenceType = null, int? referenceId = null, CancellationToken ct = default)
    {
        var movements = await db.InventoryMovements.AsNoTracking().Where(move => move.ProductId == productId
                && move.Type != InventoryMovementType.TransactionReversal
                && (move.SaleItem == null || !move.SaleItem.Sale!.IsDeleted)
                && (move.PurchaseItem == null || !move.PurchaseItem.Purchase!.IsDeleted))
            .OrderBy(move => move.Id).ToListAsync(ct);
        decimal quantity = 0;
        decimal cost = 0;
        foreach (var move in movements)
        {
            if (move.Quantity > 0 && move.Type is InventoryMovementType.Purchase or InventoryMovementType.Opening or InventoryMovementType.Adjustment)
            {
                cost = InventoryValuationCalculator.CalculateMovingAverageCost(quantity, cost, move.Quantity, move.UnitCost);
            }
            quantity += move.Quantity;
        }
        var product = await db.Products.FindAsync([productId], ct);
        if (product is not null)
        {
            if (product.CostPrice != cost)
            {
                db.ProductCostHistories.Add(new ProductCostHistory
                {
                    ProductId = product.Id,
                    OldCostPrice = product.CostPrice,
                    NewCostPrice = cost,
                    Reason = reason ?? "Admin inventory recalculation",
                    ReferenceType = referenceType ?? "AdminCorrection",
                    ReferenceId = referenceId ?? product.Id,
                    CreatedByUserId = actorId,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }
            product.CostPrice = cost;
        }
    }

    private static async Task<CustomerBalance?> CustomerAdjustmentAsync(AppDbContext db, int? customerId, decimal amount,
        string note, int actorId, DateTimeOffset now, CancellationToken ct)
    {
        if (customerId is null)
        {
            return null;
        }
        var balance = await db.CustomerBalances.SingleOrDefaultAsync(item => item.CustomerId == customerId, ct);
        if (balance is null)
        {
            balance = new CustomerBalance { CustomerId = customerId.Value, OpenedAt = now };
            balance.Entries.Add(new CustomerBalanceEntry
            {
                CustomerBalanceId = 0,
                CustomerBalance = balance,
                Type = BalanceEntryType.OpeningBalance,
                OccurredAt = now
            });
            db.CustomerBalances.Add(balance);
        }
        balance.CurrentBalance += amount;
        StoreAccess.Money(balance.CurrentBalance, "Customer balance", negative: true);
        balance.UpdatedAt = DateTimeOffset.UtcNow;
        balance.UpdatedByUserId = actorId;
        balance.Entries.Add(new CustomerBalanceEntry
        {
            CustomerBalanceId = balance.Id,
            CustomerBalance = balance,
            Type = BalanceEntryType.Adjustment,
            Amount = amount,
            BalanceAfter = balance.CurrentBalance,
            Notes = note,
            OccurredAt = now,
            CreatedByUserId = actorId
        });
        return balance;
    }

    private static async Task<SupplierBalance> SupplierAdjustmentAsync(AppDbContext db, int supplierId, decimal amount,
        string note, int actorId, DateTimeOffset now, CancellationToken ct)
    {
        var balance = await db.SupplierBalances.SingleOrDefaultAsync(item => item.SupplierId == supplierId, ct);
        if (balance is null)
        {
            balance = new SupplierBalance { SupplierId = supplierId, OpenedAt = now };
            balance.Entries.Add(new SupplierBalanceEntry
            {
                SupplierBalanceId = 0,
                SupplierBalance = balance,
                Type = BalanceEntryType.OpeningBalance,
                OccurredAt = now
            });
            db.SupplierBalances.Add(balance);
        }
        balance.CurrentBalance += amount;
        StoreAccess.Money(balance.CurrentBalance, "Supplier balance", negative: true);
        balance.UpdatedAt = DateTimeOffset.UtcNow;
        balance.UpdatedByUserId = actorId;
        balance.Entries.Add(new SupplierBalanceEntry
        {
            SupplierBalanceId = balance.Id,
            SupplierBalance = balance,
            Type = BalanceEntryType.Adjustment,
            Amount = amount,
            BalanceAfter = balance.CurrentBalance,
            Notes = note,
            OccurredAt = now,
            CreatedByUserId = actorId
        });
        return balance;
    }
}
