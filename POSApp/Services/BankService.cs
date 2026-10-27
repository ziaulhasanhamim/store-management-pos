using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;

namespace POSApp.Services;

public sealed class BankService(IServiceScopeFactory scopes, IConfiguration configuration)
{
    public async Task<BankPage> ReadAsync(ClaimsPrincipal actor, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
        var account = await db.BankAccounts.AsNoTracking().SingleOrDefaultAsync(account => account.Code == "MAIN", ct);
        var history = await db.BankMovements.OrderByDescending(move => move.Id).Take(100)
            .Select(move => new BankRow(move.BusinessDate, move.Kind.ToString(), move.Method, move.Amount, move.BalanceAfter, move.Notes)).ToArrayAsync(ct);
        return new(account?.OpeningBalance ?? 0, account?.CurrentBalance ?? 0, account?.OpeningDate, history);
    }

    public async Task SaveAsync(ClaimsPrincipal actor, BankInput input, CancellationToken ct = default)
    {
        await StoreOperation.RunAsync(async () =>
        {
            StoreAccess.Money(input.Amount, "Amount", negative: input.Action is "opening" or "adjustment");
            CashDrawerService.ValidateNotes(input.Notes);
            if (input.RequestId == Guid.Empty || input.Action is not ("opening" or "adjustment" or "deposit" or "withdraw" or "contribution" or "owner-withdrawal")
                || (input.Action is "deposit" or "withdraw" or "contribution" or "owner-withdrawal" && input.Amount <= 0))
            {
                throw new ValidationException("Enter a valid bank movement.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var hash = StoreOperation.Fingerprint(new { input.RequestId, input.Amount, input.Action, input.Notes });
            var previous = await db.BankMovements.SingleOrDefaultAsync(move => move.RequestId == input.RequestId, ct);
            if (previous is not null)
            {
                StoreOperation.CheckReplay(actorId, previous.CreatedByUserId, previous.RequestHash, hash);
                return;
            }
            var date = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
            AccountingCalendar.ValidateExpected(date, WorkingDayRequest.Date);
            await AccountingCalendar.EnsureOpenAsync(db, date, ct);
            var account = await db.BankAccounts.SingleOrDefaultAsync(account => account.Code == "MAIN", ct);
            if (input.Action == "opening" && account?.OpeningDate is not null)
            {
                throw new ValidationException("The bank opening is already recorded. Use an adjustment for corrections.");
            }
            var amount = input.Action is "withdraw" or "owner-withdrawal" ? -input.Amount : input.Amount;
            var kind = input.Action switch
            {
                "opening" => CashMovementKind.OpeningBalance,
                "adjustment" => CashMovementKind.CashIn,
                "contribution" => CashMovementKind.OwnerContribution,
                "owner-withdrawal" => CashMovementKind.OwnerWithdrawal,
                _ => CashMovementKind.Transfer
            };
            if (input.Action is "deposit" or "withdraw")
            {
                var session = await CashDrawerService.RequireOpenAsync(db, actorId, ct);
                CashDrawerService.Append(db, session, new CashMovement
                {
                    RequestId = Guid.NewGuid(),
                    BusinessDate = date,
                    Kind = CashMovementKind.Transfer,
                    Amount = -amount,
                    Notes = input.Notes.Trim(),
                    CreatedByUserId = actorId
                });
            }
            await FinancialPosting.BankAsync(db, new BankMovement
            {
                RequestId = input.RequestId,
                RequestHash = hash,
                BusinessDate = date,
                Kind = kind,
                Method = PaymentMethod.BankTransfer,
                Amount = amount,
                Notes = input.Notes.Trim(),
                CreatedByUserId = actorId
            }, ct);
            if (input.Action == "opening")
            {
                account = db.BankAccounts.Local.Single(account => account.Code == "MAIN");
                account.OpeningBalance = input.Amount;
                account.OpeningDate = date;
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }, ct);
    }
}
