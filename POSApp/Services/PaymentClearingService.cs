using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Client.Security;
using POSApp.Persistence;
using POSApp.Persistence.Entities;

namespace POSApp.Services;

public sealed class PaymentClearingService(IServiceScopeFactory scopes, IConfiguration configuration)
{
    public async Task<PaymentClearingPageData> GetPageDataAsync(
        ClaimsPrincipal actor, int? locationId = null, PaymentMethod? method = null, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);

        var locations = await db.StockLocations.AsNoTracking()
            .Where(l => !l.IsDeleted && l.CanSell)
            .OrderBy(l => l.Name)
            .Select(l => new StockLocationOption(l.Id, l.Name))
            .ToListAsync(ct);

        var summary = await GetSummaryAsync(actor, locationId, ct);

        var allPending = new List<ClearingSaleItemDto>();
        var locsToQuery = locationId.HasValue && locationId.Value > 0
            ? locations.Where(l => l.Id == locationId.Value)
            : locations;

        foreach (var loc in locsToQuery)
        {
            if (method == null || method == PaymentMethod.Card)
            {
                allPending.AddRange(await GetUnsettledSalesAsync(actor, loc.Id, PaymentMethod.Card, ct));
            }
            if (method == null || method == PaymentMethod.MobileWallet)
            {
                allPending.AddRange(await GetUnsettledSalesAsync(actor, loc.Id, PaymentMethod.MobileWallet, ct));
            }
        }
        var pendingSales = allPending.OrderBy(s => s.BusinessDate).ThenBy(s => s.Id).ToList();
        var history = await GetPayoutHistoryAsync(actor, locationId > 0 ? locationId : null, limit: 50, ct);
        var ageing = await GetAgeingReportAsync(actor, ct);
        var feeTrends = await GetFeeAnalyticsAsync(actor, ct: ct);
        var workingDate = AccountingCalendar.Today(configuration);
        var canManage = actor.IsInRole(AppRoles.Admin);

        return new PaymentClearingPageData(
            summary,
            locations,
            pendingSales,
            history,
            ageing,
            feeTrends,
            workingDate,
            canManage);
    }

    public async Task<ClearingSummaryDto> GetSummaryAsync(ClaimsPrincipal actor, int? locationId = null, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);

        var query = db.PaymentClearingEntries.AsNoTracking()
            .Where(e => e.Status == PaymentClearingStatus.Unsettled
                && (locationId == null || e.StockLocationId == locationId.Value)
                && (e.Payment == null || (!e.Payment.Sale!.IsDeleted && e.Payment.Sale.Status != SaleStatus.Voided)));

        var entries = await query.Select(e => new { e.Method, e.GrossAmount, e.BusinessDate }).ToListAsync(ct);

        var today = AccountingCalendar.Today(configuration);
        var cardAwaiting = entries.Where(e => e.Method == PaymentMethod.Card).Sum(e => e.GrossAmount);
        var walletAwaiting = entries.Where(e => e.Method == PaymentMethod.MobileWallet).Sum(e => e.GrossAmount);
        var totalAwaiting = cardAwaiting + walletAwaiting;
        var oldest = entries.Count > 0 ? entries.Min(e => e.BusinessDate) : (DateOnly?)null;

        var overdueEntries = entries.Where(e => today.DayNumber - e.BusinessDate.DayNumber > 7).ToList();
        var overdueCount = overdueEntries.Count;
        var overdueAmount = overdueEntries.Sum(e => e.GrossAmount);

        return new ClearingSummaryDto(
            cardAwaiting,
            walletAwaiting,
            totalAwaiting,
            entries.Count,
            oldest,
            overdueCount,
            overdueAmount);
    }

    public async Task<IReadOnlyList<ClearingSaleItemDto>> GetUnsettledSalesAsync(
        ClaimsPrincipal actor, int locationId, PaymentMethod method, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);

        var today = AccountingCalendar.Today(configuration);

        return await db.PaymentClearingEntries.AsNoTracking()
            .Include(e => e.Payment)
            .Include(e => e.StockLocation)
            .Where(e => e.Status == PaymentClearingStatus.Unsettled
                && e.StockLocationId == locationId
                && e.Method == method
                && (e.Payment == null || (!e.Payment.Sale!.IsDeleted && e.Payment.Sale.Status != SaleStatus.Voided)))
            .OrderBy(e => e.BusinessDate)
            .ThenBy(e => e.Id)
            .Select(e => new ClearingSaleItemDto(
                e.Id,
                e.PaymentId,
                e.Payment != null ? e.Payment.SaleId : null,
                e.BusinessDate,
                e.Payment != null ? e.Payment.PaidAt : e.CreatedAt,
                e.StockLocationId ?? locationId,
                e.StockLocation != null ? e.StockLocation.Name : "Store",
                e.Method,
                e.GrossAmount,
                today.DayNumber - e.BusinessDate.DayNumber))
            .ToListAsync(ct);
    }

    public async Task<int> RecordPayoutAsync(ClaimsPrincipal actor, RecordPayoutInput input, CancellationToken ct = default)
    {
        return await StoreOperation.RunAsync<int>(async () =>
        {
            if (input.StockLocationId <= 0)
            {
                throw new ValidationException("Select a store location for the payout.");
            }
            if (input.ClearingEntryIds.Count == 0)
            {
                throw new ValidationException("Select at least one transaction to clear.");
            }
            StoreAccess.Money(input.GrossAmount, "Gross amount");
            StoreAccess.Money(input.NetAmount, "Net amount");
            if (input.NetAmount <= 0 || input.GrossAmount <= 0)
            {
                throw new ValidationException("Gross and net amounts must be greater than zero.");
            }
            if (input.NetAmount > input.GrossAmount)
            {
                throw new ValidationException("Net amount received in bank cannot exceed gross cleared amount.");
            }
            var fee = input.GrossAmount - input.NetAmount;

            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);

            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);

            var location = await db.StockLocations.FindAsync([input.StockLocationId], ct)
                ?? throw new ValidationException("Store location not found.");

            var workingDate = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
            AccountingCalendar.ValidateExpected(workingDate, WorkingDayRequest.Date);
            await AccountingCalendar.EnsureOpenAsync(db, workingDate, ct);

            var entries = await db.PaymentClearingEntries
                .Include(e => e.Payment)
                    .ThenInclude(p => p!.Sale)
                .Where(e => input.ClearingEntryIds.Contains(e.Id))
                .ToListAsync(ct);

            if (entries.Count != input.ClearingEntryIds.Count)
            {
                throw new ValidationException("One or more selected transactions could not be found.");
            }

            foreach (var entry in entries)
            {
                if (entry.Status != PaymentClearingStatus.Unsettled)
                {
                    throw new ValidationException($"Transaction #{entry.Id} is already settled or reversed.");
                }
                if (entry.Payment?.Sale is not null && (entry.Payment.Sale.IsDeleted || entry.Payment.Sale.Status == SaleStatus.Voided))
                {
                    throw new ValidationException($"Transaction #{entry.Id} belongs to a voided sale and cannot be cleared.");
                }
                if (entry.StockLocationId != input.StockLocationId)
                {
                    throw new ValidationException($"Transaction #{entry.Id} does not belong to {location.Name}.");
                }
                if (entry.Method != input.Method)
                {
                    throw new ValidationException($"Transaction #{entry.Id} payment method ({entry.Method}) does not match payout method ({input.Method}).");
                }
            }

            var selectedGross = entries.Sum(e => e.GrossAmount);
            if (Math.Abs(selectedGross - input.GrossAmount) > 0.01m)
            {
                throw new ValidationException($"Selected transactions total ({selectedGross:N2}) does not match specified gross cleared amount ({input.GrossAmount:N2}).");
            }

            // Ensure "Payment Processing Fees" category exists
            var feeCategory = await db.ExpenseCategories.FirstOrDefaultAsync(c => c.Name == "Payment Processing Fees", ct);
            if (feeCategory is null)
            {
                feeCategory = new ExpenseCategoryDefinition
                {
                    Name = "Payment Processing Fees",
                    NormalizedName = NameNormalization.Normalize("Payment Processing Fees"),
                    IsArchived = false
                };
                db.ExpenseCategories.Add(feeCategory);
                await db.SaveChangesAsync(ct);
            }

            // 1. Deposit gross cleared amount into bank (+GrossAmount)
            var grossBankMovement = new BankMovement
            {
                RequestId = Guid.NewGuid(),
                BusinessDate = workingDate,
                Method = PaymentMethod.BankTransfer,
                Kind = CashMovementKind.Payout,
                Amount = input.GrossAmount,
                Notes = $"Card/wallet settlement gross · {input.Method} at {location.Name} ref #{input.Reference?.Trim()}".Trim(),
                CreatedByUserId = actorId
            };
            await FinancialPosting.BankAsync(db, grossBankMovement, ct);

            // 2. If fee > 0, record fee Expense AND corresponding Bank deduction (-fee)
            Expense? feeExpense = null;
            if (fee > 0)
            {
                feeExpense = new Expense
                {
                    RequestId = Guid.NewGuid(),
                    StockLocationId = input.StockLocationId,
                    CategoryDefinitionId = feeCategory.Id,
                    Amount = fee,
                    Method = PaymentMethod.BankTransfer,
                    OccurredAt = AccountingCalendar.At(workingDate, configuration),
                    ReceiptReference = input.Reference?.Trim(),
                    Notes = $"Payment processing fee · {input.Method} settlement ref #{input.Reference?.Trim()} (Gross: {input.GrossAmount:N2}, Fee: {fee:N2}, Net: {input.NetAmount:N2})".Trim(),
                    CreatedByUserId = actorId
                };
                db.Expenses.Add(feeExpense);

                var feeBankMovement = new BankMovement
                {
                    RequestId = Guid.NewGuid(),
                    BusinessDate = workingDate,
                    Method = PaymentMethod.BankTransfer,
                    Kind = CashMovementKind.Expense,
                    Amount = -fee,
                    Expense = feeExpense,
                    Notes = $"Payment processing fee · {input.Method} payout ref #{input.Reference?.Trim()}".Trim(),
                    CreatedByUserId = actorId
                };
                await FinancialPosting.BankAsync(db, feeBankMovement, ct);
            }

            // 3. Record PaymentPayout
            var payout = new PaymentPayout
            {
                RequestId = input.RequestId,
                BusinessDate = workingDate,
                SettledAt = DateTimeOffset.UtcNow,
                Method = input.Method,
                StockLocationId = input.StockLocationId,
                GrossAmount = input.GrossAmount,
                NetAmount = input.NetAmount,
                FeeAmount = fee,
                BankAccountId = grossBankMovement.BankAccountId,
                BankMovement = grossBankMovement,
                Expense = feeExpense,
                Reference = input.Reference?.Trim(),
                Notes = input.Notes.Trim(),
                CreatedByUserId = actorId
            };
            db.PaymentPayouts.Add(payout);

            // Mark entries as settled
            foreach (var entry in entries)
            {
                entry.Status = PaymentClearingStatus.Settled;
                entry.SettledAmount = entry.GrossAmount;
                entry.Payout = payout;
                entry.SettledAt = payout.SettledAt;
                entry.UpdatedAt = DateTimeOffset.UtcNow;
                entry.UpdatedByUserId = actorId;
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return payout.Id;
        }, ct);
    }

    public async Task<IReadOnlyList<PayoutHistoryRowDto>> GetPayoutHistoryAsync(
        ClaimsPrincipal actor, int? locationId = null, int limit = 50, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);

        var query = db.PaymentPayouts.AsNoTracking()
            .Include(p => p.StockLocation)
            .Include(p => p.BankAccount)
            .Where(p => locationId == null || p.StockLocationId == locationId.Value)
            .OrderByDescending(p => p.BusinessDate)
            .ThenByDescending(p => p.Id)
            .Take(limit);

        var payouts = await query.ToListAsync(ct);
        var userIds = payouts.Select(p => p.CreatedByUserId).Distinct().ToList();
        var userNames = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.UserName ?? "User", ct);

        return payouts.Select(p => new PayoutHistoryRowDto(
            p.Id,
            p.BusinessDate,
            p.SettledAt,
            p.StockLocationId,
            p.StockLocation?.Name ?? "Store",
            p.Method,
            p.GrossAmount,
            p.FeeAmount,
            p.GrossAmount > 0 ? Math.Round((p.FeeAmount / p.GrossAmount) * 100m, 2) : 0m,
            p.NetAmount,
            p.BankAccount?.Code ?? "MAIN",
            p.Reference,
            p.Notes,
            (p.CreatedByUserId.HasValue && userNames.ContainsKey(p.CreatedByUserId.Value)) ? userNames[p.CreatedByUserId.Value] : "User")).ToList();
    }

    public async Task<IReadOnlyList<ClearingAgeingDto>> GetAgeingReportAsync(ClaimsPrincipal actor, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);

        var today = AccountingCalendar.Today(configuration);

        var unsettled = await db.PaymentClearingEntries.AsNoTracking()
            .Include(e => e.StockLocation)
            .Where(e => e.Status == PaymentClearingStatus.Unsettled)
            .ToListAsync(ct);

        return unsettled.GroupBy(e => new { e.StockLocationId, LocationName = e.StockLocation?.Name ?? "Store", e.Method })
            .Select(g =>
            {
                var under3 = g.Where(e => today.DayNumber - e.BusinessDate.DayNumber < 3).Sum(e => e.GrossAmount);
                var from3To7 = g.Where(e => today.DayNumber - e.BusinessDate.DayNumber >= 3 && today.DayNumber - e.BusinessDate.DayNumber <= 7).Sum(e => e.GrossAmount);
                var over7 = g.Where(e => today.DayNumber - e.BusinessDate.DayNumber > 7).Sum(e => e.GrossAmount);
                return new ClearingAgeingDto(
                    g.Key.StockLocationId ?? 0,
                    g.Key.LocationName,
                    g.Key.Method,
                    under3,
                    from3To7,
                    over7,
                    under3 + from3To7 + over7);
            })
            .OrderBy(a => a.LocationName)
            .ThenBy(a => a.Method)
            .ToList();
    }

    public async Task<IReadOnlyList<MonthlyFeeAnalyticsDto>> GetFeeAnalyticsAsync(
        ClaimsPrincipal actor, int months = 6, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);

        var today = AccountingCalendar.Today(configuration);
        var startMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-(months - 1));

        var payouts = await db.PaymentPayouts.AsNoTracking()
            .Where(p => p.BusinessDate >= startMonth)
            .ToListAsync(ct);

        return payouts.GroupBy(p => new { Month = new DateOnly(p.BusinessDate.Year, p.BusinessDate.Month, 1), p.Method })
            .Select(g =>
            {
                var gross = g.Sum(p => p.GrossAmount);
                var fee = g.Sum(p => p.FeeAmount);
                var pct = gross > 0 ? Math.Round((fee / gross) * 100m, 2) : 0m;
                return new MonthlyFeeAnalyticsDto(g.Key.Month, g.Key.Method, gross, fee, pct);
            })
            .OrderByDescending(f => f.Month)
            .ThenBy(f => f.Method)
            .ToList();
    }
}
