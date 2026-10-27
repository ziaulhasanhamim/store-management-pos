using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;

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
        var sessions = await db.CashSessions.Where(session => session.BusinessDate == date)
            .Select(session => new { session.CashDrawerId, session.Id, session.ClosedAt }).ToArrayAsync(ct);
        var selected = await db.Users.Where(user => user.Id == actorId).Select(user => user.CashLocationId).SingleAsync(ct);
        if (!locations.Any(location => location.Id == selected))
        {
            selected = locations.Length == 1 ? locations[0].Id : null;
        }
        var currentId = drawers.SingleOrDefault(drawer => drawer.StockLocationId == selected && selected != null)?.CurrentSessionId;
        var current = currentId is null ? null : await Rows(db, db.CashSessions.Where(session => session.Id == currentId)).SingleOrDefaultAsync(ct);
        var visible = db.CashSessions.Where(session => admin || session.CashDrawer!.StockLocationId == selected);
        var viewed = sessionId is null ? current : await Rows(db, visible.Where(session => session.Id == sessionId)).SingleOrDefaultAsync(ct);
        var recent = await Rows(db, visible.OrderByDescending(session => session.Id)).ToPageAsync(sessionPage, pageSize, ct);
        var movements = viewed is null ? null : await db.CashMovements.Where(move => move.CashSessionId == viewed.Id)
            .OrderByDescending(move => move.Id)
            .Select(move => new CashMovementRow(move.CreatedAt, move.Kind, move.Amount, move.Notes))
            .ToPageAsync(movementPage, pageSize, ct);
        var breakdown = viewed is null ? [] : await db.CashMovements.Where(move => move.CashSessionId == viewed.Id)
            .GroupBy(move => move.Kind).Select(group => new CashSummary(group.Key, group.Sum(move => move.Amount))).ToArrayAsync(ct);
        return new(current, viewed, movements?.Items ?? [], breakdown, recent.Items, admin)
        {
            MovementPagination = movements?.Pagination,
            SessionPagination = recent.Pagination,
            SelectedLocationId = selected,
            Locations = locations.Select(location =>
            {
                var drawer = drawers.SingleOrDefault(drawer => drawer.StockLocationId == location.Id);
                var session = sessions.Where(session => session.CashDrawerId == drawer?.Id).OrderByDescending(session => session.Id).FirstOrDefault();
                return new CashLocationRow(location.Id, location.Name, drawer?.CurrentCash ?? 0,
                    drawer?.OpeningDate != null, drawer?.CurrentSessionId, session?.ClosedAt != null);
            }).ToArray(),
            Unassigned = admin ? drawers.Where(drawer => drawer.StockLocationId == null && (drawer.CurrentCash != 0 || drawer.CurrentSessionId != null))
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

    public async Task SelectLocationAsync(ClaimsPrincipal actor, int locationId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);
        await ValidateLocationAsync(db, locationId, ct);
        var user = await db.Users.SingleAsync(user => user.Id == actorId, ct);
        user.CashLocationId = locationId;
        await db.SaveChangesAsync(ct);
    }

    public async Task<string?> SelectedLocationNameAsync(ClaimsPrincipal actor, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);
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
            var previous = await db.CashSessions.SingleOrDefaultAsync(session => session.RequestId == input.RequestId, ct);
            if (previous is not null)
            {
                if (previous.CreatedByUserId != actorId)
                {
                    throw new UnauthorizedAccessException();
                }
                if ((input.StockLocationId is { } requestedLocation && previous.StockLocationId != requestedLocation)
                    || (input.OpeningCash != 0 && previous.OpeningCash != input.OpeningCash))
                {
                    throw new ValidationException("This drawer opening was already saved with different values. Review the existing drawer.");
                }
                return previous.Id;
            }
            var locationId = input.StockLocationId ?? await ResolveLocationAsync(db, actorId, ct);
            await ValidateLocationAsync(db, locationId, ct);
            var date = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
            AccountingCalendar.ValidateExpected(date, WorkingDayRequest.Date);
            await AccountingCalendar.EnsureOpenAsync(db, date, ct);
            var drawer = await db.CashDrawers.SingleOrDefaultAsync(drawer => drawer.StockLocationId == locationId, ct);
            if (drawer?.CurrentSessionId != null || await db.CashSessions.AnyAsync(session => session.CashDrawerId == (drawer == null ? -1 : drawer.Id) && session.BusinessDate == date, ct))
            {
                throw new ValidationException("This location's drawer has already been opened for the working day. A counted closing is final.");
            }
            var day = await db.DayFinances.SingleOrDefaultAsync(day => day.Date == date, ct);
            if (day is null)
            {
                day = new DayFinance
                {
                    Date = date,
                    OpeningCash = await db.CashDrawers.Select(drawer => (decimal?)drawer.CurrentCash).SumAsync(ct) ?? 0,
                    CreatedByUserId = actorId
                };
                db.DayFinances.Add(day);
            }
            if (!await db.WorkingDays.AnyAsync(day => day.Id == 1, ct))
            {
                db.WorkingDays.Add(new WorkingDay { Id = 1, Date = date });
            }
            if (drawer is null)
            {
                if (input.OpeningCash > 0 && await db.DayFinances.AnyAsync(day => day.ClosedAt != null, ct))
                {
                    throw new ValidationException("New locations start at zero after initial setup. Fund this drawer using a cash transfer or a recorded cash-in.");
                }
                drawer = new CashDrawer
                {
                    StockLocationId = locationId,
                    OpeningBalance = input.OpeningCash,
                    OpeningDate = date,
                    CurrentCash = input.OpeningCash,
                    CreatedByUserId = actorId
                };
                day.OpeningCash += input.OpeningCash;
                db.CashDrawers.Add(drawer);
                await db.SaveChangesAsync(ct);
            }
            day.ExpectedCash = day.OpeningCash;
            var session = new CashSession
            {
                RequestId = input.RequestId,
                CashDrawer = drawer,
                StockLocationId = locationId,
                BusinessDate = date,
                OpenedAt = DateTimeOffset.UtcNow,
                OpeningCash = drawer.CurrentCash,
                ExpectedCash = drawer.CurrentCash,
                CreatedByUserId = actorId
            };
            db.CashSessions.Add(session);
            await db.SaveChangesAsync(ct);
            drawer.CurrentSession = session;
            drawer.UpdatedAt = DateTimeOffset.UtcNow;
            drawer.UpdatedByUserId = actorId;
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
            var session = await db.CashSessions.Include(session => session.CashDrawer)
                .SingleOrDefaultAsync(session => session.Id == input.SessionId, ct)
                ?? throw new ValidationException("Cash drawer not found.");
            if (session.ClosedAt is not null)
            {
                if (session.ActualClosingCash != actual || session.ClosingNotes != input.Notes?.Trim())
                {
                    throw new ValidationException("This drawer is already closed with a different count. An administrator must reopen it before correcting the count.");
                }
                return;
            }
            if (session.CashDrawer!.StockLocationId == null && !await IsAdminAsync(db, actorId, ct))
            {
                throw new UnauthorizedAccessException();
            }
            if (!await IsAdminAsync(db, actorId, ct) && session.CashDrawer.StockLocationId != await ResolveLocationAsync(db, actorId, ct))
            {
                throw new ValidationException("Select this location before closing its drawer.");
            }
            if (input.ExpectedCash is { } reviewed && reviewed != session.ExpectedCash)
            {
                throw new ValidationException("Cash movements changed after review. Count and review the drawer again.");
            }
            await AccountingCalendar.EnsureOpenAsync(db, session.BusinessDate, ct);
            var before = ClosureService.DrawerSnapshot(session);
            await BalanceIntegrityService.CheckDrawerAsync(db, session, ct);
            var difference = actual - session.ExpectedCash;
            if (difference != 0 && string.IsNullOrWhiteSpace(input.Notes))
            {
                throw new ValidationException("Add a note explaining the shortage or overage.");
            }
            session.ExpectedClosingCash = session.ExpectedCash;
            session.ActualClosingCash = actual;
            session.Difference = difference;
            session.ClosingNotes = input.Notes?.Trim();
            session.ClosedAt = DateTimeOffset.UtcNow;
            session.UpdatedAt = session.ClosedAt;
            session.UpdatedByUserId = actorId;
            session.CashDrawer.CurrentCash = actual;
            session.CashDrawer.CurrentSession = null;
            session.CashDrawer.CurrentSessionId = null;
            session.CashDrawer.UpdatedAt = session.ClosedAt;
            session.CashDrawer.UpdatedByUserId = actorId;
            db.RecordChanges.Add(new RecordChange
            {
                RequestId = Guid.NewGuid(),
                RecordType = "drawer",
                RecordId = session.Id,
                Action = "Close",
                Reason = input.Notes?.Trim() ?? "Drawer counted and closed",
                Before = before,
                After = ClosureService.DrawerSnapshot(session),
                CreatedByUserId = actorId
            });
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
            var existing = await db.CashDrawers.Include(drawer => drawer.CurrentSession)
                .SingleOrDefaultAsync(drawer => drawer.StockLocationId == input.LocationId, ct);
            if (existing is not null)
            {
                if (drawer.CurrentSessionId != null)
                {
                    throw new ValidationException("Count and close the legacy drawer before consolidating its cash into another location.");
                }
                if (drawer.CurrentCash == 0)
                {
                    return;
                }
                var date = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
                AccountingCalendar.ValidateExpected(date, WorkingDayRequest.Date);
                await AccountingCalendar.EnsureOpenAsync(db, date, ct);
                var destination = await RequireOpenAsync(db, actorId, ct, input.LocationId);
                var amount = drawer.CurrentCash;
                var source = new CashSession
                {
                    RequestId = Guid.NewGuid(),
                    CashDrawer = drawer,
                    BusinessDate = date,
                    OpeningCash = amount,
                    ExpectedCash = amount,
                    OpenedAt = DateTimeOffset.UtcNow,
                    CreatedByUserId = actorId
                };
                db.CashSessions.Add(source);
                var transfer = new CashTransfer
                {
                    RequestId = Guid.NewGuid(),
                    SourceDrawer = drawer,
                    DestinationDrawer = destination.CashDrawer,
                    BusinessDate = date,
                    Amount = amount,
                    Notes = "Legacy cash consolidated into selected location",
                    CreatedByUserId = actorId
                };
                db.CashTransfers.Add(transfer);
                foreach (var (session, value) in new[] { (source, -amount), (destination, amount) })
                {
                    Append(db, session, new CashMovement
                    {
                        RequestId = Guid.NewGuid(),
                        BusinessDate = date,
                        Kind = CashMovementKind.Transfer,
                        Amount = value,
                        CashTransfer = transfer,
                        Notes = transfer.Notes,
                        CreatedByUserId = actorId
                    });
                }
                source.ExpectedClosingCash = 0;
                source.ActualClosingCash = 0;
                source.Difference = 0;
                source.ClosedAt = DateTimeOffset.UtcNow;
                source.UpdatedByUserId = actorId;
                source.ClosingNotes = "Counted legacy cash moved in full to the selected location";
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
            var date = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
            AccountingCalendar.ValidateExpected(date, WorkingDayRequest.Date);
            await AccountingCalendar.EnsureOpenAsync(db, date, ct);
            var source = await RequireOpenAsync(db, actorId, ct, input.SourceLocationId);
            var destination = await RequireOpenAsync(db, actorId, ct, input.DestinationLocationId);
            var names = await db.StockLocations.Where(location => location.Id == input.SourceLocationId || location.Id == input.DestinationLocationId)
                .ToDictionaryAsync(location => location.Id, location => location.Name, ct);
            var transfer = new CashTransfer
            {
                RequestId = input.RequestId,
                SourceDrawer = source.CashDrawer,
                DestinationDrawer = destination.CashDrawer,
                BusinessDate = date,
                Amount = input.Amount,
                Notes = input.Notes.Trim(),
                CreatedByUserId = actorId
            };
            db.CashTransfers.Add(transfer);
            foreach (var (session, amount) in new[] { (source, -input.Amount), (destination, input.Amount) })
            {
                Append(db, session, new CashMovement
                {
                    RequestId = Guid.NewGuid(),
                    BusinessDate = date,
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
                if (previous.CreatedByUserId != actorId || previous.CashSessionId != input.SessionId
                    || previous.Amount != (input.Payout ? -input.Amount : input.Amount)
                    || previous.Kind != (input.Payout ? CashMovementKind.OwnerWithdrawal : CashMovementKind.OwnerContribution)
                    || previous.Notes != input.Notes.Trim())
                {
                    throw new ValidationException("This cash movement was already saved with different values. Start a new entry.");
                }
                return;
            }
            var session = await RequireOpenAsync(db, actorId, ct);
            if (session.Id != input.SessionId)
            {
                throw new ValidationException("The selected cash location changed. Reload before recording cash.");
            }
            var date = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
            AccountingCalendar.ValidateExpected(date, WorkingDayRequest.Date);
            await AccountingCalendar.EnsureOpenAsync(db, date, ct);
            Append(db, session, new CashMovement
            {
                BusinessDate = date,
                RequestId = input.RequestId,
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
        if (selected is { } id)
        {
            await ValidateLocationAsync(db, id, ct);
            return id;
        }
        var locations = await db.StockLocations.Where(location => location.CanSell && !location.IsDeleted)
            .Select(location => location.Id).Take(2).ToArrayAsync(ct);
        if (locations.Length != 1)
        {
            throw new ValidationException("Select the paying cash location on the Cash drawers page.");
        }
        return locations[0];
    }

    internal static async Task<CashSession> RequireOpenAsync(AppDbContext db, int actorId, CancellationToken ct, int? locationId = null)
    {
        var id = locationId ?? await ResolveLocationAsync(db, actorId, ct);
        await ValidateLocationAsync(db, id, ct);
        var drawer = db.CashDrawers.Local.SingleOrDefault(drawer => drawer.StockLocationId == id)
            ?? await db.CashDrawers.Include(drawer => drawer.CurrentSession).SingleOrDefaultAsync(drawer => drawer.StockLocationId == id, ct);
        var session = drawer?.CurrentSession;
        if (session is null || session.ClosedAt != null)
        {
            throw new ValidationException("Open this selling location's cash drawer before recording cash payments or refunds.");
        }
        var working = await db.WorkingDays.Where(day => day.Id == 1).Select(day => (DateOnly?)day.Date).SingleOrDefaultAsync(ct);
        if (working != null && session.BusinessDate != working)
        {
            throw new ValidationException("This drawer belongs to another working day. Count and close it before continuing.");
        }
        return session;
    }

    internal static void Append(AppDbContext db, CashSession session, CashMovement movement)
    {
        var expected = session.ExpectedCash + movement.Amount;
        StoreAccess.Money(expected, "Expected drawer cash");
        session.ExpectedCash = expected;
        session.CashDrawer!.CurrentCash += movement.Amount;
        StoreAccess.Money(session.CashDrawer.CurrentCash, "Location cash balance");
        session.CashDrawer.UpdatedAt = DateTimeOffset.UtcNow;
        session.CashDrawer.UpdatedByUserId = movement.CreatedByUserId;
        session.UpdatedAt = DateTimeOffset.UtcNow;
        movement.CashSession = session;
        db.CashMovements.Add(movement);
    }
    internal static async Task ReverseAsync(AppDbContext db, string kind, int id, int actorId, string reason, CancellationToken ct)
    {
        var original = await db.CashMovements.Include(move => move.CashSession).ThenInclude(session => session!.CashDrawer)
            .Where(move => kind == "sale" ? move.Payment!.SaleId == id : move.SupplierPayment!.PurchaseId == id)
            .Where(move => move.ReversesMovementId == null
                && !db.CashMovements.Any(reverse => reverse.ReversesMovementId == move.Id))
            .ToListAsync(ct);
        foreach (var movement in original)
        {
            if (movement.CashSession!.ClosedAt is not null)
            {
                throw new ValidationException("This transaction belongs to a reconciled cash drawer. Record a return or a current-shift correction instead of deleting it.");
            }
            await AccountingCalendar.EnsureOpenAsync(db, movement.BusinessDate, ct);
            Append(db, movement.CashSession, new CashMovement
            {
                RequestId = Guid.NewGuid(),
                Kind = CashMovementKind.Reversal,
                Amount = -movement.Amount,
                Notes = reason,
                BusinessDate = movement.BusinessDate,
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
