using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Persistence;
using POSApp.Persistence.Entities;

namespace POSApp.Services;

public sealed class CashDrawerService(IServiceScopeFactory scopes, IConfiguration? configuration = null)
{
    public async Task<CashDrawerPage> ReadAsync(ClaimsPrincipal actor, int? sessionId = null, CancellationToken ct = default, int movementPage = 1, int sessionPage = 1, int pageSize = 30)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);
        var admin = await IsAdminAsync(db, actorId, ct);
        var date = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
        var locations = await db.StockLocations.Where(location => location.CanSell && !location.IsDeleted)
            .OrderBy(location => location.Name).Select(location => new { location.Id, location.Name }).ToArrayAsync(ct);
        var drawers = await db.CashDrawers.AsNoTracking().ToArrayAsync(ct);
        var selected = await db.Users.Where(user => user.Id == actorId).Select(user => user.CashLocationId).SingleAsync(ct);
        if (!locations.Any(location => location.Id == selected))
        {
            selected = locations.Length == 1 ? locations[0].Id : null;
        }

        var selectedDrawer = drawers.SingleOrDefault(drawer => drawer.StockLocationId == selected && selected != null);
        var drawerId = selectedDrawer?.Id;

        // Pending count for the selected location (if any)
        var pendingCountEntity = drawerId is null ? null : await db.CashCounts.AsNoTracking()
            .Where(c => c.CashDrawerId == drawerId.Value && c.Status == CashCountStatus.PendingReview && !c.IsVoided)
            .Include(c => c.CountedByUser)
            .OrderByDescending(c => c.Id)
            .FirstOrDefaultAsync(ct);

        var pendingCount = pendingCountEntity is null ? null : new CashCountRow(
            pendingCountEntity.Id,
            pendingCountEntity.StockLocationId,
            selectedDrawer?.StockLocationId != null ? locations.FirstOrDefault(l => l.Id == selectedDrawer.StockLocationId)?.Name ?? "Drawer" : "Drawer",
            pendingCountEntity.CountedByUser?.UserName ?? "Staff",
            pendingCountEntity.CountedAt,
            pendingCountEntity.CountedCash,
            pendingCountEntity.ExpectedCash,
            pendingCountEntity.Difference,
            pendingCountEntity.Status,
            pendingCountEntity.Notes,
            pendingCountEntity.CompletedAt,
            pendingCountEntity.VarianceAcceptedAt,
            pendingCountEntity.IsVoided);

        // Last completed or accepted count
        var lastCountEntity = drawerId is null ? null : await db.CashCounts.AsNoTracking()
            .Where(c => c.CashDrawerId == drawerId.Value && (c.Status == CashCountStatus.Completed || c.Status == CashCountStatus.VarianceAccepted) && !c.IsVoided)
            .Include(c => c.CountedByUser)
            .OrderByDescending(c => c.Id)
            .FirstOrDefaultAsync(ct);

        var lastCount = lastCountEntity is null ? null : new CashCountRow(
            lastCountEntity.Id,
            lastCountEntity.StockLocationId,
            selectedDrawer?.StockLocationId != null ? locations.FirstOrDefault(l => l.Id == selectedDrawer.StockLocationId)?.Name ?? "Drawer" : "Drawer",
            lastCountEntity.CountedByUser?.UserName ?? "Staff",
            lastCountEntity.CountedAt,
            lastCountEntity.CountedCash,
            lastCountEntity.ExpectedCash,
            lastCountEntity.Difference,
            lastCountEntity.Status,
            lastCountEntity.Notes,
            lastCountEntity.CompletedAt,
            lastCountEntity.VarianceAcceptedAt,
            lastCountEntity.IsVoided);

        // Recent counts history
        var countHistoryEntities = await db.CashCounts.AsNoTracking()
            .Where(c => admin || c.StockLocationId == selected)
            .Include(c => c.StockLocation)
            .Include(c => c.CountedByUser)
            .OrderByDescending(c => c.CountedAt)
            .Take(50)
            .ToListAsync(ct);

        var countHistory = countHistoryEntities.Select(c => new CashCountRow(
            c.Id,
            c.StockLocationId,
            c.StockLocation?.Name ?? "Drawer",
            c.CountedByUser?.UserName ?? "Staff",
            c.CountedAt,
            c.CountedCash,
            c.ExpectedCash,
            c.Difference,
            c.Status,
            c.Notes,
            c.CompletedAt,
            c.VarianceAcceptedAt,
            c.IsVoided)).ToArray();

        // Recent movements for selected drawer
        var movementsQuery = db.CashMovements.AsNoTracking()
            .Where(move => (drawerId != null && move.CashDrawerId == drawerId.Value)
                || (sessionId != null && move.CashSessionId == sessionId.Value));

        var movements = drawerId is null && sessionId is null ? null : await movementsQuery
            .OrderByDescending(move => move.Id)
            .Select(move => new CashMovementRow(move.CreatedAt, move.Kind, move.Amount, move.Notes))
            .ToPageAsync(movementPage, pageSize, ct);

        var breakdown = drawerId is null && sessionId is null ? [] : await movementsQuery
            .GroupBy(move => move.Kind).Select(group => new CashSummary(group.Key, group.Sum(move => move.Amount))).ToArrayAsync(ct);



        // Sessions fallback for legacy inspection
        var sessions = await db.CashSessions.Where(session => session.OpenedAt >= DateTimeOffset.UtcNow.Date)
            .Select(session => new { session.CashDrawerId, session.Id, session.ClosedAt }).ToArrayAsync(ct);
        var visible = db.CashSessions.Where(session => admin || session.CashDrawer!.StockLocationId == selected);
        var recent = await Rows(db, visible.OrderByDescending(session => session.Id)).ToPageAsync(sessionPage, pageSize, ct);
        var viewed = sessionId is null ? null : await Rows(db, visible.Where(session => session.Id == sessionId)).SingleOrDefaultAsync(ct);

        // Synthesize current CashSessionRow if viewed is null to preserve legacy DTO contract
        var current = viewed;
        if (current is null && selectedDrawer != null)
        {
            current = new CashSessionRow(
                selectedDrawer.Id,
                "Continuous",
                selectedDrawer.LastCountedAt ?? selectedDrawer.CreatedAt,
                null,
                selectedDrawer.OpeningBalance,
                selectedDrawer.CurrentCash,
                selectedDrawer.LastCountedCash,
                (selectedDrawer.LastCountedCash ?? selectedDrawer.CurrentCash) - selectedDrawer.CurrentCash,
                "Live continuous drawer")
            {
                DrawerId = selectedDrawer.Id,
                LocationId = selectedDrawer.StockLocationId,
                Location = locations.FirstOrDefault(l => l.Id == selectedDrawer.StockLocationId)?.Name ?? "Main",
                BusinessDate = date
            };
        }

        return new(current, viewed ?? current, movements?.Items ?? [], breakdown, recent.Items, admin)
        {
            CurrentCash = selectedDrawer?.CurrentCash ?? 0,
            MovementPagination = movements?.Pagination,
            SessionPagination = recent.Pagination,
            SelectedLocationId = selected,
            PendingCount = pendingCount,
            LastCount = lastCount,
            CountHistory = countHistory,
            Locations = locations.Select(location =>
            {
                var drawer = drawers.SingleOrDefault(drawer => drawer.StockLocationId == location.Id);
                var activeSession = sessions.FirstOrDefault(session => session.CashDrawerId == drawer?.Id && session.ClosedAt == null);
                var closedSession = sessions.Where(session => session.CashDrawerId == drawer?.Id && session.ClosedAt != null)
                    .OrderByDescending(session => session.Id).FirstOrDefault();
                var isOpenToday = activeSession != null;
                var isClosedToday = !isOpenToday && closedSession != null;
                return new CashLocationRow(location.Id, location.Name, drawer?.CurrentCash ?? 0,
                    drawer?.OpeningDate != null, activeSession?.Id, isClosedToday)
                {
                    ClosedSessionId = closedSession?.Id,
                    LastCountedAt = drawer?.LastCountedAt,
                    LastCountedCash = drawer?.LastCountedCash
                };
            }).ToArray(),
            Unassigned = admin ? drawers.Where(drawer => drawer.StockLocationId == null && drawer.CurrentCash != 0)
                .Select(drawer => new LegacyCashDrawerRow(drawer.Id, drawer.CurrentCash, drawer.CurrentSessionId)).ToArray() : []
        };
    }

    private static IQueryable<CashSessionRow> Rows(AppDbContext db, IQueryable<CashSession> query) => query.Select(session => new CashSessionRow(
        session.Id,
        db.Users.Where(user => user.Id == session.CreatedByUserId).Select(user => user.UserName!).FirstOrDefault() ?? "Staff",
        session.OpenedAt, session.ClosedAt, session.OpeningCash, session.ExpectedClosingCash ?? session.ExpectedCash,
        session.ActualClosingCash, session.Difference, session.ClosingNotes)
    {
        DrawerId = session.CashDrawerId,
        LocationId = session.CashDrawer!.StockLocationId,
        Location = session.CashDrawer.StockLocation == null ? "Legacy / unassigned" : session.CashDrawer.StockLocation.Name,
        BusinessDate = DateOnly.FromDateTime(session.OpenedAt.DateTime)
    });

    public async Task<int> SubmitCountAsync(ClaimsPrincipal actor, CashCountInput input, CancellationToken ct = default)
    {
        return await StoreOperation.RunAsync<int>(async () =>
        {
            var counted = input.CountedCash ?? throw new ValidationException("Enter the physically counted cash.");
            StoreAccess.Money(counted, "Counted cash");
            if (input.Notes?.Length > 2000)
            {
                throw new ValidationException("Notes must be at most 2,000 characters.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            var locationId = input.LocationId != 0 ? input.LocationId : await ResolveLocationAsync(db, actorId, ct);
            await ValidateLocationAsync(db, locationId, ct);
            var drawer = await RequireDrawerAsync(db, actorId, ct, locationId);

            var now = DateTimeOffset.UtcNow;
            var date = AccountingCalendar.Today(configuration);
            var expected = drawer.CurrentCash;

            var difference = counted - expected;
            if (difference != 0 && string.IsNullOrWhiteSpace(input.Notes))
            {
                throw new ValidationException("Add a note explaining the shortage or overage.");
            }

            var status = difference == 0 ? CashCountStatus.Completed : CashCountStatus.PendingReview;

            var count = new CashCount
            {
                RequestId = input.RequestId != Guid.Empty ? input.RequestId : Guid.NewGuid(),
                CashDrawerId = drawer.Id,
                StockLocationId = locationId,
                BusinessDate = date,
                CountedAt = now,
                CountedByUserId = actorId,
                CountedCash = counted,
                ExpectedCash = expected,
                Notes = input.Notes?.Trim(),
                Status = status,
                CompletedAt = status == CashCountStatus.Completed ? now : null,
                CompletedByUserId = status == CashCountStatus.Completed ? actorId : null
            };
            db.CashCounts.Add(count);

            drawer.LastCountedAt = now;
            drawer.LastCountedCash = counted;
            drawer.UpdatedAt = now;
            drawer.UpdatedByUserId = actorId;

            db.RecordChanges.Add(new RecordChange
            {
                RequestId = Guid.NewGuid(),
                RecordType = "cash_count",
                RecordId = count.Id,
                Action = status == CashCountStatus.Completed ? "CountBalanced" : "CountPendingReview",
                Reason = input.Notes?.Trim() ?? "Physical cash count recorded",
                Before = $"{{\"Expected\": {expected}}}",
                After = $"{{\"Counted\": {counted}, \"Difference\": {difference}, \"Status\": \"{status}\"}}",
                CreatedByUserId = actorId
            });

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return count.Id;
        }, ct);
    }

    public async Task AcceptVarianceAsync(ClaimsPrincipal actor, AcceptVarianceInput input, CancellationToken ct = default)
    {
        await StoreOperation.RunAsync(async () =>
        {
            if (!input.Confirmed)
            {
                throw new ValidationException("Confirm acceptance of this cash variance.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            var count = await db.CashCounts.Include(c => c.CashDrawer)
                .SingleOrDefaultAsync(c => c.Id == input.CountId, ct)
                ?? throw new ValidationException("Cash count record not found.");

            if (count.Status != CashCountStatus.PendingReview)
            {
                throw new ValidationException("This cash count is not awaiting variance approval.");
            }



            var note = string.IsNullOrWhiteSpace(input.Notes) ? count.Notes : input.Notes.Trim();
            if (string.IsNullOrWhiteSpace(note))
            {
                throw new ValidationException("A note is required when accepting a cash variance.");
            }

            var now = DateTimeOffset.UtcNow;
            var movement = new CashMovement
            {
                RequestId = Guid.NewGuid(),
                CashDrawerId = count.CashDrawerId,
                BusinessDate = count.BusinessDate,
                Kind = CashMovementKind.Variance,
                Amount = count.Difference,
                Notes = $"Cash count #{count.Id} variance · {note}",
                CreatedByUserId = actorId,
                CreatedAt = count.CountedAt
            };
            Append(db, count.CashDrawer!, movement);

            count.VarianceMovement = movement;
            count.Status = CashCountStatus.VarianceAccepted;
            count.VarianceAcceptedAt = now;
            count.VarianceAcceptedByUserId = actorId;
            count.CompletedAt = now;
            count.CompletedByUserId = actorId;
            count.Notes = note;
            count.UpdatedAt = now;
            count.UpdatedByUserId = actorId;

            db.RecordChanges.Add(new RecordChange
            {
                RequestId = Guid.NewGuid(),
                RecordType = "cash_count",
                RecordId = count.Id,
                Action = "AcceptVariance",
                Reason = note,
                Before = $"{{\"Status\": \"PendingReview\", \"Variance\": {count.Difference}}}",
                After = $"{{\"Status\": \"VarianceAccepted\", \"MovementId\": {movement.Id}}}",
                CreatedByUserId = actorId
            });

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }, ct);
    }

    public async Task VoidCountAsync(ClaimsPrincipal actor, int countId, string reason, CancellationToken ct = default)
    {
        await StoreOperation.RunAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(reason) || reason.Length > 2000)
            {
                throw new ValidationException("Enter a reason of up to 2,000 characters.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            var count = await db.CashCounts.Include(c => c.CashDrawer).Include(c => c.VarianceMovement)
                .SingleOrDefaultAsync(c => c.Id == countId, ct)
                ?? throw new ValidationException("Cash count not found.");

            if (count.IsVoided)
            {
                return;
            }

            // If a variance movement was already posted, reverse it
            if (count.VarianceMovement is not null)
            {
                Append(db, count.CashDrawer!, new CashMovement
                {
                    RequestId = Guid.NewGuid(),
                    CashDrawerId = count.CashDrawerId,
                    BusinessDate = count.BusinessDate,
                    Kind = CashMovementKind.Reversal,
                    Amount = -count.VarianceMovement.Amount,
                    Notes = $"Reversal of count #{count.Id} variance: {reason.Trim()}",
                    ReversesMovementId = count.VarianceMovement.Id,
                    CreatedByUserId = actorId
                });
            }

            count.IsVoided = true;
            count.VoidedAt = DateTimeOffset.UtcNow;
            count.VoidedByUserId = actorId;
            count.VoidReason = reason.Trim();
            count.Status = CashCountStatus.Voided;
            count.UpdatedAt = DateTimeOffset.UtcNow;
            count.UpdatedByUserId = actorId;

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }, ct);
    }

    public static async Task<decimal> GetCashBalanceAtAsync(AppDbContext db, int drawerId, DateTimeOffset asOf, CancellationToken ct = default)
    {
        var drawer = await db.CashDrawers.AsNoTracking().SingleOrDefaultAsync(d => d.Id == drawerId, ct);
        if (drawer is null) return 0m;

        // If querying right now (within 30 seconds of UtcNow), return live stored CurrentCash
        if (Math.Abs((DateTimeOffset.UtcNow - asOf).TotalSeconds) < 30)
        {
            return drawer.CurrentCash;
        }

        // Find nearest accepted Physical Cash Count checkpoint at or before 'asOf'
        var checkpoint = await db.CashCounts.AsNoTracking()
            .Where(c => c.CashDrawerId == drawerId && (c.Status == CashCountStatus.Completed || c.Status == CashCountStatus.VarianceAccepted))
            .Where(c => c.CountedAt <= asOf && !c.IsVoided)
            .OrderByDescending(c => c.CountedAt)
            .Select(c => new { c.CountedAt, c.CountedCash })
            .FirstOrDefaultAsync(ct);

        var baseTimestamp = checkpoint?.CountedAt ?? DateTimeOffset.MinValue;
        var baseBalance = checkpoint?.CountedCash ?? drawer.OpeningBalance;

        // Sum only delta movements between the checkpoint and asOf
        var delta = await db.CashMovements.AsNoTracking()
            .Where(m => m.CashDrawerId == drawerId && m.CreatedAt > baseTimestamp && m.CreatedAt <= asOf)
            .SumAsync(m => (decimal?)m.Amount, ct) ?? 0m;

        return baseBalance + delta;
    }

    public async Task<decimal> GetCashBalanceAtAsync(ClaimsPrincipal actor, int drawerId, DateTimeOffset asOf, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
        return await GetCashBalanceAtAsync(db, drawerId, asOf, ct);
    }

    public async Task SelectLocationAsync(ClaimsPrincipal actor, int locationId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
        await ValidateLocationAsync(db, locationId, ct);
        var user = await db.Users.SingleAsync(user => user.Id == actorId, ct);
        user.CashLocationId = locationId;
        await db.SaveChangesAsync(ct);
    }

    public async Task<string?> SelectedLocationNameAsync(ClaimsPrincipal actor, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
        return await db.Users.Where(user => user.Id == actorId && user.CashLocation != null && user.CashLocation.CanSell && !user.CashLocation.IsDeleted)
            .Select(user => user.CashLocation!.Name).SingleOrDefaultAsync(ct);
    }

    public async Task<int> OpenAsync(ClaimsPrincipal actor, OpenCashInput input, CancellationToken ct = default)
    {
        return await StoreOperation.RunAsync<int>(async () =>
        {
            StoreAccess.Money(input.OpeningCash, "Initial opening cash");
            if (input.RequestId == Guid.Empty)
            {
                throw new ValidationException("Reload the opening form and try again.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var locationId = input.StockLocationId ?? await ResolveLocationAsync(db, actorId, ct);
            await ValidateLocationAsync(db, locationId, ct);
            var date = AccountingCalendar.Today(configuration);
            var drawer = await db.CashDrawers.SingleOrDefaultAsync(drawer => drawer.StockLocationId == locationId, ct);
            if (drawer is null)
            {
                drawer = new CashDrawer
                {
                    StockLocationId = locationId,
                    OpeningBalance = input.OpeningCash,
                    OpeningDate = date,
                    CurrentCash = input.OpeningCash,
                    CreatedByUserId = actorId
                };
                db.CashDrawers.Add(drawer);
                await db.SaveChangesAsync(ct);
            }
            else if (drawer.OpeningDate is null)
            {
                drawer.OpeningBalance = input.OpeningCash;
                drawer.OpeningDate = date;
                drawer.CurrentCash = input.OpeningCash;
                await db.SaveChangesAsync(ct);
            }

            var session = new CashSession
            {
                RequestId = input.RequestId,
                CashDrawer = drawer,
                StockLocationId = locationId,
                OpenedAt = DateTimeOffset.UtcNow,
                OpeningCash = drawer.CurrentCash,
                ExpectedCash = drawer.CurrentCash,
                CreatedByUserId = actorId
            };
            db.CashSessions.Add(session);
            drawer.CurrentSession = session;
            drawer.CurrentSessionId = session.Id;
            await db.SaveChangesAsync(ct);

            (await db.Users.SingleAsync(user => user.Id == actorId, ct)).CashLocationId = locationId;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return session.Id;
        }, ct);
    }

    public async Task CloseAsync(ClaimsPrincipal actor, CloseCashInput input, CancellationToken ct = default)
    {
        await StoreOperation.RunAsync(async () =>
        {
            var actual = input.ActualCash ?? throw new ValidationException("Enter the physically counted cash.");
            StoreAccess.Money(actual, "Counted cash");
            if (input.Notes?.Length > 2000)
            {
                throw new ValidationException("Closing notes must be at most 2,000 characters.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            var session = await db.CashSessions.Include(s => s.CashDrawer)
                .SingleOrDefaultAsync(s => s.Id == input.SessionId, ct);

            var drawer = session?.CashDrawer ?? await RequireDrawerAsync(db, actorId, ct);

            var now = DateTimeOffset.UtcNow;
            var difference = actual - drawer.CurrentCash;
            if (difference != 0 && string.IsNullOrWhiteSpace(input.Notes))
            {
                throw new ValidationException("Add a note explaining the shortage or overage.");
            }

            var count = new CashCount
            {
                RequestId = Guid.NewGuid(),
                CashDrawerId = drawer.Id,
                StockLocationId = drawer.StockLocationId ?? 0,
                BusinessDate = AccountingCalendar.Today(configuration),
                CountedAt = now,
                CountedByUserId = actorId,
                CountedCash = actual,
                ExpectedCash = drawer.CurrentCash,
                Notes = input.Notes?.Trim(),
                Status = difference == 0 ? CashCountStatus.Completed : CashCountStatus.VarianceAccepted,
                CompletedAt = now,
                CompletedByUserId = actorId
            };
            db.CashCounts.Add(count);

            if (difference != 0)
            {
                var varianceMove = new CashMovement
                {
                    RequestId = Guid.NewGuid(),
                    CashDrawerId = drawer.Id,
                    BusinessDate = count.BusinessDate,
                    Kind = CashMovementKind.Variance,
                    Amount = difference,
                    Notes = $"Closing count variance · {input.Notes?.Trim()}",
                    CreatedByUserId = actorId
                };
                Append(db, drawer, varianceMove);
                count.VarianceMovement = varianceMove;
                count.VarianceAcceptedAt = now;
                count.VarianceAcceptedByUserId = actorId;
            }

            drawer.LastCountedAt = now;
            drawer.LastCountedCash = actual;
            drawer.CurrentSession = null;
            drawer.CurrentSessionId = null;
            drawer.UpdatedAt = now;
            drawer.UpdatedByUserId = actorId;

            if (session != null)
            {
                session.ExpectedClosingCash = session.ExpectedCash;
                session.ActualClosingCash = actual;
                session.Difference = difference;
                session.ClosingNotes = input.Notes?.Trim();
                session.ClosedAt = now;
                session.UpdatedAt = now;
                session.UpdatedByUserId = actorId;
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }, ct);
    }

    public async Task AssignAsync(ClaimsPrincipal actor, AssignCashDrawerInput input, CancellationToken ct = default)
    {
        await StoreOperation.RunAsync(async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await ValidateLocationAsync(db, input.LocationId, ct);
            var drawer = await db.CashDrawers.SingleOrDefaultAsync(drawer => drawer.Id == input.DrawerId && drawer.StockLocationId == null, ct)
                ?? throw new ValidationException("Choose an unassigned legacy drawer.");
            var existing = await db.CashDrawers.SingleOrDefaultAsync(drawer => drawer.StockLocationId == input.LocationId, ct);
            if (existing is not null)
            {
                if (drawer.CurrentCash == 0)
                {
                    return;
                }
                var date = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
                var amount = drawer.CurrentCash;
                var transfer = new CashTransfer
                {
                    RequestId = Guid.NewGuid(),
                    SourceDrawer = drawer,
                    DestinationDrawer = existing,
                    BusinessDate = date,
                    Amount = amount,
                    Notes = "Legacy cash consolidated into selected location",
                    CreatedByUserId = actorId
                };
                db.CashTransfers.Add(transfer);
                Append(db, drawer, new CashMovement
                {
                    RequestId = Guid.NewGuid(),
                    BusinessDate = date,
                    Kind = CashMovementKind.Transfer,
                    Amount = -amount,
                    CashTransfer = transfer,
                    Notes = transfer.Notes,
                    CreatedByUserId = actorId
                });
                Append(db, existing, new CashMovement
                {
                    RequestId = Guid.NewGuid(),
                    BusinessDate = date,
                    Kind = CashMovementKind.Transfer,
                    Amount = amount,
                    CashTransfer = transfer,
                    Notes = transfer.Notes,
                    CreatedByUserId = actorId
                });
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return;
            }
            drawer.StockLocationId = input.LocationId;
            drawer.UpdatedAt = DateTimeOffset.UtcNow;
            drawer.UpdatedByUserId = actorId;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }, ct);
    }

    public async Task TransferAsync(ClaimsPrincipal actor, CashTransferInput input, CancellationToken ct = default)
    {
        await StoreOperation.RunAsync(async () =>
        {
            StoreAccess.Money(input.Amount, "Transfer amount");
            ValidateNotes(input.Notes);
            if (input.Amount <= 0 || input.RequestId == Guid.Empty || input.SourceLocationId == input.DestinationLocationId)
            {
                throw new ValidationException("Choose two different selling locations and a positive cash amount.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var previous = await db.CashTransfers.Include(transfer => transfer.SourceDrawer)
                .Include(transfer => transfer.DestinationDrawer).SingleOrDefaultAsync(transfer => transfer.RequestId == input.RequestId, ct);
            if (previous is not null)
            {
                if (previous.CreatedByUserId != actorId || previous.SourceDrawer!.StockLocationId != input.SourceLocationId
                    || previous.DestinationDrawer!.StockLocationId != input.DestinationLocationId
                    || previous.Amount != input.Amount || previous.Notes != input.Notes.Trim())
                {
                    throw new ValidationException("This cash transfer was already saved with different values. Start a new transfer.");
                }
                return;
            }
            var source = await RequireDrawerAsync(db, actorId, ct, input.SourceLocationId);
            var destination = await RequireDrawerAsync(db, actorId, ct, input.DestinationLocationId);
            var names = await db.StockLocations.Where(location => location.Id == input.SourceLocationId || location.Id == input.DestinationLocationId)
                .ToDictionaryAsync(location => location.Id, location => location.Name, ct);
            var transfer = new CashTransfer
            {
                RequestId = input.RequestId,
                SourceDrawer = source,
                DestinationDrawer = destination,
                Amount = input.Amount,
                Notes = input.Notes.Trim(),
                CreatedByUserId = actorId
            };
            db.CashTransfers.Add(transfer);
            foreach (var (drawer, amount) in new[] { (source, -input.Amount), (destination, input.Amount) })
            {
                Append(db, drawer, new CashMovement
                {
                    RequestId = Guid.NewGuid(),
                    Kind = CashMovementKind.Transfer,
                    Amount = amount,
                    CashTransfer = transfer,
                    Notes = $"Transfer {(amount < 0 ? "to " + names[input.DestinationLocationId] : "from " + names[input.SourceLocationId])} · {input.Notes.Trim()}",
                    CreatedByUserId = actorId
                });
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }, ct);
    }

    public async Task AdjustAsync(ClaimsPrincipal actor, CashAdjustmentInput input, CancellationToken ct = default)
    {
        await StoreOperation.RunAsync(async () =>
        {
            StoreAccess.Money(input.Amount, "Cash amount");
            ValidateNotes(input.Notes);
            if (input.Amount <= 0 || input.RequestId == Guid.Empty)
            {
                throw new ValidationException("Enter a positive cash amount.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var previous = await db.CashMovements.SingleOrDefaultAsync(move => move.RequestId == input.RequestId, ct);
            if (previous is not null)
            {
                return;
            }
            int? locId = input.LocationId;
            CashSession? session = null;
            if (input.SessionId.HasValue)
            {
                session = await db.CashSessions.FirstOrDefaultAsync(s => s.Id == input.SessionId.Value, ct);
                locId ??= session?.StockLocationId;
            }
            var drawer = await RequireDrawerAsync(db, actorId, ct, locId);
            Append(db, drawer, new CashMovement
            {
                RequestId = input.RequestId,
                CashSession = session,
                CashSessionId = session?.Id,
                BusinessDate = session?.BusinessDate ?? AccountingCalendar.Today(configuration),
                Kind = input.Payout ? CashMovementKind.OwnerWithdrawal : CashMovementKind.OwnerContribution,
                Amount = input.Payout ? -input.Amount : input.Amount,
                Notes = input.Notes.Trim(),
                CreatedByUserId = actorId
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }, ct);
    }

    private static async Task ValidateLocationAsync(AppDbContext db, int locationId, CancellationToken ct)
    {
        if (!await db.StockLocations.AnyAsync(location => location.Id == locationId && location.CanSell && !location.IsDeleted, ct))
        {
            throw new ValidationException("Choose an active selling location. Warehouses do not hold a sales drawer.");
        }
    }

    private static async Task<int> ResolveLocationAsync(AppDbContext db, int actorId, CancellationToken ct)
    {
        var selected = await db.Users.Where(user => user.Id == actorId).Select(user => user.CashLocationId).SingleAsync(ct);
        if (selected is not null)
        {
            await ValidateLocationAsync(db, selected.Value, ct);
            return selected.Value;
        }
        var locations = await db.StockLocations.Where(location => location.CanSell && !location.IsDeleted)
            .Select(location => location.Id).Take(2).ToArrayAsync(ct);
        if (locations.Length != 1)
        {
            throw new ValidationException("Select the paying cash location on the Cash drawers page.");
        }
        return locations[0];
    }

    internal static async Task<CashDrawer> RequireDrawerAsync(AppDbContext db, int actorId, CancellationToken ct, int? locationId = null)
    {
        var id = locationId ?? await ResolveLocationAsync(db, actorId, ct);
        await ValidateLocationAsync(db, id, ct);
        var drawer = db.CashDrawers.Local.SingleOrDefault(d => d.StockLocationId == id)
            ?? await db.CashDrawers.SingleOrDefaultAsync(d => d.StockLocationId == id, ct);
        if (drawer is null)
        {
            drawer = new CashDrawer
            {
                StockLocationId = id,
                OpeningBalance = 0m,
                OpeningDate = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime),
                CurrentCash = 0m,
                CreatedByUserId = actorId
            };
            db.CashDrawers.Add(drawer);
            await db.SaveChangesAsync(ct);
        }
        return drawer;
    }

    internal static void Append(AppDbContext db, CashDrawer drawer, CashMovement movement)
    {
        var newCash = drawer.CurrentCash + movement.Amount;
        StoreAccess.Money(newCash, "Location cash balance");
        drawer.CurrentCash = newCash;
        drawer.UpdatedAt = DateTimeOffset.UtcNow;
        drawer.UpdatedByUserId = movement.CreatedByUserId;
        movement.CashDrawer = drawer;
        movement.CashDrawerId = drawer.Id;
        movement.BalanceAfter = newCash;

        if (movement.CashSession != null)
        {
            movement.CashSession.ExpectedCash += movement.Amount;
            movement.CashSession.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else if (movement.CashSessionId != null)
        {
            var sess = db.CashSessions.Local.FirstOrDefault(s => s.Id == movement.CashSessionId.Value)
                ?? db.CashSessions.Find(movement.CashSessionId.Value);
            if (sess != null)
            {
                sess.ExpectedCash += movement.Amount;
                sess.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        db.CashMovements.Add(movement);
    }

    internal static void Append(AppDbContext db, CashSession session, CashMovement movement)
    {
        var drawer = session.CashDrawer ?? db.CashDrawers.Local.FirstOrDefault(d => d.Id == session.CashDrawerId)
            ?? db.CashDrawers.Find(session.CashDrawerId);
        if (drawer is null)
        {
            session.ExpectedCash += movement.Amount;
            session.UpdatedAt = DateTimeOffset.UtcNow;
            movement.CashSession = session;
            movement.CashSessionId = session.Id;
            db.CashMovements.Add(movement);
            return;
        }
        movement.CashSession = session;
        movement.CashSessionId = session.Id;
        session.ExpectedCash += movement.Amount;
        session.UpdatedAt = DateTimeOffset.UtcNow;
        Append(db, drawer, movement);
    }

    internal static async Task ReverseAsync(AppDbContext db, string kind, int id, int actorId, string reason, DateOnly activeDate, int? locationId, CancellationToken ct)
    {
        var original = await db.CashMovements.Include(move => move.CashDrawer).Include(move => move.CashSession)
            .Where(move => kind == "sale" ? move.Payment!.SaleId == id : move.SupplierPayment!.PurchaseId == id)
            .Where(move => move.ReversesMovementId == null
                && !db.CashMovements.Any(reverse => reverse.ReversesMovementId == move.Id))
            .ToListAsync(ct);
        foreach (var movement in original)
        {
            var drawerId = movement.CashDrawerId != 0 ? movement.CashDrawerId : (movement.CashSession?.CashDrawerId ?? 0);
            var drawer = movement.CashDrawer 
                ?? await db.CashDrawers.SingleOrDefaultAsync(d => d.Id == drawerId, ct)
                ?? await RequireDrawerAsync(db, actorId, ct, locationId ?? movement.CashSession?.StockLocationId);

            Append(db, drawer, new CashMovement
            {
                RequestId = Guid.NewGuid(),
                BusinessDate = activeDate,
                Kind = CashMovementKind.Reversal,
                Amount = -movement.Amount,
                Notes = reason,
                ReversesMovementId = movement.Id,
                CreatedByUserId = actorId
            });
        }
    }

    internal static Task<bool> IsAdminAsync(AppDbContext db, int actorId, CancellationToken ct) => db.UserRoles.AnyAsync(
        membership => membership.UserId == actorId && db.Roles.Any(role => role.Id == membership.RoleId && role.Name == AppRoles.Admin), ct);

    internal static void ValidateNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes) || notes.Length > 2000)
        {
            throw new ValidationException("Enter a description of up to 2,000 characters.");
        }
    }
}
