using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;

namespace POSApp.Services;

public sealed class ClosureService(IServiceScopeFactory scopes, IConfiguration configuration)
{
    public async Task<IReadOnlyList<AuditRow>> HistoryAsync(ClaimsPrincipal actor, string? search = null, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
        var query = db.RecordChanges.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            var id = int.TryParse(term.TrimStart('#'), out var value) ? value : -1;
            query = query.Where(change => change.RecordId == id || change.RecordType.ToLower().Contains(term)
                || change.Action.ToLower().Contains(term) || change.Reason.ToLower().Contains(term));
        }
        return await query.OrderByDescending(change => change.Id).Take(100)
            .Select(change => new AuditRow(change.CreatedAt,
                db.Users.Where(user => user.Id == change.CreatedByUserId).Select(user => user.UserName).FirstOrDefault() ?? "System",
                change.RecordType, change.RecordId, change.Action, change.Reason, change.Before, change.After)).ToArrayAsync(ct);
    }

    public async Task<PagedResult<AuditRow>> HistoryPageAsync(ClaimsPrincipal actor, string? search = null, int page = 1, int pageSize = 30, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
        var query = db.RecordChanges.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            var id = int.TryParse(term.TrimStart('#'), out var value) ? value : -1;
            query = query.Where(change => change.RecordId == id || change.RecordType.ToLower().Contains(term)
                || change.Action.ToLower().Contains(term) || change.Reason.ToLower().Contains(term));
        }
        return await query.OrderByDescending(change => change.Id)
            .Select(change => new AuditRow(change.CreatedAt,
                db.Users.Where(user => user.Id == change.CreatedByUserId).Select(user => user.UserName).FirstOrDefault() ?? "System",
                change.RecordType, change.RecordId, change.Action, change.Reason, change.Before, change.After)).ToPageAsync(page, pageSize, ct);
    }

    public async Task<OperatingDay> OperatingDayAsync(ClaimsPrincipal actor, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);
        var date = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
        var day = await db.DayFinances.AsNoTracking().SingleOrDefaultAsync(day => day.Date == date, ct);
        var sessions = await db.CashSessions.Where(session => session.BusinessDate == date && session.StockLocationId != null)
            .Select(session => new { session.StockLocationId, session.ClosedAt }).ToArrayAsync(ct);
        var locations = await db.StockLocations.Where(location => location.CanSell && !location.IsDeleted)
            .Select(location => location.Id).ToArrayAsync(ct);
        var month = new DateOnly(date.Year, date.Month, 1);
        var monthClosed = await db.FinanceClosings.AnyAsync(closing => closing.Kind == ClosingKind.Month
            && closing.Date == month && closing.ClosedAt != null, ct);
        return new(date, day is not null, day?.ClosedAt is not null,
            sessions.Count(session => session.ClosedAt == null), sessions.Count(session => session.ClosedAt != null),
            locations.Count(id => !sessions.Any(session => session.StockLocationId == id)), monthClosed);
    }

    public async Task<ReopenTarget> ReadAsync(ClaimsPrincipal actor, string kind, int id, DateOnly date, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
        var target = await FindAsync(db, kind, id, date, ct);
        var closedAt = target.ClosedAt ?? throw new ValidationException("This record is already open.");
        return new(target.Entity.Id, kind, target.Date, target.Title, closedAt, Hash(target.Snapshot));
    }

    public async Task ReopenAsync(ClaimsPrincipal actor, ReopenInput input, CancellationToken ct = default)
    {
        await StoreOperation.RunAsync(async () =>
        {
            var reason = input.Reason?.Trim() ?? "";
            if (!input.Confirmed || input.RequestId == Guid.Empty || reason.Length is 0 or > 2000
                || input.Kind is not ("drawer" or "day" or "month"))
            {
                throw new ValidationException("Confirm reopening and enter a reason up to 2,000 characters.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var previous = await db.RecordChanges.SingleOrDefaultAsync(change => change.RequestId == input.RequestId, ct);
            if (previous is not null)
            {
                if (previous.CreatedByUserId != actorId || previous.RecordType != input.Kind || previous.RecordId != input.Id
                    || previous.Reason != reason || Hash(previous.Before) != input.Revision)
                {
                    throw new ValidationException("This request was already saved. Reload before changing it.");
                }
                return;
            }
            var target = await FindAsync(db, input.Kind, input.Id, input.Date, ct);
            if (target.ClosedAt is null || target.Entity.Id != input.Id || Hash(target.Snapshot) != input.Revision || target.Date != input.Date)
            {
                throw new ValidationException("This closing changed. Reload and review it again.");
            }
            if (input.Kind == "drawer")
            {
                var session = (CashSession)target.Entity;
                await RequireLatestDayAsync(db, actorId, target.Date, ct);
                await AccountingCalendar.EnsureOpenAsync(db, target.Date, ct);
                if (session.CashDrawer!.CurrentSessionId is not null
                    || await db.CashSessions.AnyAsync(other => other.CashDrawerId == session.CashDrawerId && other.Id > session.Id, ct))
                {
                    throw new ValidationException("A later drawer session exists. Its carried-forward balance must not be changed.");
                }
                session.CashDrawer.CurrentCash = session.ExpectedCash;
                session.CashDrawer.CurrentSession = session;
                session.CashDrawer.UpdatedAt = DateTimeOffset.UtcNow;
                session.CashDrawer.UpdatedByUserId = actorId;
                session.ClosedAt = null;
                session.ExpectedClosingCash = null;
                session.ActualClosingCash = null;
                session.Difference = null;
                session.ClosingNotes = null;
            }
            else
            {
                var closing = (FinanceClosing)target.Entity;
                if (input.Kind == "month")
                {
                    if (await db.FinanceClosings.AnyAsync(other => other.Kind == ClosingKind.Month
                        && other.Date > target.Date && other.ClosedAt != null, ct))
                    {
                        throw new ValidationException("Reopen later closed months first to preserve their opening balances.");
                    }
                }
                else
                {
                    await RequireLatestDayAsync(db, actorId, target.Date, ct);
                    var month = new DateOnly(target.Date.Year, target.Date.Month, 1);
                    if (await db.FinanceClosings.AnyAsync(other => other.Kind == ClosingKind.Month
                        && other.Date == month && other.ClosedAt != null, ct))
                    {
                        throw new ValidationException("Reopen this month before reopening its day.");
                    }
                    var day = await db.DayFinances.SingleOrDefaultAsync(day => day.Date == target.Date, ct)
                        ?? throw new ValidationException("The saved working day was not found.");
                    day.ClosedAt = null;
                    day.CountedCash = null;
                    day.Difference = null;
                    day.UpdatedAt = DateTimeOffset.UtcNow;
                    day.UpdatedByUserId = actorId;
                }
                closing.ClosedAt = null;
                closing.MonthlyDetailsJson = null;
            }
            target.Entity.UpdatedAt = DateTimeOffset.UtcNow;
            target.Entity.UpdatedByUserId = actorId;
            db.RecordChanges.Add(new RecordChange
            {
                RequestId = input.RequestId,
                RecordType = input.Kind,
                RecordId = target.Entity.Id,
                Action = "Reopen",
                Reason = reason,
                Before = target.Snapshot,
                After = input.Kind == "drawer" ? DrawerSnapshot((CashSession)target.Entity) : PeriodSnapshot((FinanceClosing)target.Entity),
                CreatedByUserId = actorId
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }, ct);
    }

    public static string Revision(ClosingTotals totals) => Hash(JsonSerializer.Serialize(new
    {
        totals.Start, totals.End, totals.OpeningCash, totals.OpeningBank, totals.CashIn, totals.CashOut,
        totals.BankIn, totals.BankOut, totals.ClosingCash, totals.ClosingBank, totals.Revenue, totals.Cogs,
        totals.Expenses, totals.Salaries, totals.SupplierPaid, totals.PayrollPaid, totals.SupplierDue,
        totals.CustomerDue, totals.StockValue, totals.DebtBorrowed, totals.DebtRepaid, totals.OutstandingDebt,
        totals.CashVariance, totals.StockVariance, totals.Monthly
    }));

    internal static string DrawerSnapshot(CashSession session) => JsonSerializer.Serialize(new
    {
        session.Id, session.BusinessDate, session.CashDrawerId, session.OpeningCash, session.ExpectedCash,
        session.ExpectedClosingCash, session.ActualClosingCash, session.Difference, session.ClosedAt,
        session.ClosingNotes, session.UpdatedAt, CurrentCash = session.CashDrawer?.CurrentCash
    });

    internal static string PeriodSnapshot(FinanceClosing closing) => JsonSerializer.Serialize(closing);

    private async Task RequireLatestDayAsync(AppDbContext db, int actorId, DateOnly date, CancellationToken ct)
    {
        if (date != await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct)
            || await db.DayFinances.AnyAsync(day => day.Date > date, ct)
            || await db.CashSessions.AnyAsync(session => session.BusinessDate > date, ct))
        {
            throw new ValidationException("Only the latest working day can be reopened. A later day's opening depends on this closing; use a current-day correction for older records.");
        }
    }

    private static async Task<Target> FindAsync(AppDbContext db, string kind, int id, DateOnly date, CancellationToken ct)
    {
        if (kind == "drawer")
        {
            var session = await db.CashSessions.Include(session => session.CashDrawer).ThenInclude(drawer => drawer!.StockLocation)
                .SingleOrDefaultAsync(session => session.Id == id, ct) ?? throw new ValidationException("Drawer session not found.");
            return new(session, session.BusinessDate, session.CashDrawer!.StockLocation?.Name ?? $"Drawer #{session.CashDrawerId}",
                session.ClosedAt, DrawerSnapshot(session));
        }
        if (kind is not ("day" or "month") || date == default)
        {
            throw new ValidationException("Choose a drawer, day or month closing.");
        }
        var period = kind == "month" ? ClosingKind.Month : ClosingKind.Day;
        date = period == ClosingKind.Month ? new(date.Year, date.Month, 1) : date;
        var closing = await db.FinanceClosings.SingleOrDefaultAsync(closing => closing.Kind == period && closing.Date == date, ct)
            ?? throw new ValidationException("Closing not found.");
        return new(closing, date, kind == "month" ? "Monthly closing" : "Business day closing", closing.ClosedAt, PeriodSnapshot(closing));
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private sealed record Target(AuditableEntity Entity, DateOnly Date, string Title, DateTimeOffset? ClosedAt, string Snapshot);
}
