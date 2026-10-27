using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;

namespace POSApp.Services;

public sealed class PartyAccountService(IServiceScopeFactory scopes, IConfiguration configuration)
{
    public async Task<PartyPage> ReadAsync(ClaimsPrincipal actor, bool suppliers, int? partyId, CancellationToken ct = default, int? page = null, int pageSize = 30, int? historyPage = null)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
        IQueryable<PartyRow> partiesQuery = suppliers
            ? db.Suppliers.Where(party => !party.IsDeleted).OrderBy(party => party.Name).ThenBy(party => party.Id)
                .Select(party => new PartyRow(party.Id, party.Name, party.Phone, party.Balance == null ? 0 : party.Balance.OpeningBalance,
                    party.Balance == null ? 0 : party.Balance.CurrentBalance))
            : db.Customers.Where(party => !party.IsDeleted).OrderBy(party => party.Name).ThenBy(party => party.Id)
                .Select(party => new PartyRow(party.Id, party.Name, party.Phone, party.Balance == null ? 0 : party.Balance.OpeningBalance,
                    party.Balance == null ? 0 : party.Balance.CurrentBalance));
        var result = page is { } number ? await partiesQuery.ToPageAsync(number, pageSize, ct) : null;
        var parties = result?.Items ?? await partiesQuery.ToArrayAsync(ct);
        IQueryable<PartyEntry> historyQuery = suppliers
            ? db.SupplierBalanceEntries.Where(entry => entry.SupplierBalance!.SupplierId == partyId).OrderByDescending(entry => entry.Id)
                .Select(entry => new PartyEntry(entry.OccurredAt, entry.Type.ToString(), entry.Amount, entry.BalanceAfter, entry.Notes))
            : db.CustomerBalanceEntries.Where(entry => entry.CustomerBalance!.CustomerId == partyId).OrderByDescending(entry => entry.Id)
                .Select(entry => new PartyEntry(entry.OccurredAt, entry.Type.ToString(), entry.Amount, entry.BalanceAfter, entry.Notes));
        var historyResult = historyPage is { } historyNumber ? await historyQuery.ToPageAsync(historyNumber, pageSize, ct) : null;
        var history = historyResult?.Items ?? await historyQuery.Take(100).ToArrayAsync(ct);
        return new(parties, history) { Pagination = result?.Pagination, HistoryPagination = historyResult?.Pagination };
    }

    public async Task ReceiveAsync(ClaimsPrincipal actor, CustomerPaymentInput input, CancellationToken ct = default)
    {
        await StoreOperation.RunAsync(async () =>
        {
            StoreAccess.Money(input.Amount, "Payment");
            CashDrawerService.ValidateNotes(input.Notes);
            if (input.Amount <= 0 || input.RequestId == Guid.Empty || !Enum.IsDefined(input.Method))
            {
                throw new ValidationException("Enter a positive customer payment and a valid method.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var hash = StoreOperation.Fingerprint(input);
            var reference = $"CUSTOMER-PAY-{input.RequestId:N}";
            var previousPayment = await db.Payments.SingleOrDefaultAsync(payment => payment.Reference == reference, ct);
            if (previousPayment is not null)
            {
                StoreOperation.CheckReplay(actorId, previousPayment.CreatedByUserId, previousPayment.RequestHash, hash);
                return;
            }
            if (!await db.Customers.AnyAsync(customer => customer.Id == input.CustomerId && !customer.IsDeleted, ct))
            {
                throw new ValidationException("Choose a customer.");
            }
            var date = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
            AccountingCalendar.ValidateExpected(date, WorkingDayRequest.Date);
            await AccountingCalendar.EnsureOpenAsync(db, date, ct);
            var at = AccountingCalendar.At(date, configuration);
            var balance = await db.CustomerBalances.SingleOrDefaultAsync(balance => balance.CustomerId == input.CustomerId, ct);
            if (balance is null)
            {
                balance = new CustomerBalance { CustomerId = input.CustomerId,
                OpenedAt = at };
                db.CustomerBalances.Add(balance);
            }
            balance.CurrentBalance -= input.Amount;
            StoreAccess.Money(balance.CurrentBalance, "Customer balance", negative: true);
            balance.UpdatedAt = DateTimeOffset.UtcNow;
            balance.UpdatedByUserId = actorId;
            var payment = new Payment
            {
                RequestHash = hash,
                CustomerBalance = balance,
                Amount = input.Amount,
                TenderedAmount = input.Amount,
                Method = input.Method,
                PaidAt = at,
                Reference = reference,
                Notes = input.Notes.Trim(),
                CreatedByUserId = actorId
            };
            db.Payments.Add(payment);
            balance.Entries.Add(new CustomerBalanceEntry
            {
                CustomerBalanceId = balance.Id,
                CustomerBalance = balance,
                Type = BalanceEntryType.Payment,
                Payment = payment,
                Amount = -input.Amount,
                BalanceAfter = balance.CurrentBalance,
                OccurredAt = at,
                Notes = input.Notes.Trim(),
                CreatedByUserId = actorId
            });
            await FinancialPosting.PostAsync(db, actorId, input.Method, input.Amount, CashMovementKind.Sale,
                $"Customer collection · #{input.CustomerId}", date, ct, payment: payment);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }, ct);
    }

    public async Task ChangeAsync(ClaimsPrincipal actor, bool suppliers, BalanceAdjustmentInput input, CancellationToken ct = default)
    {
        await StoreOperation.RunAsync(async () =>
        {
            CashDrawerService.ValidateNotes(input.Reason);
            StoreAccess.Money(input.Amount, "Balance adjustment", negative: true);
            if (input.RequestId == Guid.Empty)
            {
                throw new ValidationException("Reload and try again.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var hash = StoreOperation.Fingerprint(input);
            var date = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
            AccountingCalendar.ValidateExpected(date, WorkingDayRequest.Date);
            await AccountingCalendar.EnsureOpenAsync(db, date, ct);
            var at = AccountingCalendar.At(date, configuration);
            var note = (input.Opening ? "Opening balance correction: " : "Due adjustment: ") + input.Reason.Trim();
            if (note.Length > 2000)
            {
                throw new ValidationException("Shorten the adjustment reason.");
            }
            if (suppliers)
            {
                var previous = await db.SupplierBalanceEntries.SingleOrDefaultAsync(entry => entry.RequestId == input.RequestId, ct);
                if (previous is not null)
                {
                    StoreOperation.CheckReplay(actorId, previous.CreatedByUserId, previous.RequestHash, hash);
                    return;
                }
                if (!await db.Suppliers.AnyAsync(party => party.Id == input.PartyId && !party.IsDeleted, ct))
                {
                    throw new ValidationException("Supplier not found.");
                }
                var balance = await db.SupplierBalances.SingleOrDefaultAsync(balance => balance.SupplierId == input.PartyId, ct);
                if (balance is null)
                {
                    balance = new SupplierBalance { SupplierId = input.PartyId,
                    OpenedAt = at };
                    db.SupplierBalances.Add(balance);
                }
                var amount = input.Opening ? input.Amount - balance.OpeningBalance : input.Amount;
                if (input.Opening)
                {
                    balance.OpeningBalance = input.Amount;
                }
                balance.CurrentBalance += amount;
                StoreAccess.Money(balance.CurrentBalance, "Supplier balance", negative: true);
                balance.UpdatedAt = DateTimeOffset.UtcNow;
                balance.UpdatedByUserId = actorId;
                balance.Entries.Add(new SupplierBalanceEntry
                {
                    SupplierBalanceId = balance.Id,
                    SupplierBalance = balance,
                    RequestId = input.RequestId,
                    RequestHash = hash,
                    Type = input.Opening ? BalanceEntryType.OpeningBalance : BalanceEntryType.Adjustment,
                    Amount = amount,
                    BalanceAfter = balance.CurrentBalance,
                    Notes = note,
                    OccurredAt = at,
                    CreatedByUserId = actorId
                });
            }
            else
            {
                var previous = await db.CustomerBalanceEntries.SingleOrDefaultAsync(entry => entry.RequestId == input.RequestId, ct);
                if (previous is not null)
                {
                    StoreOperation.CheckReplay(actorId, previous.CreatedByUserId, previous.RequestHash, hash);
                    return;
                }
                if (!await db.Customers.AnyAsync(party => party.Id == input.PartyId && !party.IsDeleted, ct))
                {
                    throw new ValidationException("Customer not found.");
                }
                var balance = await db.CustomerBalances.SingleOrDefaultAsync(balance => balance.CustomerId == input.PartyId, ct);
                if (balance is null)
                {
                    balance = new CustomerBalance { CustomerId = input.PartyId,
                    OpenedAt = at };
                    db.CustomerBalances.Add(balance);
                }
                var amount = input.Opening ? input.Amount - balance.OpeningBalance : input.Amount;
                if (input.Opening)
                {
                    balance.OpeningBalance = input.Amount;
                }
                balance.CurrentBalance += amount;
                StoreAccess.Money(balance.CurrentBalance, "Customer balance", negative: true);
                balance.UpdatedAt = DateTimeOffset.UtcNow;
                balance.UpdatedByUserId = actorId;
                balance.Entries.Add(new CustomerBalanceEntry
                {
                    CustomerBalanceId = balance.Id,
                    CustomerBalance = balance,
                    RequestId = input.RequestId,
                    RequestHash = hash,
                    Type = input.Opening ? BalanceEntryType.OpeningBalance : BalanceEntryType.Adjustment,
                    Amount = amount,
                    BalanceAfter = balance.CurrentBalance,
                    Notes = note,
                    OccurredAt = at,
                    CreatedByUserId = actorId
                });
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }, ct);
    }
}
