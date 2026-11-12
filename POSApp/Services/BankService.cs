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
        var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
        var workingDate = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
        var monthStart = new DateOnly(workingDate.Year, workingDate.Month, 1);
        var account = await db.BankAccounts.AsNoTracking().SingleOrDefaultAsync(account => account.Code == "MAIN", ct);
        var rawHistory = await db.BankMovements.OrderByDescending(move => move.Id).Take(100)
            .Select(move => new { move.OccurredAt, Kind = move.Kind.ToString(), move.Method, move.Amount, move.BalanceAfter, move.Notes }).ToArrayAsync(ct);
        var history = rawHistory
            .Select(move => new BankRow(DateOnly.FromDateTime(move.OccurredAt.ToBusinessTime(configuration).DateTime), move.Kind, move.Method, move.Amount, move.BalanceAfter, move.Notes)).ToArray();

        var monthStartUtc = AccountingCalendar.At(monthStart, TimeOnly.MinValue, configuration);
        decimal openingBeforeMonth = (account?.OpeningDate is not null && account.OpeningDate <= monthStart) ? account.OpeningBalance : 0m;
        decimal movementsBeforeMonth = await db.BankMovements.AsNoTracking()
            .Where(move => move.OccurredAt < monthStartUtc && move.Kind != CashMovementKind.OpeningBalance
                && move.ReversesMovementId == null
                && !db.BankMovements.Any(r => r.ReversesMovementId == move.Id))
            .SumAsync(move => (decimal?)move.Amount, ct) ?? 0m;
        decimal monthOpening = openingBeforeMonth + movementsBeforeMonth;

        return new(account?.OpeningBalance ?? 0, account?.CurrentBalance ?? 0, account?.OpeningDate, history, monthOpening, monthStart);
    }

    public async Task SaveAsync(ClaimsPrincipal actor, BankInput input, CancellationToken ct = default)
    {
        await StoreOperation.RunAsync(async () =>
        {
            StoreAccess.Money(input.Amount, "Amount", negative: input.Action is "opening" or "adjustment");
            CashDrawerService.ValidateNotes(input.Notes);
            if (input.RequestId == Guid.Empty)
            {
                input.RequestId = Guid.NewGuid();
            }
            if (input.Action is not ("opening" or "adjustment" or "deposit" or "withdraw" or "contribution" or "owner-withdrawal")
                || (input.Action is "deposit" or "withdraw" or "contribution" or "owner-withdrawal" && input.Amount <= 0))
            {
                throw new ValidationException("Enter a valid bank movement.");
            }
            if (!input.IsBackdate || input.Action is not ("deposit" or "withdraw"))
            {
                input.OccurredAt = null;
            }
            if (input.IsBackdate && input.Action is "deposit" or "withdraw")
            {
                if (!input.OccurredAt.HasValue)
                {
                    throw new ValidationException("Select the date and time for the transfer.");
                }
                var effective = input.OccurredAt.Value.ToUniversalTime();
                if (effective > DateTimeOffset.UtcNow)
                {
                    throw new ValidationException("Transfer date and time cannot be in the future.");
                }
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var hash = StoreOperation.Fingerprint(new { input.RequestId, input.Amount, input.Action, input.Notes, input.IsBackdate, input.OccurredAt });
            var previous = await db.BankMovements.SingleOrDefaultAsync(move => move.RequestId == input.RequestId, ct);
            if (previous is not null)
            {
                if (previous.CreatedByUserId == actorId && previous.RequestHash == hash)
                {
                    // Exact duplicate retry - idempotent return
                    return;
                }
                // Resubmitted form or reused RequestId with different values: assign a fresh RequestId so the new movement succeeds
                input.RequestId = Guid.NewGuid();
                hash = StoreOperation.Fingerprint(new { input.RequestId, input.Amount, input.Action, input.Notes, input.IsBackdate, input.OccurredAt });
            }
            var effectiveAt = (input.IsBackdate && input.OccurredAt.HasValue)
                ? input.OccurredAt.Value.ToUniversalTime()
                : DateTimeOffset.UtcNow;
            var date = (input.IsBackdate && input.OccurredAt.HasValue)
                ? DateOnly.FromDateTime(effectiveAt.ToBusinessTime(configuration).DateTime)
                : await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
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
                var drawer = await CashDrawerService.RequireDrawerAsync(db, actorId, ct);
                CashDrawerService.Append(db, drawer, new CashMovement
                {
                    RequestId = Guid.NewGuid(),
                    OccurredAt = effectiveAt,
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
                OccurredAt = effectiveAt,
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
