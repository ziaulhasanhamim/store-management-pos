using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;

namespace POSApp.Services;

public sealed class DebtService(IServiceScopeFactory scopes, IConfiguration configuration)
{
    public async Task<DebtPage> ReadAsync(ClaimsPrincipal actor, int? lenderId = null, CancellationToken ct = default, int? page = null, int pageSize = 30, int? historyPage = null)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
        var date = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
        var lenderQuery = db.Lenders.AsNoTracking().OrderBy(lender => lender.Name).ThenBy(lender => lender.Id)
            .Select(lender => new LenderRow(lender.Id, lender.Name, lender.Phone,
                lender.Balance!.OpeningBalance, lender.Balance.CurrentBalance));
        var lenderPage = page is { } number ? await lenderQuery.ToPageAsync(number, pageSize, ct) : null;
        var lenders = lenderPage?.Items ?? await lenderQuery.ToArrayAsync(ct);
        var outstanding = await db.LenderBalances.Select(balance => (decimal?)balance.CurrentBalance).SumAsync(ct) ?? 0;
        if (lenderId is not null && !await db.Lenders.AnyAsync(lender => lender.Id == lenderId, ct))
        {
            throw new ValidationException("Lender not found.");
        }
        var locations = await db.StockLocations.Where(location => location.CanSell && !location.IsDeleted)
            .OrderBy(location => location.Name)
            .Select(location => new DebtLocation(location.Id, location.Name)).ToArrayAsync(ct);
        var month = new DateOnly(date.Year, date.Month, 1);
        var closed = await db.FinanceClosings.AnyAsync(closing => closing.ClosedAt != null
            && (closing.Kind == ClosingKind.Day && closing.Date == date
                || closing.Kind == ClosingKind.Month && closing.Date == month), ct);
        var today = await db.DebtEntries.Where(entry => entry.BusinessDate == date)
            .Select(entry => new { entry.Kind, entry.Amount }).ToArrayAsync(ct);
        var historyQuery = db.DebtEntries.AsNoTracking()
            .Where(entry => lenderId == null || entry.LenderBalance!.LenderId == lenderId)
            .OrderByDescending(entry => entry.BusinessDate).ThenByDescending(entry => entry.Id)
            .Select(entry => new DebtHistoryRow(entry.Id, entry.BusinessDate, entry.LenderBalance!.LenderId,
                entry.LenderBalance.Lender!.Name, entry.Kind, entry.Amount, entry.BalanceAfter, entry.Method,
                entry.CashLocation == null ? null : entry.CashLocation.Name, entry.Notes));
        var historyResult = historyPage is { } historyNumber
            ? await historyQuery.ToPageAsync(historyNumber, pageSize, ct) : null;
        var history = historyResult?.Items ?? await historyQuery.Take(200).ToArrayAsync(ct);
        return new(date, !closed, lenders, locations,
            today.Where(entry => entry.Kind == DebtEntryKind.Borrowing).Sum(entry => entry.Amount),
            -today.Where(entry => entry.Kind == DebtEntryKind.Repayment).Sum(entry => entry.Amount), history)
        {
            LenderPagination = lenderPage?.Pagination,
            HistoryPagination = historyResult?.Pagination,
            TotalOutstanding = outstanding
        };
    }

    public async Task<int> AddLenderAsync(ClaimsPrincipal actor, LenderInput input, CancellationToken ct = default)
    {
        return await StoreOperation.RunAsync<int>(async () =>
        {
            var name = input.Name?.Trim() ?? "";
            var phone = input.Phone?.Trim();
            StoreAccess.Money(input.OpeningBalance, "Opening debt");
            if (input.RequestId == Guid.Empty || name.Length is 0 or > 200 || phone?.Length > 64)
            {
                throw new ValidationException("Enter a lender name up to 200 characters and a phone number up to 64 characters.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var previous = await db.Lenders.Include(lender => lender.Balance)
                .SingleOrDefaultAsync(lender => lender.RequestId == input.RequestId, ct);
            if (previous is not null)
            {
                if (previous.CreatedByUserId != actorId || previous.Name != name || previous.Phone != phone
                    || previous.Balance!.OpeningBalance != input.OpeningBalance || previous.Balance.OpeningDate != input.WorkingDate)
                {
                    throw new ValidationException("This request was already saved with different values. Reload before adding another lender.");
                }
                return previous.Id;
            }
            var date = await RequireDayAsync(db, actorId, input.WorkingDate, ct);
            var balance = new LenderBalance
            {
                OpeningDate = date,
                OpeningBalance = input.OpeningBalance,
                CurrentBalance = input.OpeningBalance,
                CreatedByUserId = actorId
            };
            var lender = new Lender
            {
                RequestId = input.RequestId,
                Name = name,
                NormalizedName = NameNormalization.Normalize(name),
                Phone = phone,
                Balance = balance,
                CreatedByUserId = actorId
            };
            db.Lenders.Add(lender);
            db.DebtEntries.Add(new DebtEntry
            {
                RequestId = Guid.NewGuid(),
                LenderBalance = balance,
                BusinessDate = date,
                Kind = DebtEntryKind.OpeningBalance,
                Amount = input.OpeningBalance,
                BalanceAfter = input.OpeningBalance,
                Notes = "Opening debt carried into system",
                CreatedByUserId = actorId
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return lender.Id;
        }, ct);
    }

    public async Task<int> PostAsync(ClaimsPrincipal actor, DebtPaymentInput input, CancellationToken ct = default)
    {
        return await StoreOperation.RunAsync<int>(async () =>
        {
            StoreAccess.Money(input.Amount, "Amount");
            var notes = input.Notes?.Trim() ?? "";
            if (input.RequestId == Guid.Empty || input.Amount <= 0 || !Enum.IsDefined(input.Method)
                || input.Kind is not (DebtEntryKind.Borrowing or DebtEntryKind.Repayment) || notes.Length > 2000
                || input.Method == PaymentMethod.Cash && input.CashLocationId is null)
            {
                throw new ValidationException("Choose a lender, borrowing or repayment, a positive amount, and a cash location for cash payments.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var amount = input.Kind == DebtEntryKind.Repayment ? -input.Amount : input.Amount;
            var locationId = input.Method == PaymentMethod.Cash ? input.CashLocationId : null;
            var previous = await db.DebtEntries.Include(entry => entry.LenderBalance)
                .SingleOrDefaultAsync(entry => entry.RequestId == input.RequestId, ct);
            if (previous is not null)
            {
                if (previous.CreatedByUserId != actorId || previous.LenderBalance!.LenderId != input.LenderId
                    || previous.Kind != input.Kind || previous.Amount != amount || previous.Method != input.Method
                    || previous.BusinessDate != input.WorkingDate || previous.CashLocationId != locationId || previous.Notes != notes)
                {
                    throw new ValidationException("This request was already saved with different values. Reload before recording another payment.");
                }
                return previous.Id;
            }
            var date = await RequireDayAsync(db, actorId, input.WorkingDate, ct);
            var balance = await db.LenderBalances.Include(balance => balance.Lender)
                .SingleOrDefaultAsync(balance => balance.LenderId == input.LenderId, ct)
                ?? throw new ValidationException("Choose an existing lender.");
            if (balance.CurrentBalance + amount < 0)
            {
                throw new ValidationException($"Repayment exceeds the outstanding debt of {balance.CurrentBalance:N2}.");
            }
            balance.CurrentBalance += amount;
            StoreAccess.Money(balance.CurrentBalance, "Outstanding debt");
            balance.UpdatedAt = DateTimeOffset.UtcNow;
            balance.UpdatedByUserId = actorId;
            var entry = new DebtEntry
            {
                RequestId = input.RequestId,
                LenderBalance = balance,
                BusinessDate = date,
                Kind = input.Kind,
                Amount = amount,
                BalanceAfter = balance.CurrentBalance,
                Method = input.Method,
                CashLocationId = locationId,
                Notes = notes,
                CreatedByUserId = actorId
            };
            db.DebtEntries.Add(entry);
            await FinancialPosting.PostAsync(db, actorId, input.Method, amount,
                input.Kind == DebtEntryKind.Borrowing ? CashMovementKind.DebtBorrowing : CashMovementKind.DebtRepayment,
                $"{input.Kind} · {balance.Lender!.Name}", date, ct,
                cashLocationId: locationId, periodValidated: true, debtEntry: entry);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return entry.Id;
        }, ct);
    }

    internal static async Task<decimal> OutstandingBeforeAsync(AppDbContext db, DateOnly before, CancellationToken ct)
    {
        return await db.DebtEntries.Where(entry => entry.BusinessDate < before)
            .Select(entry => (decimal?)entry.Amount).SumAsync(ct) ?? 0;
    }

    private async Task<DateOnly> RequireDayAsync(AppDbContext db, int actorId, DateOnly requested, CancellationToken ct)
    {
        var date = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
        if (requested != date)
        {
            throw new ValidationException("The working day changed. Reload this page before recording debt.");
        }
        await AccountingCalendar.EnsureOpenAsync(db, date, ct);
        return date;
    }
}
