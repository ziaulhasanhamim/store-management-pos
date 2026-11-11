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
    public async Task<CashDrawerPage> ReadAsync(ClaimsPrincipal actor, int? sessionId = null, CancellationToken ct = default,
        int movementPage = 1, int sessionPage = 1, int pageSize = 30, int countsPage = 1, DateOnly? countsFrom = null, DateOnly? countsTo = null)
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

        CashCountRow? pendingCount = null;
        if (pendingCountEntity is not null && selectedDrawer is not null)
        {
            var locName = selectedDrawer.StockLocationId != null ? locations.FirstOrDefault(l => l.Id == selectedDrawer.StockLocationId)?.Name ?? "Drawer" : "Drawer";
            var liveExp = await CalculateLiveBalanceAsync(db, selectedDrawer, pendingCountEntity.CountedAt, ct, configuration, excludeCountId: pendingCountEntity.Id);
            var liveVar = pendingCountEntity.CountedCash - liveExp;
            var hasDrift = Math.Round(pendingCountEntity.ExpectedCash, 2) != Math.Round(liveExp, 2);

            if (hasDrift)
            {
                pendingCountEntity.ExpectedCash = liveExp;
                await db.SaveChangesAsync(ct);
            }

            pendingCount = new CashCountRow(
                pendingCountEntity.Id,
                pendingCountEntity.StockLocationId,
                locName,
                pendingCountEntity.CountedByUser?.UserName ?? "Staff",
                pendingCountEntity.CountedAt,
                pendingCountEntity.CountedCash,
                liveExp,
                liveVar,
                pendingCountEntity.Status,
                pendingCountEntity.Notes,
                pendingCountEntity.CompletedAt,
                pendingCountEntity.VarianceAcceptedAt,
                pendingCountEntity.IsVoided)
            {
                RecordedAt = pendingCountEntity.RecordedAt,
                AcceptedVariance = pendingCountEntity.AcceptedVariance,
                LiveExpectedCash = liveExp,
                LiveVariance = liveVar
            };
        }

        // Last completed or accepted count
        var lastCountEntity = drawerId is null ? null : await db.CashCounts.AsNoTracking()
            .Where(c => c.CashDrawerId == drawerId.Value && (c.Status == CashCountStatus.Completed || c.Status == CashCountStatus.VarianceAccepted || c.Status == CashCountStatus.Accepted) && !c.IsVoided)
            .Include(c => c.CountedByUser)
            .OrderByDescending(c => c.BusinessDate)
            .ThenByDescending(c => c.CountedAt)
            .ThenByDescending(c => c.Id)
            .FirstOrDefaultAsync(ct);

        CashCountRow? lastCount = null;
        if (lastCountEntity is not null && selectedDrawer is not null)
        {
            var locName = selectedDrawer.StockLocationId != null ? locations.FirstOrDefault(l => l.Id == selectedDrawer.StockLocationId)?.Name ?? "Drawer" : "Drawer";
            var liveExp = await CalculateLiveBalanceAsync(db, selectedDrawer, lastCountEntity.CountedAt, ct, configuration, excludeCountId: lastCountEntity.Id);
            var liveVar = lastCountEntity.CountedCash - liveExp;
            lastCount = new CashCountRow(
                lastCountEntity.Id,
                lastCountEntity.StockLocationId,
                locName,
                lastCountEntity.CountedByUser?.UserName ?? "Staff",
                lastCountEntity.CountedAt,
                lastCountEntity.CountedCash,
                lastCountEntity.ExpectedCash,
                lastCountEntity.Difference,
                lastCountEntity.Status,
                lastCountEntity.Notes,
                lastCountEntity.CompletedAt,
                lastCountEntity.VarianceAcceptedAt,
                lastCountEntity.IsVoided)
            {
                RecordedAt = lastCountEntity.RecordedAt,
                AcceptedVariance = lastCountEntity.AcceptedVariance,
                LiveExpectedCash = liveExp,
                LiveVariance = liveVar
            };
        }

        // Paginated & date-filtered cash counts history
        var countsBaseQuery = db.CashCounts.AsNoTracking()
            .Where(c => admin || c.StockLocationId == selected);

        if (countsFrom.HasValue)
        {
            countsBaseQuery = countsBaseQuery.Where(c => c.BusinessDate >= countsFrom.Value);
        }
        if (countsTo.HasValue)
        {
            countsBaseQuery = countsBaseQuery.Where(c => c.BusinessDate <= countsTo.Value);
        }

        var countsTotal = await countsBaseQuery.CountAsync(ct);
        var countsPageSize = 20;
        var countsEffectivePage = Math.Max(1, countsPage);
        var countHistoryEntities = await countsBaseQuery
            .Include(c => c.StockLocation)
            .Include(c => c.CountedByUser)
            .Include(c => c.CashDrawer)
            .OrderByDescending(c => c.BusinessDate)
            .ThenByDescending(c => c.CountedAt)
            .ThenByDescending(c => c.Id)
            .Skip((countsEffectivePage - 1) * countsPageSize)
            .Take(countsPageSize)
            .ToListAsync(ct);

        var countHistoryList = new List<CashCountRow>();
        foreach (var c in countHistoryEntities)
        {
            var drw = c.CashDrawer ?? (selectedDrawer?.Id == c.CashDrawerId ? selectedDrawer : drawers.FirstOrDefault(d => d.Id == c.CashDrawerId));
            var liveExp = drw is not null ? await CalculateLiveBalanceAsync(db, drw, c.CountedAt, ct, configuration, excludeCountId: c.Id) : c.ExpectedCash;
            var liveVar = c.CountedCash - liveExp;

            countHistoryList.Add(new CashCountRow(
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
                c.IsVoided)
            {
                RecordedAt = c.RecordedAt,
                AcceptedVariance = c.AcceptedVariance,
                LiveExpectedCash = liveExp,
                LiveVariance = liveVar
            });
        }
        var countHistory = countHistoryList.ToArray();
        var countsPagination = new PageInfo(countsEffectivePage, countsPageSize, countsTotal);

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
        var todayStartUtc = AccountingCalendar.At(date, TimeOnly.MinValue, configuration);
        var sessions = await db.CashSessions.Where(session => session.BusinessDate == date || session.OpenedAt >= todayStartUtc)
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

        var liveDrawerCash = selectedDrawer != null ? await CalculateLiveBalanceAsync(db, selectedDrawer, null, ct, configuration) : 0m;
        if (selectedDrawer != null && selectedDrawer.CurrentCash != liveDrawerCash)
        {
            var trackedDrawer = await db.CashDrawers.FindAsync([selectedDrawer.Id], ct);
            if (trackedDrawer != null)
            {
                trackedDrawer.CurrentCash = liveDrawerCash;
                await db.SaveChangesAsync(ct);
            }
        }

        return new(current, viewed ?? current, movements?.Items ?? [], breakdown, recent.Items, admin)
        {
            CurrentCash = liveDrawerCash,
            MovementPagination = movements?.Pagination,
            SessionPagination = recent.Pagination,
            SelectedLocationId = selected,
            PendingCount = pendingCount,
            LastCount = lastCount,
            CountHistory = countHistory,
            CountsPagination = countsPagination,
            CountsFromDate = countsFrom,
            CountsToDate = countsTo,
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
        BusinessDate = session.BusinessDate
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
            var isAdmin = await IsAdminAsync(db, actorId, ct);

            var now = DateTimeOffset.UtcNow;
            var recordedAt = now;
            DateTimeOffset effectiveCountedAt;
            DateOnly effectiveDate;

            // Find last accepted count on this drawer
            var lastAccepted = await db.CashCounts.AsNoTracking()
                .Where(c => c.CashDrawerId == drawer.Id
                    && (c.Status == CashCountStatus.Completed || c.Status == CashCountStatus.VarianceAccepted || c.Status == CashCountStatus.Accepted)
                    && !c.IsVoided)
                .OrderByDescending(c => c.BusinessDate)
                .ThenByDescending(c => c.CountedAt)
                .ThenByDescending(c => c.Id)
                .FirstOrDefaultAsync(ct);

            if (isAdmin && (input.IsBackdate || input.CountedAt.HasValue))
            {
                if (!input.CountedAt.HasValue)
                {
                    throw new ValidationException("Select the date and time when physical cash was counted.");
                }
                effectiveCountedAt = input.CountedAt.Value.ToUniversalTime();

                var businessZone = AccountingCalendar.Zone(configuration);
                if (effectiveCountedAt > now)
                {
                    throw new ValidationException("Count date and time cannot be in the future.");
                }
                effectiveDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(effectiveCountedAt, businessZone).DateTime);
                if (lastAccepted is not null &&
                    (effectiveDate < lastAccepted.BusinessDate ||
                     (effectiveDate == lastAccepted.BusinessDate && effectiveCountedAt <= lastAccepted.CountedAt)))
                {
                    var lastTimeStr = TimeZoneInfo.ConvertTime(lastAccepted.CountedAt, businessZone).ToString("dd MMM yyyy, hh:mm tt");
                    throw new ValidationException($"A backdated count cannot be on or earlier than the last accepted count on {lastTimeStr}. Accepted counts are permanent anchors.");
                }
            }
            else
            {
                effectiveCountedAt = now;
                effectiveDate = AccountingCalendar.Today(configuration);
            }

            // Expected cash AT THE TIME of the count (not now!)
            var expected = await CalculateLiveBalanceAsync(db, drawer, effectiveCountedAt, ct, configuration);
            var difference = counted - expected;
            if (difference != 0 && string.IsNullOrWhiteSpace(input.Notes))
            {
                throw new ValidationException("Add a note explaining the shortage or overage.");
            }

            // Zero variance gets accepted automatically as an anchor
            var isAutoAccepted = (difference == 0);
            var status = isAutoAccepted ? CashCountStatus.Completed : CashCountStatus.PendingReview;

            var count = new CashCount
            {
                RequestId = input.RequestId != Guid.Empty ? input.RequestId : Guid.NewGuid(),
                CashDrawerId = drawer.Id,
                StockLocationId = locationId,
                BusinessDate = effectiveDate,
                CountedAt = effectiveCountedAt,
                RecordedAt = recordedAt,
                CountedByUserId = actorId,
                CountedCash = counted,
                ExpectedCash = expected,
                AcceptedVariance = isAutoAccepted ? 0m : null,
                Notes = input.Notes?.Trim(),
                Status = status,
                CompletedAt = isAutoAccepted ? now : null,
                CompletedByUserId = isAutoAccepted ? actorId : null,
                VarianceAcceptedAt = null,
                VarianceAcceptedByUserId = null
            };
            db.CashCounts.Add(count);

            if (isAutoAccepted)
            {
                drawer.LastCountedAt = effectiveCountedAt;
                drawer.LastCountedCash = counted;
                var today = AccountingCalendar.Today(configuration);
                var deltaAfter = await db.CashMovements.AsNoTracking()
                    .Where(m => (m.CashDrawerId == drawer.Id || (m.CashSession != null && m.CashSession.CashDrawerId == drawer.Id))
                        && (m.BusinessDate > effectiveDate || (m.BusinessDate == effectiveDate && m.CreatedAt > effectiveCountedAt))
                        && (m.BusinessDate < today || (m.BusinessDate == today && m.CreatedAt <= now))
                        && m.Kind != CashMovementKind.Variance)
                    .SumAsync(m => (decimal?)m.Amount, ct) ?? 0m;
                drawer.CurrentCash = counted + deltaAfter;
            }
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
            input.Confirmed = true;
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            var count = await db.CashCounts.Include(c => c.CashDrawer).Include(c => c.VarianceMovement)
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

            // Derived live variance at the time of the count:
            // variance = counted - (previous accepted anchor's counted + net movements between the two anchors)
            var liveExpected = await CalculateLiveBalanceAsync(db, count.CashDrawer!, count.CountedAt, ct, configuration, excludeCountId: count.Id);
            var liveVariance = count.CountedCash - liveExpected;

            count.AcceptedVariance = liveVariance;
            count.ExpectedCash = liveExpected;
            count.Status = CashCountStatus.VarianceAccepted;
            count.VarianceAcceptedAt = now; // Admin approvals cannot be backdated
            count.VarianceAcceptedByUserId = actorId;
            count.CompletedAt = now;
            count.CompletedByUserId = actorId;
            count.Notes = note;
            count.UpdatedAt = now;
            count.UpdatedByUserId = actorId;

            // Post or update the physical cash count variance adjustment movement so statements reconcile:
            // Opening + Movements = Current Cash
            if (liveVariance != 0)
            {
                var varianceDesc = liveVariance < 0
                    ? $"Physical cash count #{count.Id}: missing BDT {Math.Abs(liveVariance):N2} (Note: {note})"
                    : $"Physical cash count #{count.Id}: extra BDT {liveVariance:N2} (Note: {note})";

                if (count.VarianceMovement is not null)
                {
                    count.VarianceMovement.Amount = liveVariance;
                    count.VarianceMovement.Notes = varianceDesc;
                    count.VarianceMovement.BusinessDate = count.BusinessDate;
                    count.VarianceMovement.CreatedAt = count.CountedAt;
                    count.VarianceMovement.UpdatedAt = now;
                    count.VarianceMovement.UpdatedByUserId = actorId;
                }
                else
                {
                    var varianceMove = new CashMovement
                    {
                        RequestId = Guid.NewGuid(),
                        CashDrawerId = count.CashDrawerId,
                        BusinessDate = count.BusinessDate,
                        CreatedAt = count.CountedAt,
                        Kind = CashMovementKind.Variance,
                        Amount = liveVariance,
                        Notes = varianceDesc,
                        CreatedByUserId = actorId
                    };
                    db.CashMovements.Add(varianceMove);
                    count.VarianceMovement = varianceMove;
                }
            }
            else if (count.VarianceMovement is not null)
            {
                db.CashMovements.Remove(count.VarianceMovement);
                count.VarianceMovement = null;
                count.VarianceMovementId = null;
            }

            if (count.CashDrawer != null)
            {
                count.CashDrawer.LastCountedAt = count.CountedAt;
                count.CashDrawer.LastCountedCash = count.CountedCash;
                count.CashDrawer.CurrentCash = await CalculateLiveBalanceAsync(db, count.CashDrawer, null, ct, configuration);
                count.CashDrawer.UpdatedAt = now;
                count.CashDrawer.UpdatedByUserId = actorId;
            }

            db.RecordChanges.Add(new RecordChange
            {
                RequestId = Guid.NewGuid(),
                RecordType = "cash_count",
                RecordId = count.Id,
                Action = "AcceptVariance",
                Reason = note,
                Before = $"{{\"Status\": \"PendingReview\", \"Expected\": {count.ExpectedCash}}}",
                After = $"{{\"Status\": \"VarianceAccepted\", \"AcceptedVariance\": {liveVariance}, \"LiveDrawerCash\": {count.CashDrawer?.CurrentCash}}}",
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

            // If a legacy variance movement was posted, reverse it
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

            if (count.CashDrawer != null)
            {
                var remainingAnchor = await db.CashCounts.AsNoTracking()
                    .Where(c => c.CashDrawerId == count.CashDrawerId && (c.Status == CashCountStatus.Completed || c.Status == CashCountStatus.VarianceAccepted || c.Status == CashCountStatus.Accepted) && !c.IsVoided && c.Id != count.Id)
                    .OrderByDescending(c => c.BusinessDate)
                    .ThenByDescending(c => c.CountedAt)
                    .ThenByDescending(c => c.Id)
                    .FirstOrDefaultAsync(ct);

                count.CashDrawer.LastCountedAt = remainingAnchor?.CountedAt;
                count.CashDrawer.LastCountedCash = remainingAnchor?.CountedCash;
                count.CashDrawer.CurrentCash = await CalculateLiveBalanceAsync(db, count.CashDrawer, null, ct, configuration);
                count.CashDrawer.UpdatedAt = DateTimeOffset.UtcNow;
                count.CashDrawer.UpdatedByUserId = actorId;
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }, ct);
    }

    public async Task DeleteCountAsync(ClaimsPrincipal actor, int countId, CancellationToken ct = default)
    {
        await StoreOperation.RunAsync(async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            var count = await db.CashCounts.Include(c => c.CashDrawer).Include(c => c.VarianceMovement)
                .SingleOrDefaultAsync(c => c.Id == countId, ct)
                ?? throw new ValidationException("Cash count not found.");

            var wasAccepted = count.Status == CashCountStatus.Completed
                || count.Status == CashCountStatus.VarianceAccepted
                || count.Status == CashCountStatus.Accepted;

            var drawer = count.CashDrawer
                ?? await db.CashDrawers.SingleOrDefaultAsync(d => d.Id == count.CashDrawerId, ct);

            if (count.VarianceMovement is not null)
            {
                db.CashMovements.Remove(count.VarianceMovement);
            }

            db.CashCounts.Remove(count);
            await db.SaveChangesAsync(ct);

            if (drawer != null)
            {
                var remainingAnchor = await db.CashCounts.AsNoTracking()
                    .Where(c => c.CashDrawerId == drawer.Id && c.Id != countId && (c.Status == CashCountStatus.Completed || c.Status == CashCountStatus.VarianceAccepted || c.Status == CashCountStatus.Accepted) && !c.IsVoided)
                    .OrderByDescending(c => c.BusinessDate)
                    .ThenByDescending(c => c.CountedAt)
                    .ThenByDescending(c => c.Id)
                    .FirstOrDefaultAsync(ct);

                drawer.LastCountedAt = remainingAnchor?.CountedAt;
                drawer.LastCountedCash = remainingAnchor?.CountedCash;
                if (wasAccepted)
                {
                    drawer.CurrentCash = await CalculateLiveBalanceAsync(db, drawer, null, ct, configuration);
                }
                drawer.UpdatedAt = DateTimeOffset.UtcNow;
                drawer.UpdatedByUserId = actorId;
                await db.SaveChangesAsync(ct);
            }

            await transaction.CommitAsync(ct);
        }, ct);
    }

    public static async Task<decimal> CalculateLiveBalanceAsync(
        AppDbContext db,
        CashDrawer drawer,
        DateTimeOffset? asOf = null,
        CancellationToken ct = default,
        IConfiguration? configuration = null,
        int? excludeCountId = null)
    {
        var effectiveAsOf = asOf ?? DateTimeOffset.UtcNow;
        var zone = AccountingCalendar.Zone(configuration);
        var asOfDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(effectiveAsOf, zone).DateTime);

        // Check local tracked entities first
        var localAnchorQuery = db.CashCounts.Local
            .Where(c => c.CashDrawerId == drawer.Id
                && (c.Status == CashCountStatus.Completed || c.Status == CashCountStatus.VarianceAccepted || c.Status == CashCountStatus.Accepted)
                && !c.IsVoided
                && (c.BusinessDate < asOfDate || (c.BusinessDate == asOfDate && c.CountedAt <= effectiveAsOf)));

        if (excludeCountId.HasValue)
        {
            localAnchorQuery = localAnchorQuery.Where(c => c.Id != excludeCountId.Value
                && !(c.BusinessDate == asOfDate && c.CountedAt == effectiveAsOf && c.Id > excludeCountId.Value));
        }

        var localAnchor = localAnchorQuery
            .OrderByDescending(c => c.BusinessDate)
            .ThenByDescending(c => c.CountedAt)
            .ThenByDescending(c => c.Id)
            .Select(c => new { c.BusinessDate, c.CountedAt, c.Id, c.CountedCash })
            .FirstOrDefault();

        var dbAnchorQuery = db.CashCounts.AsNoTracking()
            .Where(c => c.CashDrawerId == drawer.Id
                && (c.Status == CashCountStatus.Completed || c.Status == CashCountStatus.VarianceAccepted || c.Status == CashCountStatus.Accepted)
                && !c.IsVoided
                && (c.BusinessDate < asOfDate || (c.BusinessDate == asOfDate && c.CountedAt <= effectiveAsOf)));

        if (excludeCountId.HasValue)
        {
            dbAnchorQuery = dbAnchorQuery.Where(c => c.Id != excludeCountId.Value
                && !(c.BusinessDate == asOfDate && c.CountedAt == effectiveAsOf && c.Id > excludeCountId.Value));
        }

        var dbAnchor = localAnchor is not null ? null : await dbAnchorQuery
            .OrderByDescending(c => c.BusinessDate)
            .ThenByDescending(c => c.CountedAt)
            .ThenByDescending(c => c.Id)
            .Select(c => new { c.BusinessDate, c.CountedAt, c.Id, c.CountedCash })
            .FirstOrDefaultAsync(ct);

        var anchor = localAnchor ?? dbAnchor;

        decimal baseBalance = 0m;
        if (anchor != null)
        {
            baseBalance = anchor.CountedCash;
        }
        else
        {
            var openingCash = drawer.OpeningBalance;
            if (openingCash == 0m && drawer.CurrentCash != 0m)
            {
                var movesSum = await db.CashMovements.AsNoTracking()
                    .Where(m => m.CashDrawerId == drawer.Id || (m.CashSession != null && m.CashSession.CashDrawerId == drawer.Id))
                    .SumAsync(m => (decimal?)m.Amount, ct) ?? 0m;
                var countsSum = await db.CashSessions.AsNoTracking()
                    .Where(s => s.CashDrawerId == drawer.Id && s.ClosedAt != null && s.Difference != null && s.Difference != 0
                        && !db.CashMovements.Any(m => (m.CashDrawerId == drawer.Id || (m.CashSession != null && m.CashSession.CashDrawerId == drawer.Id)) && m.Kind == CashMovementKind.Variance && m.CreatedAt == s.ClosedAt))
                    .SumAsync(s => s.Difference, ct) ?? 0m;
                openingCash = drawer.CurrentCash - movesSum - countsSum;
            }

            if (openingCash != 0m && (drawer.OpeningDate == null || drawer.OpeningDate.Value <= asOfDate))
            {
                baseBalance = openingCash;
            }
        }

        // Movement ordering is (BusinessDate, CreatedAt, Id). A movement belongs to an interval by that order.
        decimal delta;
        if (anchor != null)
        {
            var aDate = anchor.BusinessDate;
            var aTime = anchor.CountedAt;
            var aId = anchor.Id;

            delta = await db.CashMovements.AsNoTracking()
                .Where(m => (m.CashDrawerId == drawer.Id || (m.CashSession != null && m.CashSession.CashDrawerId == drawer.Id))
                    && (m.BusinessDate > aDate || (m.BusinessDate == aDate && (m.CreatedAt > aTime || (m.CreatedAt == aTime && m.Id > aId))))
                    && (m.BusinessDate < asOfDate || (m.BusinessDate == asOfDate && m.CreatedAt <= effectiveAsOf))
                    && m.Kind != CashMovementKind.Variance)
                .SumAsync(m => (decimal?)m.Amount, ct) ?? 0m;
        }
        else
        {
            delta = await db.CashMovements.AsNoTracking()
                .Where(m => (m.CashDrawerId == drawer.Id || (m.CashSession != null && m.CashSession.CashDrawerId == drawer.Id))
                    && (m.BusinessDate < asOfDate || (m.BusinessDate == asOfDate && m.CreatedAt <= effectiveAsOf))
                    && m.Kind != CashMovementKind.Variance)
                .SumAsync(m => (decimal?)m.Amount, ct) ?? 0m;
        }

        return baseBalance + delta;
    }

    public async Task<decimal> GetExpectedAtAsync(ClaimsPrincipal actor, int locationId, DateTimeOffset asOf, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);
        if (locationId <= 0)
        {
            try
            {
                locationId = await ResolveLocationAsync(db, actorId, ct);
            }
            catch
            {
                // Fall back to primary selling drawer if multiple locations exist for unassigned admin
            }
        }
        var drawer = locationId > 0
            ? await db.CashDrawers.AsNoTracking().FirstOrDefaultAsync(d => d.StockLocationId == locationId, ct)
            : null;
        drawer ??= await db.CashDrawers.AsNoTracking().FirstOrDefaultAsync(ct)
            ?? throw new ValidationException("Cash drawer not found for location.");
        return await CalculateLiveBalanceAsync(db, drawer, asOf, ct, configuration);
    }

    public static async Task<decimal> GetCashBalanceAtAsync(AppDbContext db, int drawerId, DateTimeOffset asOf, CancellationToken ct = default, IConfiguration? configuration = null)
    {
        var drawer = await db.CashDrawers.AsNoTracking().SingleOrDefaultAsync(d => d.Id == drawerId, ct);
        if (drawer is null) return 0m;
        return await CalculateLiveBalanceAsync(db, drawer, asOf, ct, configuration);
    }

    public async Task<decimal> GetCashBalanceAtAsync(ClaimsPrincipal actor, int drawerId, DateTimeOffset asOf, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
        return await GetCashBalanceAtAsync(db, drawerId, asOf, ct, configuration);
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
                RecordedAt = now,
                CountedByUserId = actorId,
                CountedCash = actual,
                ExpectedCash = drawer.CurrentCash,
                AcceptedVariance = difference,
                Notes = input.Notes?.Trim(),
                Status = difference == 0 ? CashCountStatus.Completed : CashCountStatus.VarianceAccepted,
                CompletedAt = now,
                CompletedByUserId = actorId,
                VarianceAcceptedAt = now,
                VarianceAcceptedByUserId = actorId
            };
            db.CashCounts.Add(count);

            drawer.LastCountedAt = now;
            drawer.LastCountedCash = actual;
            drawer.CurrentCash = actual;
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
                OpeningDate = AccountingCalendar.Today(null),
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
        drawer = db.CashDrawers.Local.FirstOrDefault(d => d.Id == drawer.Id) ?? drawer;

        // An accepted count is a permanent anchor: drawer balance after it = its counted amount + movements after it.
        // Retroactive historical entries (on or before the latest accepted count) do NOT change current drawer cash!
        var latestAnchor = db.CashCounts.Local
            .Where(c => c.CashDrawerId == drawer.Id && (c.Status == CashCountStatus.Completed || c.Status == CashCountStatus.VarianceAccepted || c.Status == CashCountStatus.Accepted) && !c.IsVoided)
            .OrderByDescending(c => c.BusinessDate)
            .ThenByDescending(c => c.CountedAt)
            .ThenByDescending(c => c.Id)
            .FirstOrDefault()
            ?? db.CashCounts.AsNoTracking()
                .Where(c => c.CashDrawerId == drawer.Id && (c.Status == CashCountStatus.Completed || c.Status == CashCountStatus.VarianceAccepted || c.Status == CashCountStatus.Accepted) && !c.IsVoided)
                .OrderByDescending(c => c.BusinessDate)
                .ThenByDescending(c => c.CountedAt)
                .ThenByDescending(c => c.Id)
                .FirstOrDefault();

        var isHistorical = latestAnchor != null &&
            (movement.BusinessDate < latestAnchor.BusinessDate ||
             (movement.BusinessDate == latestAnchor.BusinessDate && movement.CreatedAt <= latestAnchor.CountedAt));

        if (!isHistorical)
        {
            var newCash = drawer.CurrentCash + movement.Amount;
            StoreAccess.Money(newCash, "Location cash balance");
            drawer.CurrentCash = newCash;
            drawer.UpdatedAt = DateTimeOffset.UtcNow;
            drawer.UpdatedByUserId = movement.CreatedByUserId;
            movement.BalanceAfter = newCash;
        }
        else
        {
            movement.BalanceAfter = drawer.CurrentCash;
        }

        movement.CashDrawer = drawer;
        movement.CashDrawerId = drawer.Id;

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

            // Reversal takes the original entry's business date and time (+1ms), falling in the same interval
            Append(db, drawer, new CashMovement
            {
                RequestId = Guid.NewGuid(),
                BusinessDate = movement.BusinessDate,
                CreatedAt = movement.CreatedAt.AddMilliseconds(1),
                Kind = CashMovementKind.Reversal,
                Amount = -movement.Amount,
                Notes = $"Deletion of {kind} #{id}: {reason}",
                ReversesMovementId = movement.Id,
                CreatedByUserId = actorId
            });

            await db.SaveChangesAsync(ct);

            await SyncCountsAndDrawerBalanceAsync(db, drawer, movement.CreatedAt, ct);
        }
    }

    internal static async Task SyncCountsAndDrawerBalanceAsync(
        AppDbContext db,
        CashDrawer drawer,
        DateOnly afterDate,
        DateTimeOffset afterTime,
        CancellationToken ct,
        IConfiguration? configuration = null)
    {
        var affectedCounts = await db.CashCounts
            .Include(c => c.VarianceMovement)
            .Where(c => c.CashDrawerId == drawer.Id
                && (c.Status == CashCountStatus.Completed || c.Status == CashCountStatus.VarianceAccepted || c.Status == CashCountStatus.Accepted)
                && !c.IsVoided
                && (c.BusinessDate > afterDate || (c.BusinessDate == afterDate && c.CountedAt >= afterTime)))
            .OrderBy(c => c.BusinessDate)
            .ThenBy(c => c.CountedAt)
            .ThenBy(c => c.Id)
            .ToListAsync(ct);

        foreach (var count in affectedCounts)
        {
            var liveExp = await CalculateLiveBalanceAsync(db, drawer, count.CountedAt, ct, configuration, excludeCountId: count.Id);
            var liveVar = count.CountedCash - liveExp;

            if (count.VarianceMovement is not null)
            {
                if (liveVar != 0)
                {
                    count.VarianceMovement.Amount = liveVar;
                    count.VarianceMovement.Notes = $"Physical cash count #{count.Id}: {(liveVar < 0 ? $"missing BDT {Math.Abs(liveVar):N2}" : $"extra BDT {liveVar:N2}")}";
                }
                else
                {
                    count.VarianceMovement.Amount = 0m;
                    count.VarianceMovement.Notes = $"Physical cash count #{count.Id}: balanced (retroactively corrected)";
                }
            }
            else if (liveVar != 0)
            {
                var varianceDesc = liveVar < 0
                    ? $"Physical cash count #{count.Id}: missing BDT {Math.Abs(liveVar):N2}"
                    : $"Physical cash count #{count.Id}: extra BDT {liveVar:N2}";

                var varianceMove = new CashMovement
                {
                    RequestId = Guid.NewGuid(),
                    CashDrawerId = count.CashDrawerId,
                    BusinessDate = count.BusinessDate,
                    CreatedAt = count.CountedAt,
                    Kind = CashMovementKind.Variance,
                    Amount = liveVar,
                    Notes = varianceDesc,
                    CreatedByUserId = count.CountedByUserId
                };
                db.CashMovements.Add(varianceMove);
                count.VarianceMovement = varianceMove;
            }
        }

        drawer.CurrentCash = await CalculateLiveBalanceAsync(db, drawer, null, ct, configuration);
        drawer.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    internal static Task SyncCountsAndDrawerBalanceAsync(
        AppDbContext db,
        CashDrawer drawer,
        DateTimeOffset afterTime,
        CancellationToken ct,
        IConfiguration? configuration = null)
    {
        var zone = AccountingCalendar.Zone(configuration);
        var afterDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(afterTime, zone).DateTime);
        return SyncCountsAndDrawerBalanceAsync(db, drawer, afterDate, afterTime, ct, configuration);
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
