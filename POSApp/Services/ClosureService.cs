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
        var date = AccountingCalendar.Today(configuration);
        var activeSessions = await db.CashSessions.Where(session => session.StockLocationId != null && session.ClosedAt == null)
            .Select(session => new { session.StockLocationId }).ToArrayAsync(ct);
        var todayStartUtc = AccountingCalendar.At(date, TimeOnly.MinValue, configuration);
        var closedTodaySessions = await db.CashSessions.Where(session => session.StockLocationId != null && session.ClosedAt != null
            && (session.BusinessDate == date || session.ClosedAt >= todayStartUtc))
            .Select(session => new { session.StockLocationId }).ToArrayAsync(ct);
        var locations = await db.StockLocations.Where(location => location.CanSell && !location.IsDeleted)
            .Select(location => location.Id).ToArrayAsync(ct);

        return new(date, true, false,
            activeSessions.Length, closedTodaySessions.Length,
            locations.Count(id => !activeSessions.Any(session => session.StockLocationId == id)), false);
    }

    public async Task<ReopenTarget> ReadAsync(ClaimsPrincipal actor, string kind, int id, DateOnly date, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
        var target = await FindAsync(db, kind, id, ct);
        var closedAt = target.ClosedAt ?? throw new ValidationException("This record is already open.");
        return new(target.Entity.Id, kind, target.Date, target.Title, closedAt, Hash(target.Snapshot));
    }

    public async Task ReopenAsync(ClaimsPrincipal actor, ReopenInput input, CancellationToken ct = default)
    {
        await StoreOperation.RunAsync(async () =>
        {
            var reason = input.Reason?.Trim() ?? "";
            if (!input.Confirmed || input.RequestId == Guid.Empty || reason.Length is 0 or > 2000
                || input.Kind != "drawer")
            {
                throw new ValidationException("Confirm shift reopening and enter a reason up to 2,000 characters.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            var previous = await db.RecordChanges.SingleOrDefaultAsync(change => change.RequestId == input.RequestId, ct);
            if (previous is not null)
            {
                return;
            }
            var target = await FindAsync(db, input.Kind, input.Id, ct);
            if (target.ClosedAt is null || target.Entity.Id != input.Id || Hash(target.Snapshot) != input.Revision)
            {
                throw new ValidationException("This shift closing changed. Reload and review it again.");
            }

            var session = (CashSession)target.Entity;
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

            target.Entity.UpdatedAt = DateTimeOffset.UtcNow;
            target.Entity.UpdatedByUserId = actorId;
            db.RecordChanges.Add(new RecordChange
            {
                RequestId = input.RequestId,
                RecordType = input.Kind,
                RecordId = target.Entity.Id,
                Action = "ReopenShift",
                Reason = reason,
                Before = target.Snapshot,
                After = DrawerSnapshot(session),
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
        session.Id, session.CashDrawerId, session.OpeningCash, session.ExpectedCash,
        session.ExpectedClosingCash, session.ActualClosingCash, session.Difference, session.ClosedAt,
        session.ClosingNotes, session.UpdatedAt, CurrentCash = session.CashDrawer?.CurrentCash
    });

    private static async Task<Target> FindAsync(AppDbContext db, string kind, int id, CancellationToken ct)
    {
        if (kind == "drawer")
        {
            var session = await db.CashSessions.Include(session => session.CashDrawer).ThenInclude(drawer => drawer!.StockLocation)
                .SingleOrDefaultAsync(session => session.Id == id, ct) ?? throw new ValidationException("Drawer session not found.");
            var openDate = session.BusinessDate;
            return new(session, openDate, session.CashDrawer!.StockLocation?.Name ?? $"Drawer #{session.CashDrawerId}",
                session.ClosedAt, DrawerSnapshot(session));
        }
        throw new ValidationException("Only drawer shifts can be reopened.");
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private sealed record Target(AuditableEntity Entity, DateOnly Date, string Title, DateTimeOffset? ClosedAt, string Snapshot);
}
