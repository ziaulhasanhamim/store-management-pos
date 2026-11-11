using System.Data;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POSApp.Client.Security;
using POSApp.Persistence;
using POSApp.Persistence.Entities;

namespace POSApp.Services;

public sealed class DateCorrectionService(IServiceScopeFactory scopes)
{
    public async Task ChangeDateAsync(
        ClaimsPrincipal actor,
        string recordType,
        int recordId,
        DateTimeOffset newTimestamp,
        string reason,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordType);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var normalizedType = (recordType ?? "").Trim().ToLowerInvariant();

        await StoreOperation.RunAsync(async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            await ApplyCorrectionAsync(db, normalizedType, recordType!, recordId, newTimestamp, reason, actorId, ct);

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }, ct);
    }

    public async Task<int> BulkChangeDateAsync(
        ClaimsPrincipal actor,
        string recordType,
        IEnumerable<int> recordIds,
        DateTimeOffset newTimestamp,
        string reason,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordType);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var normalizedType = recordType.Trim().ToLowerInvariant();
        var ids = recordIds.Distinct().ToArray();
        if (ids.Length == 0) return 0;

        return await StoreOperation.RunAsync(async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            int count = 0;
            foreach (var id in ids)
            {
                await ApplyCorrectionAsync(db, normalizedType, recordType, id, newTimestamp, reason, actorId, ct);
                count++;
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return count;
        }, ct);
    }

    private static async Task ApplyCorrectionAsync(
        AppDbContext db,
        string normalizedType,
        string recordType,
        int recordId,
        DateTimeOffset newTimestamp,
        string reason,
        int actorId,
        CancellationToken ct)
    {
        newTimestamp = newTimestamp.ToUniversalTime();
        string beforeStr = "";
        string afterStr = newTimestamp.ToString("yyyy-MM-dd HH:mm:ss zzz");
        var newDateOnly = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(newTimestamp, AccountingCalendar.Zone(null)).DateTime);

        switch (normalizedType)
        {
            case "sale":
            {
                var sale = await db.Sales
                    .Include(s => s.Items)
                    .Include(s => s.Payments)
                    .SingleOrDefaultAsync(s => s.Id == recordId, ct)
                    ?? throw new ValidationException($"Sale #{recordId} not found.");

                beforeStr = sale.SoldAt.ToString("yyyy-MM-dd HH:mm:ss zzz");
                sale.SoldAt = newTimestamp;

                var saleItemIds = sale.Items.Select(i => i.Id).ToList();
                if (saleItemIds.Count > 0)
                {
                    var invMovements = await db.InventoryMovements
                        .Where(m => m.SaleItemId != null && saleItemIds.Contains(m.SaleItemId.Value))
                        .ToListAsync(ct);
                    foreach (var m in invMovements) m.OccurredAt = newTimestamp;
                }

                var paymentIds = sale.Payments.Select(p => p.Id).ToList();
                foreach (var payment in sale.Payments)
                {
                    payment.PaidAt = newTimestamp;
                }

                var customerEntries = await db.CustomerBalanceEntries
                    .Where(e => e.SaleId == recordId || (e.PaymentId != null && paymentIds.Contains(e.PaymentId.Value)))
                    .ToListAsync(ct);
                foreach (var e in customerEntries) e.OccurredAt = newTimestamp;

                if (paymentIds.Count > 0)
                {
                    var cashMovements = await db.CashMovements
                        .Where(m => m.PaymentId != null && paymentIds.Contains(m.PaymentId.Value))
                        .ToListAsync(ct);
                    foreach (var m in cashMovements)
                    {
                        m.CreatedAt = newTimestamp;
                        m.BusinessDate = newDateOnly;
                    }

                    var bankMovements = await db.BankMovements
                        .Where(m => m.PaymentId != null && paymentIds.Contains(m.PaymentId.Value))
                        .ToListAsync(ct);
                    foreach (var m in bankMovements)
                    {
                        m.CreatedAt = newTimestamp;
                        m.BusinessDate = newDateOnly;
                    }
                }

                break;
            }
            case "expense":
            {
                var expense = await db.Expenses.SingleOrDefaultAsync(e => e.Id == recordId, ct)
                    ?? throw new ValidationException($"Expense #{recordId} not found.");

                beforeStr = expense.OccurredAt.ToString("yyyy-MM-dd HH:mm:ss zzz");
                expense.OccurredAt = newTimestamp;

                var cashMovements = await db.CashMovements.Where(m => m.ExpenseId == recordId).ToListAsync(ct);
                foreach (var m in cashMovements)
                {
                    m.CreatedAt = newTimestamp;
                    m.BusinessDate = newDateOnly;
                }

                var bankMovements = await db.BankMovements.Where(m => m.ExpenseId == recordId).ToListAsync(ct);
                foreach (var m in bankMovements)
                {
                    m.CreatedAt = newTimestamp;
                    m.BusinessDate = newDateOnly;
                }

                break;
            }
            case "supplierpayment":
            case "supplier-payment":
            {
                var payment = await db.SupplierPayments.SingleOrDefaultAsync(p => p.Id == recordId, ct)
                    ?? throw new ValidationException($"Supplier payment #{recordId} not found.");

                beforeStr = payment.PaidAt.ToString("yyyy-MM-dd HH:mm:ss zzz");
                payment.PaidAt = newTimestamp;

                var balanceEntries = await db.SupplierBalanceEntries.Where(b => b.SupplierPaymentId == recordId).ToListAsync(ct);
                foreach (var b in balanceEntries) b.OccurredAt = newTimestamp;

                var cashMovements = await db.CashMovements.Where(m => m.SupplierPaymentId == recordId).ToListAsync(ct);
                foreach (var m in cashMovements)
                {
                    m.CreatedAt = newTimestamp;
                    m.BusinessDate = newDateOnly;
                }

                var bankMovements = await db.BankMovements.Where(m => m.SupplierPaymentId == recordId).ToListAsync(ct);
                foreach (var m in bankMovements)
                {
                    m.CreatedAt = newTimestamp;
                    m.BusinessDate = newDateOnly;
                }

                break;
            }
            case "customerpayment":
            case "customer-payment":
            case "payment":
            {
                var payment = await db.Payments.SingleOrDefaultAsync(p => p.Id == recordId, ct)
                    ?? throw new ValidationException($"Payment #{recordId} not found.");

                beforeStr = payment.PaidAt.ToString("yyyy-MM-dd HH:mm:ss zzz");
                payment.PaidAt = newTimestamp;

                var balanceEntries = await db.CustomerBalanceEntries.Where(b => b.PaymentId == recordId).ToListAsync(ct);
                foreach (var b in balanceEntries) b.OccurredAt = newTimestamp;

                var cashMovements = await db.CashMovements.Where(m => m.PaymentId == recordId).ToListAsync(ct);
                foreach (var m in cashMovements)
                {
                    m.CreatedAt = newTimestamp;
                    m.BusinessDate = newDateOnly;
                }

                var bankMovements = await db.BankMovements.Where(m => m.PaymentId == recordId).ToListAsync(ct);
                foreach (var m in bankMovements)
                {
                    m.CreatedAt = newTimestamp;
                    m.BusinessDate = newDateOnly;
                }

                break;
            }
            case "purchase":
            case "receiving":
            {
                var purchase = await db.Purchases
                    .Include(p => p.Items)
                    .SingleOrDefaultAsync(p => p.Id == recordId, ct)
                    ?? throw new ValidationException($"Purchase #{recordId} not found.");

                beforeStr = (purchase.ReceivedAt ?? purchase.OrderedAt).ToString("yyyy-MM-dd HH:mm:ss zzz");
                purchase.OrderedAt = newTimestamp;
                if (purchase.ReceivedAt != null) purchase.ReceivedAt = newTimestamp;

                var purchaseItemIds = purchase.Items.Select(i => i.Id).ToList();
                if (purchaseItemIds.Count > 0)
                {
                    var invMovements = await db.InventoryMovements
                        .Where(m => m.PurchaseItemId != null && purchaseItemIds.Contains(m.PurchaseItemId.Value))
                        .ToListAsync(ct);
                    foreach (var m in invMovements) m.OccurredAt = newTimestamp;
                }

                var supplierEntries = await db.SupplierBalanceEntries
                    .Where(e => e.PurchaseId == recordId)
                    .ToListAsync(ct);
                foreach (var e in supplierEntries) e.OccurredAt = newTimestamp;

                var payments = await db.SupplierPayments.Where(p => p.PurchaseId == recordId).ToListAsync(ct);
                foreach (var p in payments)
                {
                    p.PaidAt = newTimestamp;
                    var cash = await db.CashMovements.Where(m => m.SupplierPaymentId == p.Id).ToListAsync(ct);
                    foreach (var c in cash)
                    {
                        c.CreatedAt = newTimestamp;
                        c.BusinessDate = newDateOnly;
                    }
                    var bank = await db.BankMovements.Where(m => m.SupplierPaymentId == p.Id).ToListAsync(ct);
                    foreach (var b in bank)
                    {
                        b.CreatedAt = newTimestamp;
                        b.BusinessDate = newDateOnly;
                    }
                }

                break;
            }
            case "payroll":
            {
                var payroll = await db.PayrollEntries.SingleOrDefaultAsync(p => p.Id == recordId, ct)
                    ?? throw new ValidationException($"Payroll entry #{recordId} not found.");

                beforeStr = payroll.OccurredAt.ToString("yyyy-MM-dd HH:mm:ss zzz");
                payroll.OccurredAt = newTimestamp;

                var cashMovements = await db.CashMovements.Where(m => m.PayrollEntryId == recordId).ToListAsync(ct);
                foreach (var m in cashMovements)
                {
                    m.CreatedAt = newTimestamp;
                    m.BusinessDate = newDateOnly;
                }

                var bankMovements = await db.BankMovements.Where(m => m.PayrollEntryId == recordId).ToListAsync(ct);
                foreach (var m in bankMovements)
                {
                    m.CreatedAt = newTimestamp;
                    m.BusinessDate = newDateOnly;
                }

                break;
            }
            case "stocktransfer":
            case "stock-transfer":
            case "transfer":
            {
                var transfer = await db.StockTransfers
                    .SingleOrDefaultAsync(t => t.Id == recordId, ct)
                    ?? throw new ValidationException($"Stock transfer #{recordId} not found.");

                beforeStr = transfer.OccurredAt.ToString("yyyy-MM-dd HH:mm:ss zzz");
                transfer.OccurredAt = newTimestamp;

                var invMovements = await db.InventoryMovements
                    .Where(m => m.StockTransferId == transfer.Id)
                    .ToListAsync(ct);
                foreach (var m in invMovements) m.OccurredAt = newTimestamp;

                break;
            }
            case "cashtransfer":
            case "cash-transfer":
            {
                var transfer = await db.CashTransfers.SingleOrDefaultAsync(t => t.Id == recordId, ct)
                    ?? throw new ValidationException($"Cash transfer #{recordId} not found.");

                beforeStr = transfer.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss zzz");
                transfer.BusinessDate = newDateOnly;
                transfer.CreatedAt = newTimestamp;

                var cashMovements = await db.CashMovements.Where(m => m.CashTransferId == recordId).ToListAsync(ct);
                foreach (var m in cashMovements)
                {
                    m.CreatedAt = newTimestamp;
                    m.BusinessDate = newDateOnly;
                }

                break;
            }
            
            default:
                throw new ValidationException($"Unsupported record type '{recordType}'.");
        }

        db.RecordChanges.Add(new RecordChange
        {
            RequestId = Guid.NewGuid(),
            RecordType = recordType,
            RecordId = recordId,
            Action = "EditDate",
            Reason = reason.Trim(),
            Before = beforeStr,
            After = afterStr,
            CreatedByUserId = actorId
        });
    }
}
