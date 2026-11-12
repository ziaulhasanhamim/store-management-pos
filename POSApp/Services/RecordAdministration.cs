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

    private static bool IsSetupKind(string kind) =>
        kind is "product" or "category" or "customer" or "supplier" or "employee" or "lender";

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
        int? party = null;
        AuditableEntity? entity;
        bool deletedOrVoided;
        IReadOnlyList<Lookup> parties = [];

        switch (kind)
        {
            case "sale":
                var sale = await db.Sales.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, ct);
                if (sale is not null)
                {
                    total = sale.TotalAmount;
                    paid = await db.Payments.Where(payment => payment.SaleId == id && !payment.IsVoided).Select(payment => (decimal?)payment.Amount).SumAsync(ct) ?? 0;
                    stockEffects = await db.SaleItems.Where(item => item.SaleId == id && !item.Product!.IsNonStocked)
                        .Select(item => new RecordStockEffect(item.ProductName, item.Quantity, item.Unit)).ToArrayAsync(ct);
                }
                entity = sale;
                deletedOrVoided = sale is not null && (sale.IsDeleted || sale.Status == SaleStatus.Voided);
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
                    paid = await db.SupplierPayments.Where(payment => payment.PurchaseId == id && !payment.IsVoided).Select(payment => (decimal?)payment.Amount).SumAsync(ct) ?? 0;
                    stockEffects = await db.PurchaseItems.Where(item => item.PurchaseId == id)
                        .Select(item => new RecordStockEffect(item.ProductName, -item.Quantity, item.Unit)).ToArrayAsync(ct);
                }
                entity = purchase;
                deletedOrVoided = purchase is not null && (purchase.IsDeleted || purchase.Status == PurchaseStatus.Cancelled);
                name = $"Receiving #{id}";
                at = purchase?.ReceivedAt ?? purchase?.OrderedAt ?? default;
                party = purchase?.SupplierId;
                parties = await db.Suppliers.Where(item => !item.IsDeleted || item.Id == party)
                    .OrderBy(item => item.Name).Select(item => new Lookup(item.Id, item.Name)).ToArrayAsync(ct);
                break;

            case "stock-transfer":
                var transfer = await db.StockTransfers.AsNoTracking()
                    .Include(t => t.SourceLocation)
                    .Include(t => t.DestinationLocation)
                    .SingleOrDefaultAsync(item => item.Id == id, ct);
                if (transfer is not null)
                {
                    stockEffects = await db.StockTransferItems.Where(item => item.StockTransferId == id)
                        .Select(item => new RecordStockEffect(item.Product!.Name, item.Quantity, item.Product.Unit)).ToArrayAsync(ct);
                }
                entity = transfer;
                deletedOrVoided = transfer?.IsVoided ?? false;
                name = transfer is null ? $"Stock transfer #{id}" : $"Stock transfer #{id} ({transfer.SourceLocation?.Name} → {transfer.DestinationLocation?.Name})";
                at = transfer?.OccurredAt ?? default;
                break;

            case "stock-adjustment":
                var adjustment = await db.StockAdjustments.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, ct);
                if (adjustment is not null)
                {
                    stockEffects = await db.StockAdjustmentItems.Where(item => item.StockAdjustmentId == id)
                        .Select(item => new RecordStockEffect(item.ProductName, -item.Quantity, item.Unit)).ToArrayAsync(ct);
                    total = await db.StockAdjustmentItems.Where(item => item.StockAdjustmentId == id)
                        .Select(item => (decimal?)(item.Quantity * item.UnitCost)).SumAsync(ct) ?? 0;
                }
                entity = adjustment;
                deletedOrVoided = adjustment?.IsVoided ?? false;
                name = $"Stock adjustment #{id}";
                at = adjustment is null ? default : AccountingCalendar.At(adjustment.Date, configuration);
                break;

            case "stock-take":
                var take = await db.StockTakes.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id && item.CompletedAt != null, ct);
                if (take is not null)
                {
                    stockEffects = await db.StockTakeItems.Where(item => item.StockTakeId == id && item.IsIncluded && item.CountedQuantity != null && item.CountedQuantity != item.ExpectedQuantity)
                        .Select(item => new RecordStockEffect(item.ProductName, -(item.CountedQuantity!.Value - item.ExpectedQuantity), item.Unit)).ToArrayAsync(ct);
                }
                entity = take;
                deletedOrVoided = take?.IsVoided ?? false;
                name = $"Stock take #{id}";
                at = take?.CompletedAt ?? default;
                break;

            case "payroll":
                var payroll = await db.PayrollEntries.AsNoTracking()
                    .Include(p => p.StaffAccount)
                        .ThenInclude(a => a!.Employee)
                    .SingleOrDefaultAsync(item => item.Id == id, ct);
                if (payroll is not null)
                {
                    total = payroll.Amount;
                    paid = payroll.PaidAmount;
                }
                entity = payroll;
                deletedOrVoided = payroll?.IsVoided ?? false;
                name = payroll is null ? $"Payroll #{id}" : $"{payroll.Kind} #{id} · {payroll.StaffAccount?.Employee?.Name}";
                at = payroll?.OccurredAt ?? default;
                break;

            case "debt":
                var debt = await db.DebtEntries.AsNoTracking()
                    .Include(d => d.LenderBalance)
                        .ThenInclude(b => b!.Lender)
                    .SingleOrDefaultAsync(item => item.Id == id && item.Kind != DebtEntryKind.OpeningBalance, ct);
                if (debt is not null)
                {
                    total = Math.Abs(debt.Amount);
                    paid = Math.Abs(debt.Amount);
                }
                entity = debt;
                deletedOrVoided = debt?.IsVoided ?? false;
                name = debt is null ? $"Debt entry #{id}" : $"{debt.Kind} #{id} · {debt.LenderBalance?.Lender?.Name}";
                at = debt is null ? default : AccountingCalendar.At(debt.BusinessDate, configuration);
                break;

            case "cash-transfer":
                var cashTransfer = await db.CashTransfers.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, ct);
                if (cashTransfer is not null)
                {
                    total = cashTransfer.Amount;
                    paid = cashTransfer.Amount;
                }
                entity = cashTransfer;
                deletedOrVoided = cashTransfer?.IsVoided ?? false;
                name = $"Cash transfer #{id}";
                at = cashTransfer is null ? default : AccountingCalendar.At(cashTransfer.BusinessDate, configuration);
                break;

            case "product":
                var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, ct);
                entity = product;
                deletedOrVoided = product?.IsDeleted ?? false;
                name = product?.Name ?? "Product";
                at = product?.CreatedAt ?? default;
                break;

            case "category":
                var category = await db.Categories.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, ct);
                entity = category;
                deletedOrVoided = category?.IsDeleted ?? false;
                name = category?.Name ?? "Category";
                at = category?.CreatedAt ?? default;
                break;

            case "customer":
                var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, ct);
                entity = customer;
                deletedOrVoided = customer?.IsDeleted ?? false;
                name = customer?.Name ?? "Customer";
                at = customer?.CreatedAt ?? default;
                break;

            case "supplier":
                var supplier = await db.Suppliers.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, ct);
                entity = supplier;
                deletedOrVoided = supplier?.IsDeleted ?? false;
                name = supplier?.Name ?? "Supplier";
                at = supplier?.CreatedAt ?? default;
                break;

            case "employee":
                var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, ct);
                entity = employee;
                deletedOrVoided = employee?.IsDeleted ?? false;
                name = employee?.Name ?? "Employee";
                at = employee?.CreatedAt ?? default;
                break;

            case "lender":
                var lender = await db.Lenders.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, ct);
                entity = lender;
                deletedOrVoided = lender?.IsDeleted ?? false;
                name = lender?.Name ?? "Lender";
                at = lender?.CreatedAt ?? default;
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
        return new(kind, id, name, at, party, deletedOrVoided, Revision(entity), parties,
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
            if (!delete && kind is not ("sale" or "purchase"))
            {
                throw new ValidationException("Only sale customer or receiving supplier assignments can be edited here.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);
            var isAdmin = await CashDrawerService.IsAdminAsync(db, actorId, ct);
            if (!isAdmin)
            {
                if (kind != "sale" || !delete)
                {
                    throw new UnauthorizedAccessException("Only administrators can perform this action.");
                }
            }
            var activeDate = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
            var effectiveAt = AccountingCalendar.At(activeDate, configuration);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var now = DateTimeOffset.UtcNow;
            var reasonText = input.Reason.Trim();
            var zone = AccountingCalendar.Zone(configuration);

            AuditableEntity entity;
            object before;
            object after;
            int[] affectedProducts = [];
            string? costReason = null;
            string? costReferenceType = null;
            int? costReferenceId = null;
            var action = delete ? (IsSetupKind(kind) ? "Archive" : "Void") : "Edit";
            var physicallyRemoved = false;

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
                    ValidateRevision(sale, sale.IsDeleted || sale.Status == SaleStatus.Voided, input);
                    var saleDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(sale.SoldAt, zone).DateTime);
                    if (!isAdmin)
                    {
                        if (sale.CashierId != actorId || saleDate != activeDate)
                        {
                            throw new UnauthorizedAccessException("Cashiers can only void their own sales in the current open working day. Voiding older sales or sales made by other staff requires an administrator with a reason.");
                        }
                    }
                    // Enforce closed-period immutability on BOTH edit and void
                    await AccountingCalendar.EnsureOpenAsync(db, saleDate, ct);
                    if (delete && activeDate != saleDate)
                    {
                        await AccountingCalendar.EnsureOpenAsync(db, activeDate, ct);
                    }
                    before = new { sale.CustomerId, sale.SoldAt, sale.Status, sale.IsDeleted };
                    var targetCustomer = delete ? null : input.PartyId;
                    if (targetCustomer is not null && !await db.Customers.AnyAsync(customer => customer.Id == targetCustomer.Value && !customer.IsDeleted, ct))
                    {
                        throw new ValidationException("Choose an active customer.");
                    }
                    var activeSalePayments = sale.Payments.Where(p => !p.IsVoided).ToArray();
                    var paid = activeSalePayments.Sum(payment => payment.Amount);
                    if (!delete && targetCustomer is null && sale.Status == SaleStatus.Completed && paid != sale.TotalAmount)
                    {
                        throw new ValidationException("An unpaid sale or customer credit must belong to a customer.");
                    }
                    var note = $"Sale {(delete ? "void" : "customer change")} #{id}: {reasonText}";
                    if (sale.Status == SaleStatus.Completed && delete)
                    {
                        // Voiding a transaction takes the original entry's date and time (+1ms), so all effects fall in the same interval
                        var saleEffectiveAt = sale.SoldAt.AddMilliseconds(1);

                        // Symmetric party ledger reversals: credit the invoice total (-Total), then debit back any payment (+Paid)
                        if (sale.CustomerId is not null)
                        {
                            if (sale.TotalAmount != 0)
                            {
                                await CustomerAdjustmentAsync(db, sale.CustomerId, -sale.TotalAmount,
                                    $"Void invoice #{id}: {reasonText}", actorId, saleEffectiveAt, ct);
                            }
                            if (paid != 0)
                            {
                                await CustomerAdjustmentAsync(db, sale.CustomerId, paid,
                                    $"Void payment(s) on sale #{id}: {reasonText}", actorId, saleEffectiveAt, ct);
                            }
                        }
                        foreach (var payment in activeSalePayments)
                        {
                            payment.IsVoided = true;
                            payment.VoidedAt = now;
                            payment.VoidedByUserId = actorId;
                            payment.VoidReason = note;
                            payment.UpdatedAt = now;
                            payment.UpdatedByUserId = actorId;
                        }
                        sale.Status = SaleStatus.Voided;
                        sale.IsDeleted = true;
                        sale.DeletedAt = now;
                        await CashDrawerService.ReverseAsync(db, "sale", id, actorId, note, saleDate, sale.StockLocationId, ct);
                        await FinancialPosting.ReverseBankAsync(db, "sale", id, actorId, note, saleDate, ct);
                        await FinancialPosting.ReverseClearingAsync(db, id, actorId, note, saleDate, configuration, ct);
                        affectedProducts = sale.Items.Select(item => item.ProductId).Distinct().ToArray();
                        costReason = $"Reversal of sale #{id}";
                        costReferenceType = "SaleReversal";
                        costReferenceId = id;
                        var nonStockedIds = await db.Products.Where(product => affectedProducts.Contains(product.Id) && product.IsNonStocked)
                            .Select(product => product.Id).ToArrayAsync(ct);
                        foreach (var item in sale.Items.Where(item => !nonStockedIds.Contains(item.ProductId)).OrderBy(item => item.Quantity < 0))
                        {
                            await ReverseStockAsync(db, item.ProductId, sale.StockLocationId, item.Quantity,
                                item.UnitCost, note, actorId, saleEffectiveAt, ct);
                        }
                    }
                    else if (!delete)
                    {
                        if (sale.Status == SaleStatus.Completed && sale.CustomerId != targetCustomer)
                        {
                            await CustomerAdjustmentAsync(db, sale.CustomerId, -(sale.TotalAmount - paid), note, actorId, effectiveAt, ct);
                            var targetBalance = await CustomerAdjustmentAsync(db, targetCustomer, sale.TotalAmount - paid, note, actorId, effectiveAt, ct);
                            foreach (var payment in activeSalePayments)
                            {
                                payment.CustomerBalance = targetBalance;
                                payment.CustomerBalanceId = targetBalance?.Id;
                            }
                        }
                        sale.CustomerId = targetCustomer;
                    }
                    else
                    {
                        sale.IsDeleted = true;
                        sale.DeletedAt = now;
                    }
                    after = new { sale.CustomerId, sale.SoldAt, sale.Status, sale.IsDeleted };
                    break;

                case "purchase":
                    var purchase = await db.Purchases.Include(item => item.Items).Include(item => item.Payments)
                        .SingleOrDefaultAsync(item => item.Id == id, ct) ?? throw new ValidationException("Receiving not found.");
                    entity = purchase;
                    if (delete && (purchase.IsDeleted || purchase.Status == PurchaseStatus.Cancelled))
                    {
                        return;
                    }
                    ValidateRevision(purchase, purchase.IsDeleted || purchase.Status == PurchaseStatus.Cancelled, input);
                    var purchaseDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(purchase.ReceivedAt ?? purchase.OrderedAt, zone).DateTime);
                    // Enforce closed-period immutability on BOTH edit and void
                    await AccountingCalendar.EnsureOpenAsync(db, purchaseDate, ct);
                    if (delete && activeDate != purchaseDate)
                    {
                        await AccountingCalendar.EnsureOpenAsync(db, activeDate, ct);
                    }
                    before = new { purchase.SupplierId, purchase.ReceivedAt, purchase.OrderedAt, purchase.Status, purchase.IsDeleted };
                    var targetSupplier = delete ? purchase.SupplierId : input.PartyId ?? 0;
                    if (!delete && !await db.Suppliers.AnyAsync(supplier => supplier.Id == targetSupplier && !supplier.IsDeleted, ct))
                    {
                        throw new ValidationException("Choose an active supplier.");
                    }
                    var supplierNote = $"Purchase {(delete ? "void" : "supplier change")} #{id}: {reasonText}";
                    var activeSupplierPayments = purchase.Payments.Where(p => !p.IsVoided).ToArray();
                    var supplierPaid = activeSupplierPayments.Sum(payment => payment.Amount);
                    if (purchase.Status == PurchaseStatus.Received && delete)
                    {
                        // Voiding receiving takes the original entry's date and time (+1ms)
                        var purchaseEffectiveAt = (purchase.ReceivedAt ?? purchase.OrderedAt).AddMilliseconds(1);

                        // Symmetric party ledger reversals: debit the bill total (-Total), then credit back any payment (+Paid)
                        if (purchase.TotalAmount != 0)
                        {
                            await SupplierAdjustmentAsync(db, purchase.SupplierId, -purchase.TotalAmount,
                                $"Void bill #{id}: {reasonText}", actorId, purchaseEffectiveAt, ct);
                        }
                        if (supplierPaid != 0)
                        {
                            await SupplierAdjustmentAsync(db, purchase.SupplierId, supplierPaid,
                                $"Void payment(s) on receiving #{id}: {reasonText}", actorId, purchaseEffectiveAt, ct);
                        }
                        foreach (var payment in activeSupplierPayments)
                        {
                            payment.IsVoided = true;
                            payment.VoidedAt = now;
                            payment.VoidedByUserId = actorId;
                            payment.VoidReason = supplierNote;
                            payment.UpdatedAt = now;
                            payment.UpdatedByUserId = actorId;
                        }
                        purchase.Status = PurchaseStatus.Cancelled;
                        purchase.IsDeleted = true;
                        purchase.DeletedAt = now;
                        await CashDrawerService.ReverseAsync(db, "purchase", id, actorId, supplierNote, purchaseDate, purchase.StockLocationId, ct);
                        await FinancialPosting.ReverseBankAsync(db, "purchase", id, actorId, supplierNote, purchaseDate, ct);
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
                                item.UnitCost, supplierNote, actorId, purchaseEffectiveAt, ct);
                        }
                    }
                    else if (!delete)
                    {
                        if (purchase.Status == PurchaseStatus.Received && purchase.SupplierId != targetSupplier)
                        {
                            await SupplierAdjustmentAsync(db, purchase.SupplierId, -(purchase.TotalAmount - supplierPaid), supplierNote, actorId, effectiveAt, ct);
                            var balance = await SupplierAdjustmentAsync(db, targetSupplier, purchase.TotalAmount - supplierPaid, supplierNote, actorId, effectiveAt, ct);
                            foreach (var payment in activeSupplierPayments)
                            {
                                payment.SupplierBalance = balance;
                                payment.SupplierBalanceId = balance.Id;
                            }
                        }
                        purchase.SupplierId = targetSupplier;
                    }
                    else
                    {
                        purchase.IsDeleted = true;
                        purchase.DeletedAt = now;
                    }
                    after = new { purchase.SupplierId, purchase.ReceivedAt, purchase.OrderedAt, purchase.Status, purchase.IsDeleted };
                    break;

                case "stock-transfer" when delete:
                    var transfer = await db.StockTransfers.Include(t => t.Items).ThenInclude(i => i.Product)
                        .SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw new ValidationException("Stock transfer not found.");
                    entity = transfer;
                    if (transfer.IsVoided)
                    {
                        return;
                    }
                    ValidateRevision(transfer, transfer.IsVoided, input);
                    var transferDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(transfer.OccurredAt, zone).DateTime);
                    await AccountingCalendar.EnsureOpenAsync(db, transferDate, ct);
                    if (activeDate != transferDate)
                    {
                        await AccountingCalendar.EnsureOpenAsync(db, activeDate, ct);
                    }
                    before = new { transfer.SourceLocationId, transfer.DestinationLocationId, transfer.IsVoided };
                    var transferNote = $"Stock transfer void #{id}: {reasonText}";
                    foreach (var item in transfer.Items)
                    {
                        var destStock = await db.InventoryBalances.SingleOrDefaultAsync(
                            b => b.ProductId == item.ProductId && b.StockLocationId == transfer.DestinationLocationId, ct);
                        if (destStock is null || destStock.QuantityOnHand < item.Quantity)
                        {
                            throw new ValidationException($"Destination location no longer holds enough {item.Product?.Name ?? "item"} to reverse this transfer.");
                        }
                        await ReverseStockAsync(db, item.ProductId, transfer.DestinationLocationId, -item.Quantity,
                            item.UnitCost, transferNote, actorId, effectiveAt, ct);
                        await ReverseStockAsync(db, item.ProductId, transfer.SourceLocationId, item.Quantity,
                            item.UnitCost, transferNote, actorId, effectiveAt, ct);
                    }
                    transfer.IsVoided = true;
                    transfer.VoidedAt = now;
                    transfer.VoidedByUserId = actorId;
                    transfer.VoidReason = reasonText;
                    after = new { transfer.SourceLocationId, transfer.DestinationLocationId, transfer.IsVoided };
                    break;

                case "stock-adjustment" when delete:
                    var adjustment = await db.StockAdjustments.Include(a => a.Items)
                        .SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw new ValidationException("Stock adjustment not found.");
                    entity = adjustment;
                    if (adjustment.IsVoided)
                    {
                        return;
                    }
                    ValidateRevision(adjustment, adjustment.IsVoided, input);
                    await AccountingCalendar.EnsureOpenAsync(db, adjustment.Date, ct, requireDayOpened: false);

                    before = new { adjustment.StockLocationId, adjustment.Date, adjustment.IsVoided };
                    var adjNote = $"Stock adjustment void #{id}: {reasonText}";
                    foreach (var item in adjustment.Items)
                    {
                        await ReverseStockAsync(db, item.ProductId, adjustment.StockLocationId, -item.Quantity,
                            item.UnitCost, adjNote, actorId, effectiveAt, ct);
                    }
                    affectedProducts = adjustment.Items.Select(i => i.ProductId).Distinct().ToArray();
                    costReason = $"Reversal of stock adjustment #{id}";
                    costReferenceType = "StockAdjustmentReversal";
                    costReferenceId = id;
                    adjustment.IsVoided = true;
                    adjustment.VoidedAt = now;
                    adjustment.VoidedByUserId = actorId;
                    adjustment.VoidReason = reasonText;
                    after = new { adjustment.StockLocationId, adjustment.Date, adjustment.IsVoided };
                    break;

                case "stock-take" when delete:
                    var take = await db.StockTakes.Include(t => t.Items)
                        .SingleOrDefaultAsync(t => t.Id == id && t.CompletedAt != null, ct)
                        ?? throw new ValidationException("Completed stock take not found.");
                    entity = take;
                    if (take.IsVoided)
                    {
                        return;
                    }
                    ValidateRevision(take, take.IsVoided, input);
                    await AccountingCalendar.EnsureOpenAsync(db, take.BusinessDate, ct);
                    if (activeDate != take.BusinessDate)
                    {
                        await AccountingCalendar.EnsureOpenAsync(db, activeDate, ct);
                    }
                    before = new { take.StockLocationId, take.BusinessDate, take.IsVoided };
                    var takeNote = $"Stock take void #{id}: {reasonText}";
                    var varianceItems = take.Items
                        .Where(i => i.IsIncluded && i.CountedQuantity != null && i.CountedQuantity != i.ExpectedQuantity)
                        .ToArray();
                    foreach (var item in varianceItems)
                    {
                        var variance = item.CountedQuantity!.Value - item.ExpectedQuantity;
                        await ReverseStockAsync(db, item.ProductId, take.StockLocationId, -variance,
                            item.UnitCost, takeNote, actorId, effectiveAt, ct);
                    }
                    affectedProducts = varianceItems.Select(i => i.ProductId).Distinct().ToArray();
                    costReason = $"Reversal of stock take #{id}";
                    costReferenceType = "StockTakeReversal";
                    costReferenceId = id;
                    take.IsVoided = true;
                    take.VoidedAt = now;
                    take.VoidedByUserId = actorId;
                    take.VoidReason = reasonText;
                    after = new { take.StockLocationId, take.BusinessDate, take.IsVoided };
                    break;

                case "payroll" when delete:
                    var payroll = await db.PayrollEntries
                        .Include(p => p.StaffAccount)
                        .Include(p => p.MonthlySalary)
                        .SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw new ValidationException("Payroll entry not found.");
                    entity = payroll;
                    if (payroll.IsVoided)
                    {
                        return;
                    }
                    ValidateRevision(payroll, payroll.IsVoided, input);
                    var payrollDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(payroll.OccurredAt, zone).DateTime);
                    await AccountingCalendar.EnsureOpenAsync(db, payrollDate, ct);
                    if (activeDate != payrollDate)
                    {
                        await AccountingCalendar.EnsureOpenAsync(db, activeDate, ct);
                    }
                    if (payroll.MonthlySalary?.CarriedToMonth is not null)
                    {
                        throw new ValidationException("Cannot void a payroll entry whose salary month has already been carried forward.");
                    }
                    before = new { payroll.Kind, payroll.Amount, payroll.PaidAmount, payroll.AdvanceApplied, payroll.AdvanceCreated, payroll.IsVoided };
                    var payrollNote = $"Payroll void #{id}: {reasonText}";
                    await ReverseEntityMovementsAsync(db, "payroll", payroll.Id, actorId, payrollNote, activeDate, ct);
                    if (payroll.StaffAccount is not null)
                    {
                        var updatedAdvance = payroll.StaffAccount.OutstandingAdvance - payroll.AdvanceCreated + payroll.AdvanceApplied;
                        if (updatedAdvance < 0)
                        {
                            throw new ValidationException("Cannot void this advance because part of it has already been applied to salary. Void the salary settlement first.");
                        }
                        payroll.StaffAccount.OutstandingAdvance = updatedAdvance;
                        payroll.StaffAccount.UpdatedAt = now;
                        payroll.StaffAccount.UpdatedByUserId = actorId;
                    }
                    if (payroll.MonthlySalary is not null)
                    {
                        payroll.MonthlySalary.SettledAmount = Math.Max(0, payroll.MonthlySalary.SettledAmount - payroll.Amount);
                        payroll.MonthlySalary.UpdatedAt = now;
                        payroll.MonthlySalary.UpdatedByUserId = actorId;
                    }
                    payroll.IsVoided = true;
                    payroll.VoidedAt = now;
                    payroll.VoidedByUserId = actorId;
                    payroll.VoidReason = reasonText;
                    after = new { payroll.Kind, payroll.Amount, payroll.PaidAmount, payroll.AdvanceApplied, payroll.AdvanceCreated, payroll.IsVoided };
                    break;

                case "debt" when delete:
                    var debt = await db.DebtEntries.Include(d => d.LenderBalance)
                        .SingleOrDefaultAsync(d => d.Id == id && d.Kind != DebtEntryKind.OpeningBalance, ct)
                        ?? throw new ValidationException("Debt entry not found.");
                    entity = debt;
                    if (debt.IsVoided)
                    {
                        return;
                    }
                    ValidateRevision(debt, debt.IsVoided, input);
                    await AccountingCalendar.EnsureOpenAsync(db, debt.BusinessDate, ct);
                    if (activeDate != debt.BusinessDate)
                    {
                        await AccountingCalendar.EnsureOpenAsync(db, activeDate, ct);
                    }
                    var lenderBalance = debt.LenderBalance ?? throw new ValidationException("Lender balance not found.");
                    if (lenderBalance.CurrentBalance - debt.Amount < 0)
                    {
                        throw new ValidationException("Voiding this borrowing would make outstanding debt negative because later repayments exist.");
                    }
                    before = new { debt.Kind, debt.Amount, debt.IsVoided };
                    var debtNote = $"Debt entry void #{id}: {reasonText}";
                    await ReverseEntityMovementsAsync(db, "debt", debt.Id, actorId, debtNote, activeDate, ct);
                    lenderBalance.CurrentBalance -= debt.Amount;
                    StoreAccess.Money(lenderBalance.CurrentBalance, "Outstanding debt");
                    lenderBalance.UpdatedAt = now;
                    lenderBalance.UpdatedByUserId = actorId;
                    debt.IsVoided = true;
                    debt.VoidedAt = now;
                    debt.VoidedByUserId = actorId;
                    debt.VoidReason = reasonText;
                    after = new { debt.Kind, debt.Amount, debt.IsVoided };
                    break;

                case "cash-transfer" when delete:
                    var cashTransfer = await db.CashTransfers.SingleOrDefaultAsync(t => t.Id == id, ct)
                        ?? throw new ValidationException("Cash transfer not found.");
                    entity = cashTransfer;
                    if (cashTransfer.IsVoided)
                    {
                        return;
                    }
                    ValidateRevision(cashTransfer, cashTransfer.IsVoided, input);
                    await AccountingCalendar.EnsureOpenAsync(db, cashTransfer.BusinessDate, ct);
                    if (activeDate != cashTransfer.BusinessDate)
                    {
                        await AccountingCalendar.EnsureOpenAsync(db, activeDate, ct);
                    }
                    before = new { cashTransfer.SourceDrawerId, cashTransfer.DestinationDrawerId, cashTransfer.Amount, cashTransfer.IsVoided };
                    var ctNote = $"Cash transfer void #{id}: {reasonText}";
                    await ReverseEntityMovementsAsync(db, "cash-transfer", cashTransfer.Id, actorId, ctNote, activeDate, ct);
                    cashTransfer.IsVoided = true;
                    cashTransfer.VoidedAt = now;
                    cashTransfer.VoidedByUserId = actorId;
                    cashTransfer.VoidReason = reasonText;
                    after = new { cashTransfer.SourceDrawerId, cashTransfer.DestinationDrawerId, cashTransfer.Amount, cashTransfer.IsVoided };
                    break;

                case "product" when delete:
                    var product = await db.Products.SingleOrDefaultAsync(item => item.Id == id, ct)
                        ?? throw new ValidationException("Product not found.");
                    entity = product;
                    if (product.IsDeleted)
                    {
                        return;
                    }
                    ValidateRevision(product, product.IsDeleted, input);
                    before = new { product.Name, product.IsDeleted };
                    var productReferenced = await db.SaleItems.AnyAsync(i => i.ProductId == id, ct)
                        || await db.PurchaseItems.AnyAsync(i => i.ProductId == id, ct)
                        || await db.StockTransferItems.AnyAsync(i => i.ProductId == id, ct)
                        || await db.StockAdjustmentItems.AnyAsync(i => i.ProductId == id, ct)
                        || await db.StockTakeItems.AnyAsync(i => i.ProductId == id, ct)
                        || await db.InventoryMovements.AnyAsync(m => m.ProductId == id, ct)
                        || await db.InventoryBalances.AnyAsync(b => b.ProductId == id && b.QuantityOnHand != 0, ct);
                    if (!productReferenced)
                    {
                        db.InventoryBalances.RemoveRange(await db.InventoryBalances.Where(b => b.ProductId == id).ToListAsync(ct));
                        db.ProductAttributeValues.RemoveRange(await db.ProductAttributeValues.Where(v => v.ProductId == id).ToListAsync(ct));
                        db.ProductCostHistories.RemoveRange(await db.ProductCostHistories.Where(h => h.ProductId == id).ToListAsync(ct));
                        db.Products.Remove(product);
                        physicallyRemoved = true;
                        action = "Delete";
                        after = new { Deleted = true };
                    }
                    else
                    {
                        product.IsDeleted = true;
                        product.DeletedAt = now;
                        after = new { product.Name, IsDeleted = true };
                    }
                    break;

                case "category" when delete:
                    var category = await db.Categories.SingleOrDefaultAsync(item => item.Id == id, ct)
                        ?? throw new ValidationException("Category not found.");
                    entity = category;
                    if (category.IsDeleted)
                    {
                        return;
                    }
                    ValidateRevision(category, category.IsDeleted, input);
                    if (await db.Products.AnyAsync(p => p.CategoryId == id && !p.IsDeleted, ct))
                    {
                        throw new ValidationException("Reassign or archive active products in this category first.");
                    }
                    before = new { category.Name, category.IsDeleted };
                    if (!await db.Products.AnyAsync(p => p.CategoryId == id, ct))
                    {
                        db.Categories.Remove(category);
                        physicallyRemoved = true;
                        action = "Delete";
                        after = new { Deleted = true };
                    }
                    else
                    {
                        category.IsDeleted = true;
                        category.DeletedAt = now;
                        after = new { category.Name, IsDeleted = true };
                    }
                    break;

                case "customer" when delete:
                    var customer = await db.Customers.Include(c => c.Balance).SingleOrDefaultAsync(item => item.Id == id, ct)
                        ?? throw new ValidationException("Customer not found.");
                    entity = customer;
                    if (customer.IsDeleted)
                    {
                        return;
                    }
                    ValidateRevision(customer, customer.IsDeleted, input);
                    if ((customer.Balance?.CurrentBalance ?? 0) != 0)
                    {
                        throw new ValidationException("Settle or adjust this customer's remaining balance to zero before archiving.");
                    }
                    before = new { customer.Name, customer.IsDeleted };
                    var customerReferenced = await db.Sales.AnyAsync(s => s.CustomerId == id, ct)
                        || (customer.Balance != null && (customer.Balance.OpeningBalance != 0
                            || await db.Payments.AnyAsync(p => p.CustomerBalanceId == customer.Balance.Id, ct)
                            || await db.CustomerBalanceEntries.AnyAsync(e => e.CustomerBalanceId == customer.Balance.Id && e.Type != BalanceEntryType.OpeningBalance, ct)));
                    if (!customerReferenced)
                    {
                        if (customer.Balance != null)
                        {
                            db.CustomerBalanceEntries.RemoveRange(await db.CustomerBalanceEntries.Where(e => e.CustomerBalanceId == customer.Balance.Id).ToListAsync(ct));
                            db.CustomerBalances.Remove(customer.Balance);
                        }
                        db.Customers.Remove(customer);
                        physicallyRemoved = true;
                        action = "Delete";
                        after = new { Deleted = true };
                    }
                    else
                    {
                        customer.IsDeleted = true;
                        customer.DeletedAt = now;
                        after = new { customer.Name, IsDeleted = true };
                    }
                    break;

                case "supplier" when delete:
                    var supplier = await db.Suppliers.Include(s => s.Balance).SingleOrDefaultAsync(item => item.Id == id, ct)
                        ?? throw new ValidationException("Supplier not found.");
                    entity = supplier;
                    if (supplier.IsDeleted)
                    {
                        return;
                    }
                    ValidateRevision(supplier, supplier.IsDeleted, input);
                    if ((supplier.Balance?.CurrentBalance ?? 0) != 0)
                    {
                        throw new ValidationException("Settle or adjust this supplier's remaining balance to zero before archiving.");
                    }
                    before = new { supplier.Name, supplier.IsDeleted };
                    var supplierReferenced = await db.Purchases.AnyAsync(p => p.SupplierId == id, ct)
                        || await db.Products.AnyAsync(p => p.SupplierId == id, ct)
                        || (supplier.Balance != null && (supplier.Balance.OpeningBalance != 0
                            || await db.SupplierPayments.AnyAsync(p => p.SupplierBalanceId == supplier.Balance.Id, ct)
                            || await db.SupplierBalanceEntries.AnyAsync(e => e.SupplierBalanceId == supplier.Balance.Id && e.Type != BalanceEntryType.OpeningBalance, ct)));
                    if (!supplierReferenced)
                    {
                        if (supplier.Balance != null)
                        {
                            db.SupplierBalanceEntries.RemoveRange(await db.SupplierBalanceEntries.Where(e => e.SupplierBalanceId == supplier.Balance.Id).ToListAsync(ct));
                            db.SupplierBalances.Remove(supplier.Balance);
                        }
                        db.Suppliers.Remove(supplier);
                        physicallyRemoved = true;
                        action = "Delete";
                        after = new { Deleted = true };
                    }
                    else
                    {
                        supplier.IsDeleted = true;
                        supplier.DeletedAt = now;
                        after = new { supplier.Name, IsDeleted = true };
                    }
                    break;

                case "employee" when delete:
                    var employee = await db.Employees.Include(e => e.StaffAccount).SingleOrDefaultAsync(item => item.Id == id, ct)
                        ?? throw new ValidationException("Employee not found.");
                    entity = employee;
                    if (employee.IsDeleted)
                    {
                        return;
                    }
                    ValidateRevision(employee, employee.IsDeleted, input);
                    if ((employee.StaffAccount?.OutstandingAdvance ?? 0) != 0
                        || await db.MonthlySalaries.AnyAsync(s => s.EmployeeId == id && s.CarriedToMonth == null && s.Amount != s.SettledAmount, ct))
                    {
                        throw new ValidationException("Settle outstanding salary or staff advance before archiving this employee.");
                    }
                    before = new { employee.Name, employee.IsDeleted };
                    var employeeReferenced = await db.MonthlySalaries.AnyAsync(s => s.EmployeeId == id, ct)
                        || (employee.StaffAccount != null && await db.PayrollEntries.AnyAsync(p => p.StaffAccountId == employee.StaffAccount.Id, ct));
                    if (!employeeReferenced)
                    {
                        if (employee.StaffAccount != null)
                        {
                            db.StaffAccounts.Remove(employee.StaffAccount);
                        }
                        db.Employees.Remove(employee);
                        physicallyRemoved = true;
                        action = "Delete";
                        after = new { Deleted = true };
                    }
                    else
                    {
                        employee.IsDeleted = true;
                        employee.DeletedAt = now;
                        after = new { employee.Name, IsDeleted = true };
                    }
                    break;

                case "lender" when delete:
                    var lender = await db.Lenders.Include(l => l.Balance).SingleOrDefaultAsync(item => item.Id == id, ct)
                        ?? throw new ValidationException("Lender not found.");
                    entity = lender;
                    if (lender.IsDeleted)
                    {
                        return;
                    }
                    ValidateRevision(lender, lender.IsDeleted, input);
                    if ((lender.Balance?.CurrentBalance ?? 0) != 0)
                    {
                        throw new ValidationException("Repay or adjust this lender's outstanding debt to zero before archiving.");
                    }
                    before = new { lender.Name, lender.IsDeleted };
                    var lenderReferenced = lender.Balance != null && (lender.Balance.OpeningBalance != 0
                        || await db.DebtEntries.AnyAsync(d => d.LenderBalanceId == lender.Balance.Id && d.Kind != DebtEntryKind.OpeningBalance, ct));
                    if (!lenderReferenced)
                    {
                        if (lender.Balance != null)
                        {
                            db.DebtEntries.RemoveRange(await db.DebtEntries.Where(d => d.LenderBalanceId == lender.Balance.Id).ToListAsync(ct));
                            db.LenderBalances.Remove(lender.Balance);
                        }
                        db.Lenders.Remove(lender);
                        physicallyRemoved = true;
                        action = "Delete";
                        after = new { Deleted = true };
                    }
                    else
                    {
                        lender.IsDeleted = true;
                        lender.DeletedAt = now;
                        after = new { lender.Name, IsDeleted = true };
                    }
                    break;

                default:
                    throw new ValidationException("Record not found.");
            }

            if (!physicallyRemoved)
            {
                entity.UpdatedAt = now;
                entity.UpdatedByUserId = actorId;
            }
            db.RecordChanges.Add(new RecordChange
            {
                RecordType = kind,
                RecordId = id,
                Action = action,
                Reason = reasonText,
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

    private static void ValidateRevision(AuditableEntity entity, bool alreadyDeletedOrVoided, RecordCorrectionInput input)
    {
        if (alreadyDeletedOrVoided || Revision(entity) != input.ExpectedRevision)
        {
            throw new ValidationException("This record has changed or was already voided. Reload before editing it.");
        }
    }

    private static async Task ReverseEntityMovementsAsync(
        AppDbContext db, string kind, int id, int actorId, string reason, DateOnly activeDate, CancellationToken ct)
    {
        var cashMovements = await db.CashMovements
            .Include(m => m.CashSession)
                .ThenInclude(s => s!.CashDrawer)
            .Where(m => (kind == "payroll" ? m.PayrollEntryId == id
                    : kind == "debt" ? m.DebtEntryId == id
                    : m.CashTransferId == id)
                && m.ReversesMovementId == null
                && !db.CashMovements.Any(r => r.ReversesMovementId == m.Id))
            .ToListAsync(ct);

        foreach (var move in cashMovements)
        {
            var drawerId = move.CashDrawerId != 0 ? move.CashDrawerId : (move.CashSession?.CashDrawerId ?? 0);
            var drawerToUse = move.CashDrawer
                ?? await db.CashDrawers.SingleOrDefaultAsync(d => d.Id == drawerId, ct)
                ?? await CashDrawerService.RequireDrawerAsync(db, actorId, ct, move.CashSession?.StockLocationId);
            CashDrawerService.Append(db, drawerToUse, new CashMovement
            {
                RequestId = Guid.NewGuid(),
                OccurredAt = move.OccurredAt.AddMilliseconds(1),
                Kind = CashMovementKind.Reversal,
                Amount = -move.Amount,
                Notes = $"Deletion of {kind} #{id}: {reason}",
                ReversesMovementId = move.Id,
                CreatedByUserId = actorId
            });
        }

        if (kind is "payroll" or "debt")
        {
            var bankMovements = await db.BankMovements
                .Where(m => (kind == "payroll" ? m.PayrollEntryId == id : m.DebtEntryId == id)
                    && m.ReversesMovementId == null
                    && !db.BankMovements.Any(r => r.ReversesMovementId == m.Id))
                .ToListAsync(ct);
            foreach (var move in bankMovements)
            {
                await FinancialPosting.BankAsync(db, new BankMovement
                {
                    RequestId = Guid.NewGuid(),
                    OccurredAt = move.OccurredAt.AddMilliseconds(1),
                    Kind = CashMovementKind.Reversal,
                    Method = move.Method,
                    Amount = -move.Amount,
                    Notes = $"Deletion of {kind} #{id}: {reason}",
                    ReversesMovementId = move.Id,
                    CreatedByUserId = actorId
                }, ct);
            }
        }
    }

    private static async Task ReverseStockAsync(AppDbContext db, int productId, int locationId, decimal quantity,
        decimal cost, string note, int actorId, DateTimeOffset effectiveAt, CancellationToken ct)
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
            throw new ValidationException("Reversal would exceed supported inventory limits.");
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
            OccurredAt = effectiveAt,
            CreatedByUserId = actorId
        });
    }

    private static async Task RecalculateCostAsync(AppDbContext db, int productId, int? actorId,
        string? reason = null, string? referenceType = null, int? referenceId = null, CancellationToken ct = default)
    {
        var movements = await db.InventoryMovements.AsNoTracking().Where(move => move.ProductId == productId
                && move.Type != InventoryMovementType.TransactionReversal
                && (move.SaleItem == null || !move.SaleItem.Sale!.IsDeleted)
                && (move.PurchaseItem == null || !move.PurchaseItem.Purchase!.IsDeleted)
                && (move.StockAdjustmentItem == null || !move.StockAdjustmentItem.StockAdjustment!.IsVoided)
                && (move.StockTakeItem == null || !move.StockTakeItem.StockTake!.IsVoided)
                && (move.StockTransfer == null || !move.StockTransfer.IsVoided))
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
        string note, int actorId, DateTimeOffset effectiveAt, CancellationToken ct)
    {
        if (customerId is null)
        {
            return null;
        }
        var balance = db.CustomerBalances.Local.SingleOrDefault(item => item.CustomerId == customerId)
            ?? await db.CustomerBalances.SingleOrDefaultAsync(item => item.CustomerId == customerId, ct);
        if (balance is null)
        {
            balance = new CustomerBalance { CustomerId = customerId.Value, OpenedAt = effectiveAt };
            balance.Entries.Add(new CustomerBalanceEntry
            {
                CustomerBalanceId = 0,
                CustomerBalance = balance,
                Type = BalanceEntryType.OpeningBalance,
                OccurredAt = effectiveAt
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
            OccurredAt = effectiveAt,
            CreatedByUserId = actorId
        });
        return balance;
    }

    private static async Task<SupplierBalance> SupplierAdjustmentAsync(AppDbContext db, int supplierId, decimal amount,
        string note, int actorId, DateTimeOffset effectiveAt, CancellationToken ct)
    {
        var balance = db.SupplierBalances.Local.SingleOrDefault(item => item.SupplierId == supplierId)
            ?? await db.SupplierBalances.SingleOrDefaultAsync(item => item.SupplierId == supplierId, ct);
        if (balance is null)
        {
            balance = new SupplierBalance { SupplierId = supplierId, OpenedAt = effectiveAt };
            balance.Entries.Add(new SupplierBalanceEntry
            {
                SupplierBalanceId = 0,
                SupplierBalance = balance,
                Type = BalanceEntryType.OpeningBalance,
                OccurredAt = effectiveAt
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
            OccurredAt = effectiveAt,
            CreatedByUserId = actorId
        });
        return balance;
    }
}
