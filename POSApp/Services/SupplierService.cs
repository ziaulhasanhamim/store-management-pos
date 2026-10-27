using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;
using POSApp.Persistence.Entities;

namespace POSApp.Services;

public sealed class SupplierService(IServiceScopeFactory scopes, IConfiguration? configuration = null)
{
    public async Task<IReadOnlyList<SupplierRow>> ListAsync(ClaimsPrincipal actor, string? search = null, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
        return await DirectoryQuery(db, search).Take(10001).ToArrayAsync(ct);
    }

    public async Task<PagedResult<SupplierRow>> PageAsync(ClaimsPrincipal actor, string? search = null,
        int page = 1, int pageSize = 30, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
        return await DirectoryQuery(db, search).ToPageAsync(page, pageSize, ct);
    }

    private static IQueryable<SupplierRow> DirectoryQuery(AppDbContext db, string? search)
    {
        var query = db.Suppliers.AsNoTracking().Where(supplier => !supplier.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = NameNormalization.Normalize(search);
            query = query.Where(supplier => supplier.NormalizedName.Contains(term));
        }
        return query.OrderBy(supplier => supplier.Id)
            .Select(supplier => new SupplierRow(supplier.Id, supplier.Name, supplier.Phone,
                supplier.Balance == null ? 0 : supplier.Balance.OpeningBalance,
                supplier.Balance == null ? 0 : supplier.Balance.CurrentBalance,
                db.Products.Where(product => product.SupplierId == supplier.Id)
                    .Select(product => (decimal?)(product.CostPrice * (product.InventoryBalances.Select(stock => (decimal?)stock.QuantityOnHand).Sum() ?? 0))).Sum() ?? 0));
    }

    public async Task<SupplierDetail?> DetailAsync(ClaimsPrincipal actor, int id, CancellationToken ct = default, int page = 1, int pageSize = 30)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
        var supplier = await db.Suppliers.AsNoTracking()
            .Where(item => item.Id == id && !item.IsDeleted)
            .Select(item => new SupplierRow(item.Id, item.Name, item.Phone,
                item.Balance == null ? 0 : item.Balance.OpeningBalance,
                item.Balance == null ? 0 : item.Balance.CurrentBalance,
                db.Products.Where(product => product.SupplierId == item.Id)
                    .Select(product => (decimal?)(product.CostPrice * (product.InventoryBalances.Select(stock => (decimal?)stock.QuantityOnHand).Sum() ?? 0))).Sum() ?? 0))
            .SingleOrDefaultAsync(ct);
        if (supplier is null)
        {
            return null;
        }
        var entries = await db.SupplierBalanceEntries.AsNoTracking()
            .Where(entry => entry.SupplierBalance!.SupplierId == id).OrderByDescending(entry => entry.Id)
            .Select(entry => new SupplierLedgerRow(entry.OccurredAt, entry.Type.ToString(), entry.Amount, entry.BalanceAfter, entry.Notes))
            .ToPageAsync(page, pageSize, ct);
        return new(supplier, entries.Items) { Pagination = entries.Pagination };
    }

    public async Task<IReadOnlyList<SupplierPaymentRow>> PaymentsAsync(ClaimsPrincipal actor, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
        return await db.SupplierPayments.AsNoTracking().Where(payment => payment.Purchase == null || !payment.Purchase.IsDeleted).OrderByDescending(payment => payment.Id).Take(100)
            .Select(payment => new SupplierPaymentRow(payment.Id, payment.SupplierBalance!.Supplier!.Name,
                payment.PaidAt, payment.Amount, payment.Method, payment.Reference)).ToListAsync(ct);
    }

    public async Task<int> PayAsync(ClaimsPrincipal actor, SupplierPaymentInput input, CancellationToken ct = default)
    {
        return await StoreOperation.RunAsync<int>(async () =>
        {
            Validator.ValidateObject(input, new ValidationContext(input), true);
            StoreAccess.Money(input.Amount, "Payment");
            if (input.RequestId == Guid.Empty || input.Amount <= 0 || !Enum.IsDefined(input.Method))
            {
                throw new ValidationException("Enter a positive payment and a valid payment method.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var hash = StoreOperation.Fingerprint(input);
            var reference = $"PAY-{input.RequestId:N}";
            var previous = await db.SupplierPayments.SingleOrDefaultAsync(payment => payment.Reference == reference, ct);
            if (previous is not null)
            {
                StoreOperation.CheckReplay(actorId, previous.CreatedByUserId, previous.RequestHash, hash);
                return previous.Id;
            }
            var businessDate = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
            AccountingCalendar.ValidateExpected(businessDate, WorkingDayRequest.Date);
            await AccountingCalendar.EnsureOpenAsync(db, businessDate, ct);
            var effectiveAt = AccountingCalendar.At(businessDate, configuration);

            if (!await db.Suppliers.AnyAsync(supplier => supplier.Id == input.SupplierId && !supplier.IsDeleted, ct))
            {
                throw new ValidationException("Choose an active supplier.");
            }
            var now = DateTimeOffset.UtcNow;
            var balance = await db.SupplierBalances.SingleOrDefaultAsync(item => item.SupplierId == input.SupplierId, ct);
            if (balance is null)
            {
                balance = new SupplierBalance { SupplierId = input.SupplierId, OpenedAt = effectiveAt };
                db.SupplierBalances.Add(balance);
                balance.Entries.Add(new SupplierBalanceEntry
                {
                    SupplierBalanceId = 0,
                    SupplierBalance = balance,
                    Type = BalanceEntryType.OpeningBalance,
                    OccurredAt = effectiveAt
                });
            }
            balance.CurrentBalance -= input.Amount;
            StoreAccess.Money(balance.CurrentBalance, "Supplier balance", negative: true);
            balance.UpdatedAt = now;
            balance.UpdatedByUserId = actorId;
            var payment = new SupplierPayment
            {
                RequestHash = hash,
                SupplierBalanceId = balance.Id,
                SupplierBalance = balance,
                Amount = input.Amount,
                Method = input.Method,
                PaidAt = effectiveAt,
                Reference = reference,
                Notes = input.Notes,
                CreatedByUserId = actorId
            };
            db.SupplierPayments.Add(payment);
            await FinancialPosting.PostAsync(db, actorId, input.Method, -input.Amount,
                CashMovementKind.SupplierPayment, $"Supplier payment · #{input.SupplierId}", businessDate, ct, supplierPayment: payment);
            payment.PaidAt = effectiveAt;
            balance.Entries.Add(new SupplierBalanceEntry
            {
                SupplierBalanceId = balance.Id,
                SupplierBalance = balance,
                Type = BalanceEntryType.Payment,
                Amount = -input.Amount,
                BalanceAfter = balance.CurrentBalance,
                SupplierPayment = payment,
                OccurredAt = effectiveAt,
                Notes = input.Notes,
                CreatedByUserId = actorId
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return payment.Id;
        }, ct);
    }
}
